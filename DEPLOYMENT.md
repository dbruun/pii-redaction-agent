# Deployment Guide for PII Redaction Web App

This guide provides detailed instructions for deploying the PII Redaction Web Application to Azure Container Apps.

## Prerequisites

Before you begin, ensure you have:

1. Azure subscription
2. Azure CLI installed and configured
3. Docker installed (for local testing)
4. Azure OpenAI or Azure AI Foundry resource with a deployed model
5. Appropriate permissions to create resources in Azure

## Step 1: Prepare Azure OpenAI

### Create Azure OpenAI Resource

```bash
# Set variables
LOCATION="eastus"
RESOURCE_GROUP="rg-pii-redaction"
OPENAI_RESOURCE_NAME="openai-pii-redaction"

# Create resource group
az group create \
  --name $RESOURCE_GROUP \
  --location $LOCATION

# Create Azure OpenAI resource
az cognitiveservices account create \
  --name $OPENAI_RESOURCE_NAME \
  --resource-group $RESOURCE_GROUP \
  --kind OpenAI \
  --sku S0 \
  --location $LOCATION
```

### Deploy a Model

```bash
# Deploy GPT-4 model
az cognitiveservices account deployment create \
  --name $OPENAI_RESOURCE_NAME \
  --resource-group $RESOURCE_GROUP \
  --deployment-name gpt-4 \
  --model-name gpt-4 \
  --model-version "0613" \
  --model-format OpenAI \
  --sku-capacity 10 \
  --sku-name "Standard"
```

### Get Endpoint and Key

```bash
# Get endpoint
OPENAI_ENDPOINT=$(az cognitiveservices account show \
  --name $OPENAI_RESOURCE_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.endpoint -o tsv)

# Get API key (for non-managed identity deployments)
OPENAI_KEY=$(az cognitiveservices account keys list \
  --name $OPENAI_RESOURCE_NAME \
  --resource-group $RESOURCE_GROUP \
  --query key1 -o tsv)

echo "Endpoint: $OPENAI_ENDPOINT"
```

## Step 2: Build and Push Docker Image

### Create Azure Container Registry

```bash
ACR_NAME="acrpiiredaction"

az acr create \
  --name $ACR_NAME \
  --resource-group $RESOURCE_GROUP \
  --sku Basic \
  --admin-enabled true \
  --location $LOCATION
```

### Build and Push Image

```bash
# Login to ACR
az acr login --name $ACR_NAME

# Build and push using ACR build
az acr build \
  --registry $ACR_NAME \
  --image pii-redaction-app:latest \
  --file Dockerfile \
  .
```

## Step 3: Create Container Apps Environment

```bash
ENVIRONMENT_NAME="env-pii-redaction"

az containerapp env create \
  --name $ENVIRONMENT_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION
```

## Step 4: Deploy Container App

### Option A: Using API Key Authentication

```bash
APP_NAME="app-pii-redaction"

# Get ACR credentials
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT_NAME \
  --image ${ACR_NAME}.azurecr.io/pii-redaction-app:latest \
  --registry-server ${ACR_NAME}.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 5 \
  --cpu 0.5 \
  --memory 1Gi \
  --env-vars \
    "AzureAI__Endpoint=$OPENAI_ENDPOINT" \
    "AzureAI__ApiKey=$OPENAI_KEY" \
    "AzureAI__DeploymentName=gpt-4" \
    "AzureAI__UseAzureIdentity=false"
```

### Option B: Using Managed Identity (Recommended)

