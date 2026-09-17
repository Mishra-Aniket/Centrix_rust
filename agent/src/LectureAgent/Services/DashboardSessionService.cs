using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace LectureAgent.Services;

/// <summary>One signed-in staff member on the dashboard.</summary>
public sealed record DashboardSession(
    string Token,
    string Email,
    IReadOnlyList<string> Rooms,
    DateTime ExpiresUtc,
    bool IsAdmin = false);

/// <summary>Outcome of one "Login with Google" attempt.</summary>
public sealed record GoogleLoginResult(string? SessionToken, string? Email, string? Error);

/// <summary>The consent URL a browser must open for one login flow.</summary>
public sealed record GoogleLoginFlow(string FlowId, string ConsentUrl, DateTime CreatedUtc);

/// <summary>
/// Owns the staff dashboard's Google-login sessions and the pending OAuth flows between
/// them. Sessions live in memory: an agent restart signs everyone out and the API key
/// keeps working, so no state has to survive. Staff emails (the allow-list) live in the
/// database so the admin edits them from the dashboard, with appsettings as a fallback.
/// </summary>
public sealed class DashboardSessionService
{
    public const string StaffConfigKey = "AllowedStaffEmails";
    public const string AdminStaffConfigKey = "AdminStaffEmails";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, DashboardSession> _sessions = new();
    private readonly ConcurrentDictionary<string, GoogleLoginResult?> _flows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _flowCreated = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialProtector _credentialProtector;
    private readonly ILogger<DashboardSessionService> _logger;

