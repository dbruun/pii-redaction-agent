# Azure Native Document PII API Setup Guide

This guide walks through setting up Azure resources for the **Native Document PII API** implementation, which preserves document formatting during PII redaction.

## Overview

The Native Document PII API provides superior document processing compared to text extraction:

| Feature | Native Document API | Text Extraction (Fallback) |
|---------|--------------------|-----------------------------|
| Formatting | ✅ Preserved | ❌ Lost |
| Layout | ✅ Maintained | ❌ Lost |
| Processing | Async (job-based) | Synchronous |
| File Support | PDF, DOCX, TXT | PDF, DOCX, TXT |
| Storage Required | Azure Blob Storage | None |
| API Version | 2024-11-15-preview | 2023-04-01 (stable) |

## Prerequisites

- Azure subscription
- Azure CLI installed
- PowerShell or Bash
- Owner or Contributor role on the subscription

## Step 1: Create Resource Group

```bash
# Set variables
$RESOURCE_GROUP="rg-pii-redaction"
$LOCATION="eastus"  # Or your preferred region

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION
```

## Step 2: Create Azure AI Language Resource

```bash
$LANGUAGE_NAME="lang-pii-redaction-$(Get-Random -Maximum 9999)"

az cognitiveservices account create `
  --name $LANGUAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --kind TextAnalytics `
  --sku S `
  --location $LOCATION `
  --yes

# Get the endpoint
$LANGUAGE_ENDPOINT=$(az cognitiveservices account show `
  --name $LANGUAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --query properties.endpoint `
  --output tsv)

echo "Language Endpoint: $LANGUAGE_ENDPOINT"
```

