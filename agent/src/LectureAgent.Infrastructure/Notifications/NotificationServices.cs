namespace LectureAgent.Infrastructure.Notifications;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Builds human-readable titles/bodies for each notification event. Static and pure
/// so message formatting is unit-testable and consistent across channels.
/// </summary>
public static class NotificationMessages
{
    public static (string Title, string Body) ReviewRequired(string centerId, LectureSession lecture)
    {
        var suggestion = string.IsNullOrWhiteSpace(lecture.BatchId)
            ? "no suggestion"
            : $"{lecture.BatchId} / {lecture.SubjectId}";
        var start = lecture.DetectedStartTime.ToLocalTime().ToString("dd MMM yyyy, HH:mm");
        return (
            "Review needed",
            $"[{centerId}] {Path.GetFileName(lecture.VideoFileLocalPath ?? "lecture")} ({start}) needs review. " +
            $"Suggested: {suggestion} (confidence {lecture.ConfidenceScore}%). Lecture id: {lecture.LectureSessionId}");
    }

    public static (string Title, string Body) UploadFailed(string centerId, UploadQueueEntry entry, string error)
    {
        var fileName = Path.GetFileName(entry.LocalFilePath);
        return (
            "Upload failed",
            $"[{centerId}] Upload permanently failed after {entry.RetryCount} attempt(s): {fileName}. " +
            $"Last error: {error}. Queue id: {entry.QueueEntryId}");
    }

    public static (string Title, string Body) MissingLecture(string centerId, TimetableEntry expectedLecture)
    {
        var slotStart = DateTime.Today.Add(expectedLecture.SlotStartTime).ToString("HH:mm");
        return (
            "Missing lecture",
            $"[{centerId}] No recording detected for {expectedLecture.BatchId} / {expectedLecture.SubjectId} " +
            $"in room {expectedLecture.RoomId} (slot starts {slotStart}).");
    }
}

/// <summary>
/// Which events produce notifications. Lets a center silence noisy events without
/// touching code.
/// </summary>
public sealed class NotificationEventSettings
{
    public bool ReviewRequired { get; init; } = true;
    public bool UploadFailed { get; init; } = true;
    public bool MissingLecture { get; init; }

    public static NotificationEventSettings FromConfiguration(IConfiguration config) => new()
    {
        ReviewRequired = config.GetValue("Notifications:Events:ReviewRequired", true),
        UploadFailed = config.GetValue("Notifications:Events:UploadFailed", true),
        MissingLecture = config.GetValue("Notifications:Events:MissingLecture", false)
    };
}

/// <summary>
/// Sends each notification to every enabled channel. A failing channel is logged and
/// skipped; one broken webhook must never stop the email channel (or the agent).
/// </summary>
public sealed class CompositeNotificationService : INotificationService
{
    private readonly IReadOnlyList<INotificationService> _channels;
    private readonly ILogger<CompositeNotificationService> _logger;

    public CompositeNotificationService(IEnumerable<INotificationService> channels, ILogger<CompositeNotificationService> logger)
    {
        _channels = channels.ToList();
        _logger = logger;
    }

    public async Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture)
    {
        foreach (var channel in _channels)
            await SafeNotifyAsync(channel, s => s.NotifyReviewRequiredAsync(centerId, lecture), "ReviewRequired");
    }

    public async Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error)
    {
        foreach (var channel in _channels)
            await SafeNotifyAsync(channel, s => s.NotifyUploadFailedAsync(centerId, entry, error), "UploadFailed");
    }

    public async Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture)
    {
        foreach (var channel in _channels)
            await SafeNotifyAsync(channel, s => s.NotifyMissingLectureAsync(centerId, expectedLecture), "MissingLecture");
    }

    private async Task SafeNotifyAsync(
        INotificationService channel,
        Func<INotificationService, Task> send,
        string eventName)
    {
        try
        {
            await send(channel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification channel {Channel} failed for {Event}: {Message}",
                channel.GetType().Name, eventName, ex.Message);
        }
    }
}

/// <summary>
/// No-op fallback when no channel is configured: events still reach the log file,
/// which is what operators read when something looks off anyway.
/// </summary>
public sealed class LogNotificationService : INotificationService
{
    private readonly ILogger<LogNotificationService> _logger;

    public LogNotificationService(ILogger<LogNotificationService> logger) => _logger = logger;

    public Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture)
    {
        var (_, body) = NotificationMessages.ReviewRequired(centerId, lecture);
        _logger.LogInformation("REVIEW_REQUIRED: {Body}", body);
        return Task.CompletedTask;
    }

    public Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error)
    {
        var (_, body) = NotificationMessages.UploadFailed(centerId, entry, error);
        _logger.LogInformation("UPLOAD_FAILED: {Body}", body);
        return Task.CompletedTask;
    }

    public Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture)
    {
        var (_, body) = NotificationMessages.MissingLecture(centerId, expectedLecture);
        _logger.LogInformation("MISSING_LECTURE: {Body}", body);
        return Task.CompletedTask;
    }
}

