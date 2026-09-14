using System.Text.Json.Serialization;

namespace LectureAgent.Infrastructure.StudioApi;

/// API response wrapper
public sealed class StudioApiResponse<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; } = default!;
}

/// Parsed from /sheets/master 2D array
public sealed class StudioMasterEntry
{
    public string Center { get; set; } = "";
    public string Room { get; set; } = "";
    public string BatchName { get; set; } = "";
    public string BatchId { get; set; } = "";
}

/// Parsed from /sheets/teacher 2D array
public sealed class StudioTeacherEntry
{
    public string Center { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string DriveLink { get; set; } = "";
    public string DriveId { get; set; } = "";
}

/// From /files endpoint
public sealed class StudioFileEntry
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";
    
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = "";
    
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = "";
    
    [JsonPropertyName("stopTime")]
    public string StopTime { get; set; } = "";
    
    [JsonPropertyName("center")]
    public string Center { get; set; } = "";
    
    [JsonPropertyName("room")]
    public string Room { get; set; } = "";
    
    [JsonPropertyName("batchName")]
    public string? BatchName { get; set; }
    
    [JsonPropertyName("batchId")]
    public string? BatchId { get; set; }
    
    [JsonPropertyName("uploaded")]
    public bool Uploaded { get; set; }
    
    [JsonPropertyName("scheduled")]
    public bool Scheduled { get; set; }
    
    [JsonPropertyName("isProcessed")]
    public bool IsProcessed { get; set; }
    
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
    
    [JsonPropertyName("youtubeId")]
    public string? YoutubeId { get; set; }
    
    [JsonPropertyName("fileKey")]
    public string? FileKey { get; set; }
    
    [JsonPropertyName("fileDetails")]
    public StudioFileDetails? FileDetails { get; set; }
}

public sealed class StudioFileDetails
{
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }
    
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