**Important Notes:**
- The Native Document PII API requires API version `2024-11-15-preview`
- Not all regions support this preview API - check [Azure AI Language regions](https://learn.microsoft.com/azure/ai-services/language-service/concepts/regional-support)
- The SKU must be `S` (Standard) or higher

## Step 3: Create Azure Storage Account

```bash
$STORAGE_NAME="stpiiredact$(Get-Random -Maximum 9999)"

az storage account create `
  --name $STORAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --location $LOCATION `
  --sku Standard_LRS `
  --kind StorageV2 `
  --allow-blob-public-access false

# Get the connection string
$STORAGE_CONNECTION_STRING=$(az storage account show-connection-string `
  --name $STORAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --query connectionString `
  --output tsv)

echo "Storage Connection String: $STORAGE_CONNECTION_STRING"
```

**Note:** The containers (`source-documents` and `redacted-documents`) will be created automatically by the application.

## Step 4: Configure Application Settings

Update your `appsettings.json`:

```json
{
  "AzureLanguage": {
    "Endpoint": "<LANGUAGE_ENDPOINT from Step 2>",
    "ApiKey": "",
    "UseAzureIdentity": true
  },
  "AzureStorage": {
    "ConnectionString": "<STORAGE_CONNECTION_STRING from Step 3>",
    "SourceContainerName": "source-documents",
    "TargetContainerName": "redacted-documents",
    "UseAzureIdentity": true
  }
}
```

Or use environment variables:

```powershell
$env:AzureLanguage__Endpoint = $LANGUAGE_ENDPOINT
$env:AzureLanguage__UseAzureIdentity = "true"
$env:AzureStorage__ConnectionString = $STORAGE_CONNECTION_STRING
$env:AzureStorage__UseAzureIdentity = "true"
```

## Step 5: Development Authentication (Azure CLI)

For local development, sign in with Azure CLI:

```bash
az login

# Set the subscription (if you have multiple)
az account set --subscription "<your-subscription-id>"
```

The application will use `DefaultAzureCredential`, which automatically uses:
1. Environment variables (if set)
2. Azure CLI credentials
3. Managed Identity (when deployed to Azure)
4. Visual Studio credentials
5. VS Code credentials

## Step 6: Production Setup - Managed Identity

When deploying to Azure (Container Apps, App Service, etc.), use Managed Identity:

### Enable System-Assigned Managed Identity

```bash
# For Azure Container Apps
az containerapp identity assign `
  --name pii-redaction-app `
  --resource-group $RESOURCE_GROUP `
  --system-assigned

# Get the principal ID
$PRINCIPAL_ID=$(az containerapp identity show `
  --name pii-redaction-app `
  --resource-group $RESOURCE_GROUP `
  --query principalId `
  --output tsv)
```

### Assign RBAC Roles

```bash
# Get Language resource ID
$LANGUAGE_ID=$(az cognitiveservices account show `
  --name $LANGUAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --query id `
  --output tsv)

# Assign Cognitive Services User role
az role assignment create `
  --role "Cognitive Services User" `
  --assignee $PRINCIPAL_ID `
  --scope $LANGUAGE_ID

# Get Storage account ID
$STORAGE_ID=$(az storage account show `
  --name $STORAGE_NAME `
  --resource-group $RESOURCE_GROUP `
  --query id `
  --output tsv)

# Assign Storage Blob Data Contributor role
az role assignment create `
  --role "Storage Blob Data Contributor" `
  --assignee $PRINCIPAL_ID `
  --scope $STORAGE_ID

# Allow managed identity to generate SAS tokens
az role assignment create `
  --role "Storage Account Contributor" `
  --assignee $PRINCIPAL_ID `
  --scope $STORAGE_ID
```

### Update Application Configuration

```json
{
  "AzureLanguage": {
    "Endpoint": "https://<language-name>.cognitiveservices.azure.com/",
    "ApiKey": "",
    "UseAzureIdentity": true
  },
  "AzureStorage": {
    "ConnectionString": "AccountName=<storage-name>;...",
    "SourceContainerName": "source-documents",
    "TargetContainerName": "redacted-documents",
    "UseAzureIdentity": true
  }
}
```

**Note:** When `UseAzureIdentity` is true, the application will:
- Use bearer token authentication for Language API
- Use Managed Identity for Blob Storage access
- Generate SAS tokens for blob URLs (required by Native Document PII API)

## Step 7: Verify Setup

Run the application locally:

```bash
dotnet run
```

Navigate to `https://localhost:5001/Upload` and:
1. Check that the UI shows **"Native Document API enabled"** badge
2. Upload a PDF or DOCX file
3. Verify the document is processed with formatting preserved
4. Check Azure Storage for created containers and blobs

## Step 8: Monitor API Usage

Check the analysis jobs in Azure Portal:
1. Go to your Language resource
2. Navigate to **Metrics**
3. Add metrics:
   - Total Calls
   - Data In
   - Data Out
   - Latency

Or use Azure CLI:

```bash
# List recent API calls (requires Azure Monitor)
az monitor metrics list `
  --resource $LANGUAGE_ID `
  --metric "TotalCalls" `
  --start-time (Get-Date).AddHours(-1) `
  --interval PT1M
```

## Troubleshooting

### Error: "operation-location header not found"
- **Cause:** API version not supported or incorrect endpoint
- **Solution:** Verify you're using API version `2024-11-15-preview`

### Error: "Failed to submit analysis job: 401"
- **Cause:** Authentication failure
- **Solution:** 
  - Verify Managed Identity has Cognitive Services User role
  - Check Azure CLI is signed in for local development
  - Verify endpoint URL is correct

### Error: "Blob not found" or "403 Forbidden"
- **Cause:** Storage authentication or permission issue
- **Solution:**
  - Verify Managed Identity has Storage Blob Data Contributor role
  - Check connection string is correct
  - Ensure containers exist

### Error: "Job did not complete within timeout"
- **Cause:** Large document or service delay
- **Solution:**
  - Check Azure Portal for job status
  - Increase timeout in `NativeDocumentPiiService.cs`
  - Verify document size is under 10MB

### Application falls back to text extraction mode
- **Cause:** Azure Storage not configured
- **Solution:**
  - Verify `AzureStorage:ConnectionString` is set
  - Check storage account is accessible
  - Review application logs for errors

## Best Practices

1. **Use Managed Identity in Production**
   - More secure than connection strings
   - Automatic credential rotation
   - No secrets to manage

2. **Enable Soft Delete on Storage**
   ```bash
   az storage blob service-properties delete-policy update `
     --account-name $STORAGE_NAME `
     --enable true `
     --days-retained 7
   ```

3. **Set Blob Lifecycle Policies**
   - Auto-delete source documents after processing
   - Move redacted documents to cool storage after 30 days

4. **Monitor Costs**
   - Native Document PII API charges per 1000 text records
   - Blob storage charges for transactions and storage
   - Set up cost alerts in Azure Portal

5. **Implement Retry Logic**
   - The service includes built-in retry for transient failures
   - Configure retry settings in `NativeDocumentPiiService.cs`

## API Limits

- **File Size:** 10MB per document
- **Batch Size:** Up to 40 documents per request
- **Rate Limits:** Varies by SKU (check Azure Portal)
- **Job Expiration:** Jobs expire after 24 hours
- **SAS Token Duration:** 2 hours (configurable)

## Additional Resources

- [Native Document PII API Documentation](https://learn.microsoft.com/azure/ai-services/language-service/personally-identifiable-information/how-to/redact-document-pii)
- [Azure AI Language Pricing](https://azure.microsoft.com/pricing/details/cognitive-services/language-service/)
- [Azure Blob Storage Pricing](https://azure.microsoft.com/pricing/details/storage/blobs/)
- [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