/// <summary>
/// HTTP webhook channel. Style "Generic" posts a JSON envelope; "Discord" and "Slack"
/// post the payload shape those platforms expect, so a center can point this at a
/// Discord channel, Slack incoming webhook, or any n8n/Zapier/WhatsApp bridge.
/// </summary>
public sealed class WebhookNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _url;
    private readonly string _style;
    private readonly NotificationEventSettings _events;
    private readonly ILogger<WebhookNotificationService> _logger;

    public WebhookNotificationService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<WebhookNotificationService> logger)
    {
        _httpClient = httpClient;
        _url = config["Notifications:Webhook:Url"] ?? string.Empty;
        _style = config["Notifications:Webhook:Style"] ?? "Generic";
        _events = NotificationEventSettings.FromConfiguration(config);
        _logger = logger;
    }

    public bool IsEnabled =>
        _url.Length > 0 && _url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public async Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture)
    {
        if (!_events.ReviewRequired)
            return;
        var (title, body) = NotificationMessages.ReviewRequired(centerId, lecture);
        await SendAsync("ReviewRequired", title, body, lecture.LectureSessionId, null);
    }

    public async Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error)
    {
        if (!_events.UploadFailed)
            return;
        var (title, body) = NotificationMessages.UploadFailed(centerId, entry, error);
        await SendAsync("UploadFailed", title, body, entry.LectureSessionId, entry.QueueEntryId);
    }

    public async Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture)
    {
        if (!_events.MissingLecture)
            return;
        var (title, body) = NotificationMessages.MissingLecture(centerId, expectedLecture);
        await SendAsync("MissingLecture", title, body, null, null);
    }

    private async Task SendAsync(string eventName, string title, string body, string? lectureId, string? queueEntryId)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Webhook notification skipped: URL missing or not https");
            return;
        }

        object payload = _style.Equals("Discord", StringComparison.OrdinalIgnoreCase)
            ? new { content = $"**{title}**\n{body}" }
            : _style.Equals("Slack", StringComparison.OrdinalIgnoreCase)
                ? new { text = $"*{title}*\n{body}" }
                : new
                {
                    @event = eventName,
                    title,
                    message = body,
                    lectureSessionId = lectureId,
                    queueEntryId,
                    timestamp = DateTime.UtcNow
                };

        using var response = await _httpClient.PostAsJsonAsync(_url, payload);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Webhook returned {StatusCode} for {Event}", response.StatusCode, eventName);
    }
}

/// <summary>
/// SMTP email channel. Uses the built-in SmtpClient; credentials can reference the
/// protected-credential file written by CredentialProtector (Notifications:Email:Password).
/// </summary>
public sealed class EmailNotificationService : INotificationService
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useSsl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly string _from;
    private readonly string[] _to;
    private readonly NotificationEventSettings _events;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(IConfiguration config, ILogger<EmailNotificationService> logger)
    {
        _host = config["Notifications:Email:SmtpHost"] ?? string.Empty;
        _port = config.GetValue("Notifications:Email:SmtpPort", 587);
        _useSsl = config.GetValue("Notifications:Email:UseSsl", true);
        _username = config["Notifications:Email:Username"];
        _password = config["Notifications:Email:Password"];
        _from = config["Notifications:Email:From"] ?? _username ?? "lectureagent@localhost";
        _to = (config["Notifications:Email:To"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _events = NotificationEventSettings.FromConfiguration(config);
        _logger = logger;
    }

    public bool IsEnabled => _host.Length > 0 && _to.Length > 0;

    public Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture)
    {
        if (!_events.ReviewRequired)
            return Task.CompletedTask;
        var (title, body) = NotificationMessages.ReviewRequired(centerId, lecture);
        return SendAsync(title, body);
    }

    public Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error)
    {
        if (!_events.UploadFailed)
            return Task.CompletedTask;
        var (title, body) = NotificationMessages.UploadFailed(centerId, entry, error);
        return SendAsync(title, body);
    }

    public Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture)
    {
        if (!_events.MissingLecture)
            return Task.CompletedTask;
        var (title, body) = NotificationMessages.MissingLecture(centerId, expectedLecture);
        return SendAsync(title, body);
    }

    private async Task SendAsync(string title, string body)
    {
        using var message = new System.Net.Mail.MailMessage
        {
            From = new System.Net.Mail.MailAddress(_from),
            Subject = $"[LectureAgent] {title}",
            Body = body,
            BodyEncoding = Encoding.UTF8
        };
        foreach (var recipient in _to)
            message.To.Add(recipient);

        using var client = new System.Net.Mail.SmtpClient(_host, _port)
        {
            EnableSsl = _useSsl,
            Credentials = _username is null
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_username, _password)
        };

        await client.SendMailAsync(message);
        _logger.LogInformation("Notification email sent to {Recipients}: {Title}", string.Join(", ", _to), title);
    }
}

/// <summary>
/// Builds the INotificationService the agent actually uses from configuration:
/// a composite of every enabled channel, or the log-only fallback.
/// </summary>
public static class NotificationServiceFactory
{
    public static INotificationService Create(HttpClient httpClient, IConfiguration config, ILoggerFactory loggerFactory)
    {
        var channels = new List<INotificationService>();

        var webhook = new WebhookNotificationService(httpClient, config, loggerFactory.CreateLogger<WebhookNotificationService>());
        if (webhook.IsEnabled)
            channels.Add(webhook);

        var email = new EmailNotificationService(config, loggerFactory.CreateLogger<EmailNotificationService>());
        if (email.IsEnabled)
            channels.Add(email);

        if (channels.Count == 0)
            return new LogNotificationService(loggerFactory.CreateLogger<LogNotificationService>());

        return new CompositeNotificationService(channels, loggerFactory.CreateLogger<CompositeNotificationService>());
    }
}
