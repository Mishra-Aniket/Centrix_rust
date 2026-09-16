namespace LectureAgent.Infrastructure.Security;

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads (and creates) protected copies of credential files so secrets are never
/// stored as plain text at rest. Convention: for credentials at path P, the protected
/// form lives at P + ".protected"; OpenRead(P) transparently resolves either.
/// Windows uses DPAPI (machine scope, so both the LocalSystem service and the
/// desktop app can decrypt). macOS/Linux have no in-box DPAPI equivalent, so the
/// protected file is written with owner-only permissions (0600) instead.
/// </summary>
public interface ICredentialProtector
{
    /// <summary>Opens the credential file for reading, decrypting a .protected file if present.</summary>
    Stream OpenRead(string plaintextPath);

    /// <summary>
    /// If a plain-text credential file exists and no protected copy does, writes the
    /// protected copy, verifies it round-trips, then deletes the plain-text file.
    /// Safe to call on every startup.
    /// </summary>
    void ProtectInPlace(string plaintextPath);
}

public sealed class CredentialProtector : ICredentialProtector
{
    private const string WindowsMarker = "DPAPI1:";
    private const string PlainMarker = "PLAIN1:";

    private readonly ILogger<CredentialProtector>? _logger;

    public CredentialProtector(ILogger<CredentialProtector>? logger = null)
    {
        _logger = logger;
    }

    public Stream OpenRead(string plaintextPath)
    {
        if (File.Exists(plaintextPath))
            return new FileStream(plaintextPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var protectedPath = plaintextPath + ".protected";
        if (!File.Exists(protectedPath))
            throw new FileNotFoundException(
                $"Credential file not found (looked for '{plaintextPath}' and '{protectedPath}')", plaintextPath);

        var raw = File.ReadAllBytes(protectedPath);
        _logger?.LogInformation("Loaded credentials from protected file {ProtectedPath}", protectedPath);
        return Decode(raw, protectedPath);
    }

    public void ProtectInPlace(string plaintextPath)
    {
        if (!File.Exists(plaintextPath))
            return;

        var protectedPath = plaintextPath + ".protected";

        if (File.Exists(protectedPath) && CanDecode(protectedPath))
        {
            // Already protected (and readable); remove the redundant plain-text copy.
            File.Delete(plaintextPath);
            _logger?.LogInformation("Removed plain-text credentials at {Path}; protected copy already exists", plaintextPath);
            return;
        }

        var plaintext = File.ReadAllBytes(plaintextPath);
        File.WriteAllBytes(protectedPath, Encode(plaintext));

        // Verify before deleting the original: a broken protected file must never
        // destroy the only copy of the credentials.
        using (var verifyStream = Decode(File.ReadAllBytes(protectedPath), protectedPath))
        {
            var roundTripped = new MemoryStream();
            verifyStream.CopyTo(roundTripped);
            if (!roundTripped.ToArray().SequenceEqual(plaintext))
                throw new InvalidOperationException("Protected credentials failed round-trip verification; original left untouched");
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(protectedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
                // Non-unix platform; nothing to tighten.
            }
        }

        File.Delete(plaintextPath);
        _logger?.LogInformation("Credentials encrypted at rest: {ProtectedPath} (plain-text copy removed)", protectedPath);
    }

    private bool CanDecode(string protectedPath)
    {
        try
        {
            using var stream = Decode(File.ReadAllBytes(protectedPath), protectedPath);
            return stream.Length >= 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                "Existing protected file {ProtectedPath} is unreadable ({Reason}); it will be rewritten from the plain-text copy",
                protectedPath, ex.Message);
            return false;
        }
    }

    private byte[] Encode(byte[] plaintext)
    {
        var payload = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.LocalMachine)
            : plaintext;

        var marker = Encoding.ASCII.GetBytes(OperatingSystem.IsWindows() ? WindowsMarker : PlainMarker);
        var result = new byte[marker.Length + payload.Length];
        Buffer.BlockCopy(marker, 0, result, 0, marker.Length);
        Buffer.BlockCopy(payload, 0, result, marker.Length, payload.Length);
        return result;
    }

    private static Stream Decode(byte[] raw, string protectedPath)
    {
        if (StartsWith(raw, WindowsMarker))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException($"'{protectedPath}' is DPAPI-protected on Windows and cannot be read here");

            return DecodeWindows(raw[WindowsMarker.Length..]);
        }

        if (StartsWith(raw, PlainMarker))
            return new MemoryStream(raw[PlainMarker.Length..]);

        throw new InvalidOperationException($"Unrecognized protected-file format: {protectedPath}");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Stream DecodeWindows(byte[] payloadBytes)
    {
        try
        {
            return new MemoryStream(ProtectedData.Unprotect(payloadBytes, optionalEntropy: null, DataProtectionScope.LocalMachine));
        }
        catch (CryptographicException)
        {
            var text = Encoding.ASCII.GetString(payloadBytes);
            var payload = Convert.FromBase64String(text);
            return new MemoryStream(ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.LocalMachine));
        }
    }

    private static bool StartsWith(byte[] raw, string marker)
    {
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        if (raw.Length < markerBytes.Length)
            return false;

        for (var index = 0; index < markerBytes.Length; index++)
        {
            if (raw[index] != markerBytes[index])
                return false;
        }

        return true;
    }
}
