using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using LectureAgent.Configuration;
using LectureAgent.Infrastructure.Security;

namespace LectureAgent.Commands;

/// <summary>
/// Interactive Google Drive sign-in, triggered by the desktop app with
/// <c>LectureAgent.exe --authorize-google</c>.
/// The Windows service runs as LocalSystem and has no desktop, so it can never open the
/// consent browser itself. This runs as the signed-in user and writes the token into the
/// shared token store the service then reads.
/// </summary>
internal static class AuthorizeGoogleCommand
{
    public const string Flag = "--authorize-google";

    public static async Task<int> RunAsync()
    {
        try
        {
            var configuration = BuildConfiguration();
            var credentialsPath = Resolve(configuration["GoogleDrive:CredentialsPath"] ?? "config/google_credentials.json");
            var tokenPath = Resolve(configuration["GoogleDrive:TokenPath"] ?? "data/google-drive-token");

            var protector = new CredentialProtector();
            await using var credentialStream = protector.OpenRead(credentialsPath);

            Console.WriteLine("A browser window will open. Sign in with the centre's Google account and click Allow.");
            var secrets = GoogleClientSecrets.FromStream(credentialStream).Secrets;
            await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] { DriveService.Scope.DriveFile },
                "lecture-agent",
                CancellationToken.None,
                new FileDataStore(tokenPath, true));

            Console.WriteLine("Google Drive connected.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Google Drive sign-in failed: {ex.Message}");
            return 1;
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true);

        if (AgentPaths.SharedSettingsFile is { } sharedSettings)
        {
            builder.AddJsonFile(sharedSettings, optional: true);
        }

        return builder.Build();
    }

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
