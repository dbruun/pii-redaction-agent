namespace PiiRedactionWebApp.Services;

/// <summary>
/// Represents a detected PII entity
/// </summary>
public class PiiEntity
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
}

/// <summary>
/// Represents a document with its metadata and redaction information
/// </summary>
public class DocumentRedactionResult
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string RedactedText { get; set; } = string.Empty;
    public List<PiiEntity> DetectedEntities { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public long FileSizeBytes { get; set; }
    
    /// <summary>
    /// Binary content of the redacted document (for PDF, DOCX, or TXT files)
    /// Used for direct file download
    /// </summary>
    public byte[]? RedactedDocumentBytes { get; set; }
}
