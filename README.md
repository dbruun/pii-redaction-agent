# PII Redaction Agent Web Application

A .NET 9.0 web application for detecting and redacting Personally Identifiable Information (PII) from documents using **Azure AI Language Native Document PII API** with Azure Blob Storage integration.

## Features

- **Native Document PII API**: Uses Azure's Native Document PII API that preserves document formatting during redaction
- **Azure Blob Storage Integration**: Secure document storage with SAS token-based access
- **Async Job Processing**: Submit analysis jobs and poll for completion
- **Entra ID Authentication**: Supports Managed Identity and DefaultAzureCredential for secure, passwordless authentication
- **Document Upload**: Upload PDF, DOCX, or TXT files up to 10MB
- **Format Preservation**: Original document formatting maintained in redacted output
- **Visual Verification**: Side-by-side comparison of original and redacted text
- **Comprehensive PII Coverage**: Detects 20+ entity categories including names, emails, phone numbers, SSNs, addresses, and more
- **Download Capability**: Export redacted documents
- **Azure Container Apps Ready**: Includes Dockerfile for easy deployment

## Supported Document Formats

- **PDF** (.pdf) - Native document processing (formatting preserved)
- **Microsoft Word** (.docx) - Native document processing (formatting preserved)
- **Plain Text** (.txt) - Direct text processing
- **Maximum file size**: 10MB per document (up to 40 documents per request)

## Detected PII Types (20+ categories)

- Names (Person, PersonType)
- Email addresses
- Phone numbers
- Social Security Numbers (SSN)
- Credit card numbers
- Addresses (street addresses, cities with zip codes)
- Dates of birth
- Driver's license numbers
- Passport numbers
- Bank account numbers
- IP addresses (IPv4, IPv6)
- Medical record numbers
- Organization names
- URLs
- And more...

## Prerequisites

- .NET 9.0 SDK
- Azure subscription with:
  - **Azure AI Language** resource with Native Document PII API enabled (required)
  - **Azure Blob Storage** account for document processing (required)
- Docker (for containerization)
- Azure CLI (for deployment to ACA)

**Note:** Both Azure AI Language and Azure Blob Storage are **required** for this application to function.

## Configuration

### Azure Resources Setup

**Step 1: Azure AI Language Resource**
1. Create an Azure AI Language resource in Azure Portal
2. Ensure the region supports Native Document PII API (2024-11-15-preview)
3. Get your Language endpoint from Keys and Endpoint section
4. Note: Native Document PII API requires API version `2024-11-15-preview`

**Step 2: Azure Blob Storage Account**
1. Create an Azure Storage account
2. The application will automatically create two containers:
   - `source-documents` - For uploaded documents
   - `redacted-documents` - For PII-redacted output
3. Get your Storage connection string from Access Keys section

**Step 3: Authentication Setup (Recommended: Managed Identity)**
1. For production, enable Managed Identity on your app
2. Assign the following RBAC roles:
   - **Cognitive Services User** on the Language resource
   - **Storage Blob Data Contributor** on the Storage account
3. For development, use Azure CLI or connection strings

### Application Configuration

Update `appsettings.json` or use environment variables:

```json
{
  "AzureLanguage": {
    "Endpoint": "https://your-language-resource.cognitiveservices.azure.com/",
    "ApiKey": "",
    "UseAzureIdentity": true
  },
  "AzureStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=your-storage;...",
    "SourceContainerName": "source-documents",
    "TargetContainerName": "redacted-documents",
    "UseAzureIdentity": true
  }
}
```

**Environment Variables** (recommended for production):

**Azure AI Language:**
- `AzureLanguage__Endpoint`: Your Azure AI Language endpoint
- `AzureLanguage__ApiKey`: Your API key (only if not using Entra ID)
- `AzureLanguage__UseAzureIdentity`: Set to `true` to use Entra ID/Managed Identity (recommended)

**Azure Blob Storage:**
- `AzureStorage__ConnectionString`: Your Storage account connection string
- `AzureStorage__SourceContainerName`: Container for source documents (default: `source-documents`)
- `AzureStorage__TargetContainerName`: Container for redacted documents (default: `redacted-documents`)
- `AzureStorage__UseAzureIdentity`: Set to `true` to use Managed Identity for Storage (recommended)

