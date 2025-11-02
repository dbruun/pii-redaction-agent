# PII Redaction Agent Web Application

A .NET 9.0 web application for detecting and redacting Personally Identifiable Information (PII) from documents using Azure AI Foundry and Microsoft's Agentic Framework (successor to Semantic Kernel).

## Features

- **AI-Powered PII Detection**: Uses Azure OpenAI through the Microsoft.Extensions.AI (Agentic Framework) to intelligently detect and redact PII
- **Multiple Input Methods**: Upload documents or paste text directly
- **Visual Verification**: Side-by-side comparison of original and redacted text
- **Comprehensive PII Coverage**: Detects names, emails, phone numbers, SSNs, addresses, and more
- **Download Capability**: Export redacted documents
- **Azure Container Apps Ready**: Includes Dockerfile for easy deployment

## Detected PII Types

- Names (full names, first names, last names)
- Email addresses
- Phone numbers
- Social Security Numbers (SSN)
- Credit card numbers
- Addresses (street addresses, cities with zip codes)
- Dates of birth
- Driver's license numbers
- Passport numbers
- Bank account numbers
- IP addresses
- Medical record numbers
- Employee IDs

## Prerequisites

- .NET 9.0 SDK
- Azure subscription with Azure OpenAI or Azure AI Foundry access
- Docker (for containerization)
- Azure CLI (for deployment to ACA)

## Configuration

### Azure AI Setup

1. Create an Azure OpenAI resource or Azure AI Foundry project
2. Deploy a GPT-4 or GPT-3.5 model
3. Get your endpoint URL and API key

### Application Configuration

Update `appsettings.json` or use environment variables:

```json
{
  "AzureAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "DeploymentName": "gpt-4",
    "UseAzureIdentity": false
  }
}
```

**Environment Variables** (recommended for production):
- `AzureAI__Endpoint`: Your Azure OpenAI endpoint
- `AzureAI__ApiKey`: Your API key (or use Managed Identity)
- `AzureAI__DeploymentName`: Model deployment name
- `AzureAI__UseAzureIdentity`: Set to `true` to use Managed Identity

## Local Development

### Run with .NET CLI

```bash
dotnet restore
dotnet run
```

Navigate to `https://localhost:5001` or `http://localhost:5000`

### Run with Docker

```bash
docker build -t pii-redaction-app .
docker run -p 8080:8080 \
  -e AzureAI__Endpoint="https://your-resource.openai.azure.com/" \
  -e AzureAI__ApiKey="your-api-key" \
  -e AzureAI__DeploymentName="gpt-4" \
  pii-redaction-app
```

## Deployment to Azure Container Apps

### Option 1: Using Azure CLI

```bash
# Login to Azure
az login

# Create resource group
az group create --name rg-pii-redaction --location eastus

# Create Azure Container Apps environment
az containerapp env create \
  --name pii-redaction-env \
  --resource-group rg-pii-redaction \
  --location eastus

# Create Azure Container Registry (ACR)
az acr create \
  --name <your-acr-name> \
  --resource-group rg-pii-redaction \
  --sku Basic \
  --admin-enabled true

# Build and push image to ACR
az acr build \
  --registry <your-acr-name> \
  --image pii-redaction-app:latest \
  --file Dockerfile .

# Get ACR credentials
$ACR_USERNAME=$(az acr credential show --name <your-acr-name> --query username -o tsv)
$ACR_PASSWORD=$(az acr credential show --name <your-acr-name> --query passwords[0].value -o tsv)

# Deploy to Container Apps
az containerapp create \
  --name pii-redaction-app \
  --resource-group rg-pii-redaction \
  --environment pii-redaction-env \
  --image <your-acr-name>.azurecr.io/pii-redaction-app:latest \
  --registry-server <your-acr-name>.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --target-port 8080 \
  --ingress external \
  --env-vars \
    AzureAI__Endpoint="<your-endpoint>" \
    AzureAI__ApiKey="<your-api-key>" \
    AzureAI__DeploymentName="gpt-4" \
    AzureAI__UseAzureIdentity="false"
```

### Option 2: Using Managed Identity (Recommended for Production)

```bash
# Enable system-assigned managed identity
az containerapp identity assign \
  --name pii-redaction-app \
  --resource-group rg-pii-redaction \
  --system-assigned

# Get the principal ID
$PRINCIPAL_ID=$(az containerapp identity show \
  --name pii-redaction-app \
  --resource-group rg-pii-redaction \
  --query principalId -o tsv)

# Assign Cognitive Services User role to the managed identity
az role assignment create \
  --role "Cognitive Services User" \
  --assignee $PRINCIPAL_ID \
  --scope /subscriptions/<subscription-id>/resourceGroups/<ai-resource-group>/providers/Microsoft.CognitiveServices/accounts/<ai-resource-name>

# Update container app to use managed identity
az containerapp update \
  --name pii-redaction-app \
  --resource-group rg-pii-redaction \
  --set-env-vars \
    AzureAI__Endpoint="<your-endpoint>" \
    AzureAI__DeploymentName="gpt-4" \
    AzureAI__UseAzureIdentity="true"
```

## Architecture

The application uses the following architecture:

```
┌─────────────────┐
│   Web UI        │
│  (Razor Pages)  │
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│ PiiRedactionService     │
│ (Business Logic)        │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│ Microsoft.Extensions.AI │
│ (Agentic Framework)     │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│   Azure OpenAI API      │
│ (AI Foundry/OpenAI)     │
└─────────────────────────┘
```

## Technology Stack

- **Framework**: ASP.NET Core 9.0 (Razor Pages)
- **AI Framework**: Microsoft.Extensions.AI (Agentic Framework)
- **AI Service**: Azure OpenAI / Azure AI Foundry
- **Authentication**: Azure Identity (Managed Identity support)
- **Container**: Docker
- **Deployment**: Azure Container Apps (ACA)

## Security Considerations

- Never commit API keys to source control
- Use Azure Key Vault or environment variables for secrets
- Enable HTTPS in production
- Use Managed Identity when possible
- Implement rate limiting for production use
- Log PII detection but not the actual PII values

## Usage

1. Navigate to the application URL
2. Click "Start Redacting"
3. Either:
   - Upload a text document (TXT, DOC, DOCX)
   - Paste text directly into the text area
4. Click "Redact PII"
5. Review the detected PII entities and redacted text
6. Download the redacted document if needed

## Project Structure

```
PiiRedactionWebApp/
├── Pages/
│   ├── Index.cshtml              # Home page
│   ├── Upload.cshtml             # Upload and redaction page
│   └── Shared/
│       └── _Layout.cshtml        # Layout template
├── Services/
│   ├── IPiiRedactionService.cs   # Service interface
│   ├── PiiRedactionService.cs    # AI-powered redaction implementation
│   └── AzureAiOptions.cs         # Configuration model
├── Program.cs                     # Application startup
├── appsettings.json              # Configuration
├── Dockerfile                     # Container definition
└── README.md                      # This file
```

## Troubleshooting

### Application fails to start
- Verify Azure AI configuration is correct
- Check that the deployment name matches your Azure OpenAI deployment
- Ensure network connectivity to Azure

### PII detection not working
- Verify API key is valid
- Check Azure OpenAI service is running
- Review application logs for errors

### Container deployment issues
- Ensure port 8080 is exposed
- Verify environment variables are set correctly
- Check container logs: `az containerapp logs show --name pii-redaction-app --resource-group rg-pii-redaction`

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.