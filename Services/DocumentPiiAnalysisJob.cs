using System.Text.Json.Serialization;

namespace PiiRedactionWebApp.Services;

/// <summary>
/// Model for Native Document PII analysis job
/// </summary>
public class DocumentPiiAnalysisJob
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdatedDateTime")]
    public DateTime LastUpdatedDateTime { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTime CreatedDateTime { get; set; }

    [JsonPropertyName("expirationDateTime")]
    public DateTime ExpirationDateTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("errors")]
    public List<object> Errors { get; set; } = new();

    [JsonPropertyName("tasks")]
    public TasksInfo? Tasks { get; set; }
}

public class TasksInfo
{
    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("inProgress")]
    public int InProgress { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<TaskItem> Items { get; set; } = new();
}

public class TaskItem
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdateDateTime")]
    public DateTime LastUpdateDateTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public Results? Results { get; set; }
}

public class Results
{
    [JsonPropertyName("documents")]
    public List<ProcessedDocument> Documents { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<object> Errors { get; set; } = new();

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; } = string.Empty;
}

public class ProcessedDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public DocumentLocation? Source { get; set; }

    [JsonPropertyName("targets")]
    public List<DocumentLocation> Targets { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<object> Warnings { get; set; } = new();
}

public class DocumentLocation
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
}
