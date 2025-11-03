namespace PiiRedactionWebApp.Services;

/// <summary>
/// Options for configuring Azure Storage for document processing
/// </summary>
public class AzureStorageOptions
{
    public const string SectionName = "AzureStorage";
    
    public string ConnectionString { get; set; } = string.Empty;
    public string SourceContainerName { get; set; } = "source-documents";
    public string TargetContainerName { get; set; } = "redacted-documents";
    public bool UseAzureIdentity { get; set; } = true;
}
