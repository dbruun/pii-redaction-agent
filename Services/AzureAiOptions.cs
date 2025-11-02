namespace PiiRedactionWebApp.Services;

public class AzureAiOptions
{
    public const string SectionName = "AzureAI";
    
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4";
    public bool UseAzureIdentity { get; set; } = false;
}
