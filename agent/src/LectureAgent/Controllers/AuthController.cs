using LectureAgent.Security;
using LectureAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace LectureAgent.Presentation.Controllers;

/// <summary>
/// Dashboard sign-in: "Login with Google (PW ID)" for staff (email allow-list, room
/// scoping) plus the staff-list management the admin uses. The API key keeps working
/// unchanged for the admin and integrations.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly DashboardSessionService _sessions;
    private readonly IConfiguration _configuration;

    public AuthController(DashboardSessionService sessions, IConfiguration configuration)
    {
        _sessions = sessions;
        _configuration = configuration;
    }

    /// <summary>Public: what the login screen needs before showing the Google button.</summary>
    [HttpGet("config")]
    public async Task<ActionResult> GetConfig()
    {
        return Ok(new
        {
            googleLoginEnabled = _sessions.IsConfigured,
            emailsRegistered = await _sessions.HasAllowedEmailsAsync(),
            bootstrapMode = await _sessions.IsBootstrapModeAsync(),
            domains = _sessions.GetAllowedDomains()
        });
    }

    /// <summary>Public: begins a login. The browser opens the returned consent URL.</summary>
    [HttpPost("google/start")]
    public ActionResult StartGoogleLogin([FromBody] StartLoginRequest? request)
    {
        if (!_sessions.IsConfigured)
        {
            return StatusCode(503, new { error = "Google login is not configured on this agent (missing Google credentials file)." });
        }

        // The redirect must land on the agent itself through loopback, which installed
        // Google clients allow on any localhost port without pre-registration.
        var redirectUri = $"http://localhost:{HttpPort()}/api/auth/google/callback";
        var flow = _sessions.StartLogin(redirectUri);
        return Ok(new { flowId = flow.FlowId, consentUrl = flow.ConsentUrl, redirectUri });
    }

    /// <summary>Public: the login window polls this while the consent tab is open.</summary>
    [HttpGet("google/poll/{flowId}")]
    public ActionResult PollGoogleLogin(string flowId)
    {
        if (_sessions.TryGetFlowResult(flowId, out var result))
        {
            return Ok(new { status = "ok", sessionToken = result!.SessionToken, email = result.Email });
        }

        return Ok(new { status = "pending" });
    }

    /// <summary>Check if YouTube channel is linked/authorized.</summary>
    [HttpGet("youtube/status")]
    public ActionResult GetYouTubeStatus()
    {
        return Ok(new
        {
            enabled = _configuration.GetValue("YouTube:Enabled", false),
            connected = _sessions.IsYouTubeConnected()
        });
    }

    /// <summary>Begins YouTube channel connection OAuth flow.</summary>
    [HttpPost("youtube/start")]
    public ActionResult StartYouTubeAuth()
    {
        var redirectUri = $"http://localhost:{HttpPort()}/api/auth/youtube/callback";
        var flow = _sessions.StartYouTubeAuth(redirectUri);
        return Ok(new { flowId = flow.FlowId, consentUrl = flow.ConsentUrl, redirectUri });
    }

    /// <summary>Google redirects here after YouTube consent.</summary>
    [HttpGet("youtube/callback")]
    public async Task<IActionResult> YouTubeCallback(
        [FromQuery] string? state,
        [FromQuery] string? code,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CallbackPage(null, $"YouTube connection cancelled or failed: {error}");
        }

        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            return CallbackPage(null, "YouTube authorization was cancelled or incomplete.");
        }

        var redirectUri = $"http://localhost:{HttpPort()}/api/auth/youtube/callback";
        var (success, authError) = await _sessions.CompleteYouTubeAuthAsync(state, code, redirectUri);
        if (!success)
        {
            return CallbackPage(null, authError);
        }

        return Content("""
            <!doctype html><html><head><meta charset="utf-8"><title>YouTube Connected</title></head>
            <body style="font-family:system-ui;padding:40px;max-width:480px;margin:auto;text-align:center">
            <h2 style="color:#059669;margin-bottom:8px">✓ YouTube Channel Connected!</h2>
            <p style="color:#4b5563;font-size:14px;line-height:1.5">Your YouTube channel is successfully authorized. Centrix can now publish lecture recordings as unlisted.</p>
            <p style="margin-top:24px"><button onclick="window.close()" style="background:#0f172a;color:#fff;border:none;padding:8px 18px;border-radius:10px;font-weight:600;cursor:pointer">Close Window</button></p>
            <script>setTimeout(function(){try{window.close();}catch(e){}},2500);</script>
            </body></html>
            """, "text/html");
    }

    /// <summary>
    /// Public: Google redirects here after consent. The code is exchanged for an identity,
    /// the session is issued, and a tiny page stores it and returns to the dashboard.
    /// </summary>
    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? state,
        [FromQuery] string? code,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return CallbackPage(null, error switch
            {
                "access_denied" =>
                    "Google blocked the sign-in for this account. If you saw \"Google hasn't verified this app\", "
                    + "click Advanced, then \"Go to Centrix (unsafe)\", and Allow. If the app is in Testing "
                    + "mode in Google Cloud Console, add your email as a test user.",
                _ => $"Google sign-in failed: {error}"
            });
        }

        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            return CallbackPage(null, "Sign-in was cancelled or incomplete.");
        }

        var redirectUri = $"http://localhost:{HttpPort()}/api/auth/google/callback";
        var result = await _sessions.CompleteLoginAsync(state, code, redirectUri);
        return CallbackPage(result.SessionToken, result.Error, result.Email);
    }

    private ContentResult CallbackPage(string? sessionToken, string? error, string? email = null)
    {
        var message = error is not null
            ? $"<p style='color:#b91c1c'>{System.Net.WebUtility.HtmlEncode(error)}</p><p>You can close this window and try again.</p>"
            : $"<p>Signed in as <b>{System.Net.WebUtility.HtmlEncode(email ?? "")}</b>.</p><p>You can close this window — the dashboard is already signed in.</p>";

        var script = sessionToken is not null
            ? $"<script>try{{localStorage.setItem('lasrs.sessionToken',{Json(sessionToken)});}}catch(e){{}}setTimeout(function(){{location.replace('/');}},600);</script>"
            : string.Empty;

        return Content($"""
            <!doctype html><html><head><meta charset="utf-8"><title>Centrix — signed in</title></head>
            <body style="font-family:system-ui;padding:40px;max-width:480px;margin:auto">
            <h2 style="margin-bottom:8px">Centrix</h2>
            {message}
            {script}
            </body></html>
            """, "text/html");
    }

    /// <summary>Who is calling: a signed-in staff session or the admin API key.</summary>
    [HttpGet("me")]
    public ActionResult Me()
    {
        if (HttpContext.Items[ApiKeyMiddleware.DashboardSessionItem] is DashboardSession session)
        {
            return Ok(new { mode = "google", email = session.Email, rooms = session.Rooms, admin = session.IsAdmin });
        }

        if (string.Equals(HttpContext.Items[ApiKeyMiddleware.AuthModeItem] as string, "key", StringComparison.Ordinal)
            || string.Equals(HttpContext.Items[ApiKeyMiddleware.AuthModeItem] as string, "local", StringComparison.Ordinal))
        {
            return Ok(new { mode = "key", email = (string?)null, rooms = (IReadOnlyList<string>?)null, admin = true });
        }

        return Unauthorized();
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var token = Request.Headers["X-Session"].FirstOrDefault() ?? Request.Cookies["lasrs_session"];
        _sessions.Revoke(token);
        Response.Cookies.Delete("lasrs_session");
        return Ok(new { ok = true });
    }

    // ----- staff allow-list (admin API key or authorized admin session only) -----

    [HttpGet("staff")]
    public async Task<ActionResult> GetStaff()
    {
        if (!IsAdmin())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access is required to view staff." });

        return Ok(new
        {
            emails = await _sessions.GetAllowedEmailsAsync(),
            domains = _sessions.GetAllowedDomains()
        });
    }

    public sealed record StaffEmailRequest(string Email);

    [HttpPost("staff")]
    public async Task<ActionResult> AddStaff([FromBody] StaffEmailRequest request)
    {
        if (!IsAdmin())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access is required to manage staff." });

        var email = request.Email?.Trim() ?? "";
        if (!email.Contains('@') || email.Contains(' ') || email.Contains(';'))
        {
            return BadRequest(new { error = "Enter a valid email address, e.g. name@pw.live" });
        }

        await _sessions.AddAllowedEmailAsync(email);
        return Ok(new { emails = await _sessions.GetAllowedEmailsAsync() });
    }

    [HttpDelete("staff/{email}")]
    public async Task<ActionResult> RemoveStaff(string email)
    {
        if (!IsAdmin())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access is required to manage staff." });

        await _sessions.RemoveAllowedEmailAsync(Uri.UnescapeDataString(email));
        return Ok(new { emails = await _sessions.GetAllowedEmailsAsync() });
    }

    private bool IsAdmin() =>
        string.Equals(HttpContext.Items[ApiKeyMiddleware.AuthModeItem] as string, "key", StringComparison.Ordinal)
        || string.Equals(HttpContext.Items[ApiKeyMiddleware.AuthModeItem] as string, "local", StringComparison.Ordinal)
        || (HttpContext.Items[ApiKeyMiddleware.DashboardSessionItem] is DashboardSession session && session.IsAdmin);

    private int HttpPort()
    {
        var url = _configuration["Kestrel:Endpoints:Http:Url"] ?? "http://0.0.0.0:5200";
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Port > 0 ? parsed.Port : 5200;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    public sealed record StartLoginRequest(string? RedirectUri);
}
