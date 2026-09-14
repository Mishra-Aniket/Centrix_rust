using System.Net;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class NotificationMessagesTests
{
    private static LectureSession MakeLecture() => new()
    {
        LectureSessionId = "LSN-1",
        CenterId = "CENTER-1",
        VideoFileLocalPath = "/recordings/AJ251NA_physics.mp4",
        BatchId = "27-AJ251NA 2026",
        SubjectId = "PHYSICS",
        ConfidenceScore = 72,
        DetectedStartTime = new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void ReviewRequired_IncludesSuggestionAndConfidence()
    {
        var (title, body) = NotificationMessages.ReviewRequired("CENTER-1", MakeLecture());

        Assert.Equal("Review needed", title);
        Assert.Contains("AJ251NA_physics.mp4", body);
        Assert.Contains("27-AJ251NA 2026 / PHYSICS", body);
        Assert.Contains("72%", body);
        Assert.Contains("LSN-1", body);
    }

    [Fact]
    public void ReviewRequired_WithoutSuggestion_MentionsIt()
    {
        var lecture = MakeLecture();
        lecture.BatchId = null;
        lecture.SubjectId = null;

        var (_, body) = NotificationMessages.ReviewRequired("CENTER-1", lecture);
        Assert.Contains("no suggestion", body);
    }

    [Fact]
    public void UploadFailed_IncludesErrorAndQueueId()
    {
        var entry = new UploadQueueEntry
        {
            QueueEntryId = "UQ-1",
            LectureSessionId = "LSN-1",
            LocalFilePath = "/recordings/class.mp4",
            RetryCount = 5
        };

        var (_, body) = NotificationMessages.UploadFailed("CENTER-1", entry, "Drive quota exceeded");

        Assert.Contains("class.mp4", body);
        Assert.Contains("Drive quota exceeded", body);
        Assert.Contains("UQ-1", body);
    }
}

/// <summary>
/// Exercises the webhook channel against a stub HTTP handler to verify payload
/// shapes (Generic / Discord / Slack) and event gating without any network access.
/// </summary>
public class WebhookNotificationServiceTests
{
    private static LectureSession MakeLecture() => new()
    {
        LectureSessionId = "LSN-1",
        CenterId = "CENTER-1",
        VideoFileLocalPath = "/recordings/class.mp4",
        BatchId = "27-AJ251NA 2026",
        SubjectId = "PHYSICS",
        ConfidenceScore = 72,
        DetectedStartTime = new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc)
    };

    private static (WebhookNotificationService Service, HttpMessageHandlerStub Handler) CreateService(
        params (string Key, string Value)[] settings)
    {
        var handler = new HttpMessageHandlerStub();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
        var service = new WebhookNotificationService(
            new HttpClient(handler), config, NullLogger<WebhookNotificationService>.Instance);
        return (service, handler);
    }

    private sealed class HttpMessageHandlerStub : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task NotifyReviewRequired_GenericStyle_PostsJsonEnvelope()
    {
        var (service, handler) = CreateService(
            ("Notifications:Webhook:Url", "https://hooks.example.com/abc"),
            ("Notifications:Webhook:Style", "Generic"));

        await service.NotifyReviewRequiredAsync("CENTER-1", MakeLecture());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.example.com/abc", request.RequestUri!.ToString());
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"event\":\"ReviewRequired\"", body.Replace(" ", string.Empty));
        Assert.Contains("LSN-1", body);
    }

    [Fact]
    public async Task NotifyReviewRequired_DiscordStyle_PostsContentField()
    {
        var (service, handler) = CreateService(
            ("Notifications:Webhook:Url", "https://discord.com/api/webhooks/123/xyz"),
            ("Notifications:Webhook:Style", "Discord"));

        await service.NotifyReviewRequiredAsync("CENTER-1", MakeLecture());

        var request = Assert.Single(handler.Requests);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"content\"", body);
        Assert.DoesNotContain("\"event\"", body);
    }

    [Fact]
    public async Task NotifyReviewRequired_EventDisabled_DoesNotSend()
    {
        var (service, handler) = CreateService(
            ("Notifications:Webhook:Url", "https://hooks.example.com/abc"),
            ("Notifications:Events:ReviewRequired", "false"));

        await service.NotifyReviewRequiredAsync("CENTER-1", MakeLecture());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void IsEnabled_InsecureHttpUrl_IsDisabled()
    {
        var (service, _) = CreateService(("Notifications:Webhook:Url", "http://insecure.example.com/hook"));
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ValidHttpsUrl_IsEnabled()
    {
        var (service, _) = CreateService(("Notifications:Webhook:Url", "https://hooks.example.com/abc"));
        Assert.True(service.IsEnabled);
    }
}
