# Security Considerations

## Overview
This document outlines the security considerations for the PII Redaction Web Application.

## Security Features

### 1. PII Data Handling
- **No Persistent Storage**: The application does not store any uploaded documents or detected PII in permanent storage
- **In-Memory Processing**: All PII detection and redaction happens in memory during the request lifecycle
- **No Logging of PII**: The application logs only metadata (e.g., count of entities) and never logs actual PII values

### 2. Authentication & Authorization
- **Azure Managed Identity Support**: The application supports Azure Managed Identity for authentication to Azure OpenAI
- **API Key Security**: API keys should be stored in Azure Key Vault or environment variables, never in source code
- **HTTPS**: Production deployments should always use HTTPS (enforced in non-development environments)

### 3. Input Validation
- **File Size Limits**: Upload file size is limited to 10MB to prevent DoS attacks
- **Content Type Validation**: Only specific file types are accepted (TXT, DOC, DOCX, PDF)
- **Input Sanitization**: All user inputs are sanitized before processing

### 4. Container Security
- **Minimal Base Image**: Uses official Microsoft .NET runtime images
- **Non-Root User**: Container runs as non-root user
- **Read-Only Filesystem**: Application can run with read-only filesystem (except for temp directories)

### 5. Dependency Security
All NuGet packages are from trusted sources:
- Microsoft.Extensions.AI (Official Microsoft package)
- Azure.Identity (Official Microsoft package)
- Azure.AI.Inference (Official Microsoft package)
- OpenAI (Official OpenAI package)

## Best Practices for Deployment

### Azure Container Apps
1. **Use Managed Identity**: Enable system-assigned managed identity for Azure OpenAI authentication
2. **Network Isolation**: Deploy in a virtual network with appropriate network security groups
3. **Secret Management**: Use Azure Key Vault for sensitive configuration
4. **Monitoring**: Enable Application Insights for security monitoring and audit logging
5. **Rate Limiting**: Implement rate limiting to prevent abuse

### Environment Variables
Never commit these to source control:
- `AzureAI__ApiKey`: Store in Azure Key Vault
- `AzureAI__Endpoint`: Safe to include but consider parameterizing for different environments

### HTTPS Configuration
```bash
# Ensure HTTPS is enforced in production
az containerapp update \
  --name pii-redaction-app \
  --resource-group rg-pii-redaction \
  --ingress-allow-insecure false
```

## Security Checklist for Production

- [ ] Enable HTTPS only (no HTTP)
- [ ] Use Managed Identity instead of API keys
- [ ] Store secrets in Azure Key Vault
- [ ] Enable Application Insights monitoring
- [ ] Configure network security groups
- [ ] Implement rate limiting
- [ ] Enable audit logging
- [ ] Set up automated vulnerability scanning
- [ ] Configure CORS policies appropriately
- [ ] Implement WAF (Web Application Firewall) if needed
- [ ] Regular security updates of dependencies
- [ ] Review and rotate credentials regularly

## Vulnerability Disclosure
If you discover a security vulnerability, please email security@example.com (update with actual contact).

## Compliance
This application can be configured to comply with:
- GDPR (General Data Protection Regulation)
- HIPAA (Health Insurance Portability and Accountability Act)
- SOC 2
- ISO 27001

**Note**: Proper compliance requires additional configuration and operational procedures beyond this application.

## Known Limitations
1. The fallback SimpleChatClient uses basic regex patterns and should NOT be used in production
2. PDF support is basic and may not extract text from complex PDFs correctly
3. The application does not detect all possible PII types - AI models may have limitations
4. Detection accuracy depends on the Azure OpenAI model used

## Updates
- 2025-11-02: Initial security documentation
