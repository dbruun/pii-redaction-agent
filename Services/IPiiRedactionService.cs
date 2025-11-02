namespace PiiRedactionWebApp.Services;

public interface IPiiRedactionService
{
    Task<RedactionResult> RedactPiiAsync(string text);
}

public class RedactionResult
{
    public string OriginalText { get; set; } = string.Empty;
    public string RedactedText { get; set; } = string.Empty;
    public List<PiiEntity> DetectedEntities { get; set; } = new();
}

public class PiiEntity
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
}