```bash
# Create the container app first without AI configuration
az containerapp create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT_NAME \
  --image ${ACR_NAME}.azurecr.io/pii-redaction-app:latest \
  --registry-server ${ACR_NAME}.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 5 \
  --cpu 0.5 \
  --memory 1Gi

# Enable system-assigned managed identity
az containerapp identity assign \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --system-assigned

# Get the principal ID
PRINCIPAL_ID=$(az containerapp identity show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query principalId -o tsv)

# Get the OpenAI resource ID
OPENAI_RESOURCE_ID=$(az cognitiveservices account show \
  --name $OPENAI_RESOURCE_NAME \
  --resource-group $RESOURCE_GROUP \
  --query id -o tsv)

# Assign Cognitive Services User role
az role assignment create \
  --role "Cognitive Services User" \
  --assignee $PRINCIPAL_ID \
  --scope $OPENAI_RESOURCE_ID

# Update container app with managed identity configuration
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars \
    "AzureAI__Endpoint=$OPENAI_ENDPOINT" \
    "AzureAI__DeploymentName=gpt-4" \
    "AzureAI__UseAzureIdentity=true"
```

## Step 5: Configure Custom Domain (Optional)

```bash
# Add custom domain
az containerapp hostname add \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname "pii-redaction.yourdomain.com"

# Bind certificate (requires existing certificate)
az containerapp hostname bind \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --hostname "pii-redaction.yourdomain.com" \
  --certificate <certificate-id>
```

## Step 6: Configure Scaling

```bash
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --min-replicas 2 \
  --max-replicas 10 \
  --scale-rule-name http-scaling \
  --scale-rule-type http \
  --scale-rule-http-concurrency 100
```

## Step 7: Configure Monitoring

```bash
# Create Application Insights
APPINSIGHTS_NAME="ai-pii-redaction"

az monitor app-insights component create \
  --app $APPINSIGHTS_NAME \
  --location $LOCATION \
  --resource-group $RESOURCE_GROUP

# Get instrumentation key
INSTRUMENTATION_KEY=$(az monitor app-insights component show \
  --app $APPINSIGHTS_NAME \
  --resource-group $RESOURCE_GROUP \
  --query instrumentationKey -o tsv)

# Update container app with Application Insights
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars \
    "APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=$INSTRUMENTATION_KEY"
```

## Step 8: Test the Deployment

```bash
# Get the application URL
APP_URL=$(az containerapp show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.configuration.ingress.fqdn -o tsv)

echo "Application URL: https://$APP_URL"

# Test the application
curl -I https://$APP_URL
```

## Updating the Application

```bash
# Build new image version
az acr build \
  --registry $ACR_NAME \
  --image pii-redaction-app:v2 \
  --file Dockerfile \
  .

# Update container app with new version
az containerapp update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image ${ACR_NAME}.azurecr.io/pii-redaction-app:v2
```

## Monitoring and Troubleshooting

### View Logs

```bash
# Stream logs
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --follow

# View recent logs
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --tail 100
```

### Check Application Status

```bash
az containerapp show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query properties.runningStatus
```

### View Metrics

```bash
az monitor metrics list \
  --resource $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --resource-type "Microsoft.App/containerApps" \
  --metric "Requests"
```

## Cost Optimization

1. **Right-size resources**: Start with 0.5 CPU and 1Gi memory, scale as needed
2. **Use consumption plan**: Set min-replicas to 0 for development environments
3. **Monitor usage**: Set up cost alerts in Azure Cost Management
4. **Use Azure OpenAI wisely**: Consider token limits and caching strategies

## Security Hardening

See [SECURITY.md](SECURITY.md) for comprehensive security guidelines.

Quick checklist:
- [ ] Enable HTTPS only
- [ ] Use Managed Identity
- [ ] Configure network restrictions
- [ ] Enable Application Insights
- [ ] Set up Azure Key Vault
- [ ] Configure CORS appropriately
- [ ] Enable diagnostic logs

## Cleanup

To remove all resources:

```bash
az group delete \
  --name $RESOURCE_GROUP \
  --yes \
  --no-wait
```

## Additional Resources

- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/cognitive-services/openai/)
- [Microsoft.Extensions.AI Documentation](https://learn.microsoft.com/dotnet/ai/)

## Support

For issues and questions:
- GitHub Issues: [Repository Issues](https://github.com/dbruun/pii-redaction-agent/issues)
- Azure Support: [Azure Portal](https://portal.azure.com)