    public DashboardSessionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ICredentialProtector credentialProtector,
        ILogger<DashboardSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _credentialProtector = credentialProtector;
        _logger = logger;
    }

    public bool IsConfigured =>
        _configuration.GetValue("Auth:GoogleLogin:Enabled", true)
        && CredentialsAvailable();

    /// <summary>
    /// True when the Google OAuth client credentials exist, either as the plain file or
    /// as the protected copy the credential protector writes on startup. Checking only
    /// the plain path here made "Login with Google" vanish after the first protection
    /// pass, even though uploads kept working through the same file.
    /// </summary>
    private bool CredentialsAvailable()
    {
        var path = ResolveCredentialsPath();
        return File.Exists(path) || File.Exists(path + ".protected");
    }

    /// <summary>False while no staff email has been registered anywhere yet.</summary>
    public async Task<bool> HasAllowedEmailsAsync()
    {
        var emails = await GetAllowedEmailsAsync();
        return emails.Count > 0;
    }

    // ----- allow-list (staff emails) ---------------------------------------

    /// <summary>Staff emails from the database plus the appsettings fallback list.</summary>
    public async Task<List<string>> GetAllowedEmailsAsync()
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in SplitList(_configuration["Auth:GoogleLogin:AllowedEmails"]))
        {
            emails.Add(email);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LectureContext>();
        var stored = await db.Configs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == StaffConfigKey);
        if (stored is { ConfigValue: not null })
        {
            try
            {
                foreach (var email in JsonSerializer.Deserialize<List<string>>(stored.ConfigValue) ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        emails.Add(email.Trim());
                    }
                }
            }
            catch (JsonException)
            {
                // A hand-edited value falls back to the appsettings list only.
            }
        }

        return emails.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task AddAllowedEmailAsync(string email)
    {
        // Only the DB list is editable from the dashboard; appsettings stays read-only.
        var stored = await LoadStoredEmailsAsync();
        var alreadyConfigured = (await GetAllowedEmailsAsync()).Contains(email, StringComparer.OrdinalIgnoreCase);
        if (!alreadyConfigured && !stored.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            stored.Add(email.Trim());
            await SaveStoredEmailsAsync(stored);
        }
    }

    public async Task RemoveAllowedEmailAsync(string email)
    {
        var stored = await LoadStoredEmailsAsync();
        stored.RemoveAll(e => string.Equals(e, email.Trim(), StringComparison.OrdinalIgnoreCase));
        await SaveStoredEmailsAsync(stored);
    }

    public List<string> GetAllowedDomains() => SplitList(_configuration["Auth:GoogleLogin:AllowedDomains"]);

    // ----- YouTube OAuth ----------------------------------------------------

    public string ResolveYouTubeTokenPath()
    {
        var path = _configuration["YouTube:TokenPath"] ?? "data/youtube-token";
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    public bool IsYouTubeConnected()
    {
        var tokenDir = ResolveYouTubeTokenPath();
        var tokenFile = Path.Combine(tokenDir, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-lecture-agent-youtube");
        return File.Exists(tokenFile);
    }

    public GoogleLoginFlow StartYouTubeAuth(string redirectUri)
    {
        var (clientId, _) = ReadClientSecrets();
        var flowId = "yt_" + RandomToken(16);
        var consentUrl = "https://accounts.google.com/o/oauth2/v2/auth"
            + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.upload")
            + "&access_type=offline"
            + "&prompt=consent"
            + "&state=" + Uri.EscapeDataString(flowId);

        _flows[flowId] = null;
        _flowCreated[flowId] = DateTime.UtcNow;
        CleanupFlows();
        return new GoogleLoginFlow(flowId, consentUrl, DateTime.UtcNow);
    }

    public async Task<(bool Success, string? Error)> CompleteYouTubeAuthAsync(string flowId, string code, string redirectUri)
    {
        var (clientId, clientSecret) = ReadClientSecrets();
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                })
            };

            using var client = _httpClientFactory.CreateClient("DashboardAuth");
            using var tokenResponse = await client.SendAsync(tokenRequest);
            var body = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return (false, $"Google rejected YouTube authorization: {Truncate(body, 160)}");
            }

            var tokenJson = JsonSerializer.Deserialize<JsonElement>(body);
            var accessToken = tokenJson.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var refreshToken = tokenJson.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = tokenJson.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3599;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                var existingFile = Path.Combine(ResolveYouTubeTokenPath(), "Google.Apis.Auth.OAuth2.Responses.TokenResponse-lecture-agent-youtube");
                if (File.Exists(existingFile))
                {
                    var existingDoc = JsonDocument.Parse(File.ReadAllText(existingFile));
                    if (existingDoc.RootElement.TryGetProperty("refresh_token", out var existingRt))
                    {
                        refreshToken = existingRt.GetString();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "Google did not return an access token.");
            }

            var tokenDir = ResolveYouTubeTokenPath();
            Directory.CreateDirectory(tokenDir);
            var tokenFile = Path.Combine(tokenDir, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-lecture-agent-youtube");

            var now = DateTime.UtcNow;
            var tokenData = new Dictionary<string, object?>
            {
                ["access_token"] = accessToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = expiresIn,
                ["refresh_token"] = refreshToken,
                ["scope"] = "https://www.googleapis.com/auth/youtube.upload",
                ["Issued"] = DateTime.Now.ToString("o"),
                ["IssuedUtc"] = now.ToString("o")
            };

            await File.WriteAllTextAsync(tokenFile, JsonSerializer.Serialize(tokenData, new JsonSerializerOptions { WriteIndented = true }));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ----- login flows ------------------------------------------------------

    public GoogleLoginFlow StartLogin(string redirectUri)
    {
        var (clientId, _) = ReadClientSecrets();
        var flowId = RandomToken(16);
        var consentUrl = "https://accounts.google.com/o/oauth2/v2/auth"
            + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString("openid email profile")
            + "&state=" + Uri.EscapeDataString(flowId)
            + "&prompt=select_account";

        _flows[flowId] = null;
        _flowCreated[flowId] = DateTime.UtcNow;
        CleanupFlows();
        return new GoogleLoginFlow(flowId, consentUrl, DateTime.UtcNow);
    }

    public bool TryGetFlowResult(string flowId, out GoogleLoginResult? result)
    {
        result = null;
        return _flows.TryGetValue(flowId, out var stored) && stored is not null && (result = stored) is not null;
    }

    /// <summary>
    /// Exchanges the consent code for an identity, checks the allow-list and rooms, and
    /// issues the dashboard session. Called from the Google redirect callback.
    /// </summary>
    public async Task<GoogleLoginResult> CompleteLoginAsync(string flowId, string code, string redirectUri)
    {
        var (_, clientSecret) = ReadClientSecrets();
        var (clientId, _) = ReadClientSecrets();
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                })
            };

            using var client = _httpClientFactory.CreateClient("DashboardAuth");
            using var tokenResponse = await client.SendAsync(tokenRequest);
            var body = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Fail(flowId, $"Google rejected the sign-in: {Truncate(body, 160)}");
            }

            var tokenJson = JsonSerializer.Deserialize<JsonElement>(body);
            var idToken = tokenJson.TryGetProperty("id_token", out var idTokenElement)
                ? idTokenElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return Fail(flowId, "Google did not return an identity token.");
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });

            var email = payload.Email;
            if (string.IsNullOrWhiteSpace(email) || payload.EmailVerified != true)
            {
                return Fail(flowId, "The Google account has no verified email address.");
            }

            var bootstrapMode = await IsBootstrapModeAsync();
            if (!await IsEmailAllowedAsync(email))
            {
                // Bootstrap: with no staff email and no allowed domain registered yet,
                // the first successful sign-in (on this PC's browser) becomes the admin.
                // From the second login on, the allow-list decides who gets in.
                if (bootstrapMode)
                {
                    await AddAllowedEmailAsync(email);
                    await AddAdminEmailAsync(email);
                    _logger.LogInformation("Bootstrap: {Email} registered as the first dashboard admin", email);
                }
                else
                {
                    _logger.LogInformation("Google login denied for {Email}: not on the staff list", email);
                    return Fail(flowId,
                        $"{email} is not registered. Ask the admin to add this PW ID on the dashboard (Controls > Staff access).");
                }
            }

            var session = CreateSession(email, ResolveRoomsForEmail(email), await IsAdminEmailAsync(email));
            _flows[flowId] = new GoogleLoginResult(session.Token, email, null);
            _logger.LogInformation("Google login ok for {Email} (rooms: {Rooms})", email, string.Join(",", session.Rooms));
            return _flows[flowId]!;
        }
        catch (InvalidJwtException ex)
        {
            return Fail(flowId, $"Identity could not be verified: {Truncate(ex.Message, 120)}");
        }
        catch (HttpRequestException ex)
        {
            return Fail(flowId, $"Could not reach Google: {Truncate(ex.Message, 120)}");
        }
        catch (JsonException ex)
        {
            return Fail(flowId, $"Unexpected Google answer: {Truncate(ex.Message, 120)}");
        }
    }

    // ----- sessions ---------------------------------------------------------

    public DashboardSession CreateSession(string email, IReadOnlyList<string> rooms, bool isAdmin = false)
    {
        var session = new DashboardSession(RandomToken(32), email, rooms, DateTime.UtcNow.Add(SessionLifetime), isAdmin);
        _sessions[session.Token] = session;
        return session;
    }

    /// <summary>Sliding expiry: every validated use extends the session.</summary>
    public bool TryValidateSession(string? token, out DashboardSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var found))
        {
            return false;
        }

        if (DateTime.UtcNow > found.ExpiresUtc)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        _sessions[token] = found with { ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime) };
        session = _sessions[token];
        return true;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            _sessions.TryRemove(token, out _);
        }
    }

    // ----- helpers ----------------------------------------------------------

    private async Task<bool> IsEmailAllowedAsync(string email)
    {
        var emails = await GetAllowedEmailsAsync();
        if (emails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var domain = email[(email.IndexOf('@') + 1)..];
        return GetAllowedDomains().Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True until anyone at all is allowed: the first sign-in claims admin.</summary>
    public async Task<bool> IsBootstrapModeAsync() =>
        (await GetAllowedEmailsAsync()).Count == 0 && GetAllowedDomains().Count == 0;

    /// <summary>
    /// Returns whether an email has the dashboard administrator role. The editable
    /// list is kept separately from the staff allow-list, with an appsettings list
    /// available for centrally managed deployments.
    /// </summary>
    public async Task<bool> IsAdminEmailAsync(string email)
    {
        var admins = new HashSet<string>(
            SplitList(_configuration["Auth:GoogleLogin:AdminEmails"]),
            StringComparer.OrdinalIgnoreCase);

        foreach (var storedAdmin in await LoadStoredEmailsAsync(AdminStaffConfigKey))
        {
            admins.Add(storedAdmin);
        }

        return admins.Contains(email.Trim());
    }

    private async Task AddAdminEmailAsync(string email)
    {
        var admins = await LoadStoredEmailsAsync(AdminStaffConfigKey);
        if (!admins.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            admins.Add(email.Trim());
            await SaveStoredEmailsAsync(AdminStaffConfigKey, admins);
        }
    }

    /// <summary>
    /// Rooms a staff member may see. "pattern=603,604" entries in EmailRooms match an
    /// exact email or an @domain; no matching entry means no restriction (all rooms).
    /// </summary>
    private IReadOnlyList<string> ResolveRoomsForEmail(string email)
    {
        foreach (var entry in SplitList(_configuration["Auth:GoogleLogin:EmailRooms"], ';'))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var pattern = entry[..separator].Trim();
            var rooms = entry[(separator + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (rooms.Count == 0)
            {
                continue;
            }

            if (string.Equals(pattern, email, StringComparison.OrdinalIgnoreCase)
                || (pattern.StartsWith('@') && email.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return rooms;
            }
        }

        return [];
    }

    private Task<List<string>> LoadStoredEmailsAsync() => LoadStoredEmailsAsync(StaffConfigKey);

    private async Task<List<string>> LoadStoredEmailsAsync(string configKey)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LectureContext>();
        var stored = await db.Configs.AsNoTracking().FirstOrDefaultAsync(c => c.ConfigKey == configKey);
        if (stored is { ConfigValue: not null })
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(stored.ConfigValue) ?? [];
            }
            catch (JsonException)
            {
                // Start a fresh list; the appsettings fallback still applies.
            }
        }

        return [];
    }

    private Task SaveStoredEmailsAsync(List<string> emails) => SaveStoredEmailsAsync(StaffConfigKey, emails);

    private async Task SaveStoredEmailsAsync(string configKey, List<string> emails)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LectureContext>();
        var json = JsonSerializer.Serialize(emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var config = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == configKey);
        if (config is null)
        {
            db.Configs.Add(new Domain.Entities.ConfigEntry
            {
                ConfigKey = configKey,
                ConfigValue = json,
                DataType = "STRING"
            });
        }
        else
        {
            config.ConfigValue = json;
        }

        await db.SaveChangesAsync();
    }

    private GoogleLoginResult Fail(string flowId, string error)
    {
        var result = new GoogleLoginResult(null, null, error);
        _flows[flowId] = result;
        return result;
    }

    private void CleanupFlows()
    {
        var cutoff = DateTime.UtcNow - FlowLifetime;
        foreach (var (flowId, created) in _flowCreated)
        {
            if (created < cutoff)
            {
                _flowCreated.TryRemove(flowId, out _);
                _flows.TryRemove(flowId, out _);
            }
        }
    }

    private string ResolveCredentialsPath()
    {
        var path = _configuration["GoogleDrive:CredentialsPath"] ?? "config/google_credentials.json";
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private (string ClientId, string ClientSecret) ReadClientSecrets()
    {
        var path = ResolveCredentialsPath();
        using var stream = _credentialProtector.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var section = document.RootElement.TryGetProperty("installed", out var installed)
            ? installed
            : document.RootElement.GetProperty("web");
        return (
            section.GetProperty("client_id").GetString() ?? "",
            section.GetProperty("client_secret").GetString() ?? "");
    }

    private static bool FileExists(string path) => File.Exists(path);

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private static List<string> SplitList(string? value, char separator = ',') =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(e => e.Length > 0)
                .ToList();
}