**Note:** Azure Storage configuration is required for the application to function.

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
  -e AzureLanguage__Endpoint="https://your-resource.cognitiveservices.azure.com/" \
  -e AzureLanguage__ApiKey="your-api-key" \
  -e AzureLanguage__UseAzureIdentity="false" \
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

```
┌─────────────────┐
│   Web UI        │
│  (Razor Pages)  │
└────────┬────────┘
         │
         ▼
┌──────────────────────────────┐
│ NativeDocumentPiiService     │
│ 1. Upload to Blob Storage    │
│ 2. Submit analysis job       │
│ 3. Poll job status           │
│ 4. Download redacted doc     │
└────┬─────────────────────┬───┘
     │                     │
     ▼                     ▼
┌─────────────────┐   ┌─────────────────────────┐
│ Azure Blob      │   │ Azure AI Language       │
│ Storage         │   │ Native Document PII API │
│ (SAS tokens)    │   │ (REST, async jobs)      │
└─────────────────┘   └─────────────────────────┘
```

## Technology Stack

- **Framework**: ASP.NET Core 9.0 (Razor Pages)
- **AI Service**: Azure AI Language Native Document PII API (2024-11-15-preview)
- **Storage**: Azure Blob Storage 12.22.2 (with SAS tokens)
- **Authentication**: Azure Identity 1.17.0 (DefaultAzureCredential, Managed Identity)
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
2. Click "Redact PII" in the navigation
3. **Upload a document** (PDF, DOCX, or TXT up to 10MB)
4. Click "Redact PII" button
5. Review the results:
   - Side-by-side comparison of original and redacted text
   - Table of all detected PII entities with types and positions
   - Document metadata (filename, size, processing time)
6. Download the redacted document

## Project Structure

```
PiiRedactionWebApp/
├── Pages/
│   ├── Index.cshtml                     # Home page
│   ├── Upload.cshtml                    # Upload and redaction page
│   ├── Upload.cshtml.cs                 # Upload page logic
│   └── Shared/
│       └── _Layout.cshtml               # Layout template
├── Services/
│   ├── INativeDocumentPiiService.cs     # Native Document PII interface
│   ├── NativeDocumentPiiService.cs      # Native Document PII implementation (async jobs)
│   ├── DocumentRedactionResult.cs       # Result and entity models
│   ├── DocumentPiiAnalysisJob.cs        # Native Document PII API response models
│   ├── AzureAiOptions.cs                # Language service configuration
│   └── AzureStorageOptions.cs           # Blob storage configuration
├── Program.cs                            # Application startup and DI configuration
├── appsettings.json                      # Configuration
├── Dockerfile                            # Container definition
├── README.md                             # This file
├── DEPLOYMENT.md                         # Deployment guide
└── NATIVE_DOCUMENT_PII_SETUP.md         # Azure setup guide
```

## Documentation

This repository includes comprehensive documentation:

- **[README.md](README.md)** (this file) - Overview, features, and quick start
- **[NATIVE_DOCUMENT_PII_SETUP.md](NATIVE_DOCUMENT_PII_SETUP.md)** - Complete Azure resource setup for Native Document PII API
- **[DEPLOYMENT.md](DEPLOYMENT.md)** - Azure Container Apps deployment guide
- **[SECURITY.md](SECURITY.md)** - Security best practices and guidelines

## Troubleshooting

### Application fails to start
- Verify Azure AI Language configuration is correct
- Check that the endpoint URL is properly formatted
- Ensure network connectivity to Azure
- Verify Entra ID authentication is configured if UseAzureIdentity=true

### PII detection not working
- Verify API key is valid (if not using Entra ID)
- Check Azure AI Language service is provisioned and running
- Review application logs for errors
- Ensure proper RBAC roles if using Managed Identity

### Storage configuration errors
- Verify Azure Storage connection string is set in appsettings.json
- Check connection string format is valid
- Ensure storage account is accessible
- Review NATIVE_DOCUMENT_PII_SETUP.md for complete setup

### Container deployment issues
- Ensure port 8080 is exposed
- Verify environment variables are set correctly
- Check container logs: `az containerapp logs show --name pii-redaction-app --resource-group rg-pii-redaction`

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.