namespace PiiRedactionWebApp.Services;

public class AzureAiOptions
{
    public const string SectionName = "AzureLanguage";
    
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool UseAzureIdentity { get; set; } = true;
}
