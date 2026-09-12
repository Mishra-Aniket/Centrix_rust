using System.Net.Http.Json;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Infrastructure.Cloud;

public sealed class HttpCloudMetadataSync : ICloudMetadataSync
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpCloudMetadataSync> _logger;

    public HttpCloudMetadataSync(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HttpCloudMetadataSync> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SyncAuditAsync(AuditLogEntry auditEntry, CancellationToken cancellationToken = default)
    {
        var endpoint = _configuration["CloudSync:MetadataEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        using var response = await _httpClient.PostAsJsonAsync(endpoint, auditEntry, cancellationToken);
        if (response.IsSuccessStatusCode)
            return true;

        _logger.LogWarning("Cloud metadata sync failed with status {StatusCode}", response.StatusCode);
        return false;
    }
}
