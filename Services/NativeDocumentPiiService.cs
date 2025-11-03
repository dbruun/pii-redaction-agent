using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System.Text;
using System.Text.Json;

namespace PiiRedactionWebApp.Services;

/// <summary>
/// Service for processing documents using Azure AI Language Native Document PII API
/// </summary>
public interface INativeDocumentPiiService
{
    Task<DocumentRedactionResult> RedactDocumentAsync(Stream documentStream, string fileName, CancellationToken cancellationToken = default);
}

public class NativeDocumentPiiService : INativeDocumentPiiService
{
    private readonly HttpClient _httpClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<NativeDocumentPiiService> _logger;
    private readonly AzureAiOptions _languageOptions;
    private readonly AzureStorageOptions _storageOptions;

    public NativeDocumentPiiService(
        HttpClient httpClient,
        BlobServiceClient blobServiceClient,
        ILogger<NativeDocumentPiiService> logger,
        Microsoft.Extensions.Options.IOptions<AzureAiOptions> languageOptions,
        Microsoft.Extensions.Options.IOptions<AzureStorageOptions> storageOptions)
    {
        _httpClient = httpClient;
        _blobServiceClient = blobServiceClient;
        _logger = logger;
        _languageOptions = languageOptions.Value;
        _storageOptions = storageOptions.Value;
    }

    public async Task<DocumentRedactionResult> RedactDocumentAsync(
        Stream documentStream, 
        string fileName, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting native document PII redaction for {FileName}", fileName);

            // Step 1: Upload document to source blob storage
            var (sourceBlobUrl, targetContainerUrl) = await UploadDocumentToBlobAsync(documentStream, fileName, cancellationToken);
            _logger.LogInformation("Document uploaded to blob storage");

            // Step 2: Submit analysis job to Native Document PII API
            var jobId = await SubmitAnalysisJobAsync(sourceBlobUrl, targetContainerUrl, fileName, cancellationToken);
            _logger.LogInformation("Analysis job submitted with ID: {JobId}", jobId);

            // Step 3: Poll for job completion
            var job = await PollJobStatusAsync(jobId, cancellationToken);
            _logger.LogInformation("Job completed with status: {Status}", job.Status);

            if (job.Status != "succeeded")
            {
                throw new InvalidOperationException($"Document analysis failed with status: {job.Status}");
            }

            // Step 4: Download redacted document
            var redactedDocument = await DownloadRedactedDocumentAsync(job, fileName, cancellationToken);
            _logger.LogInformation("Redacted document downloaded successfully");

            return redactedDocument;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during native document PII redaction");
            throw;
        }
    }

    private async Task<(string sourceBlobUrl, string targetContainerUrl)> UploadDocumentToBlobAsync(
        Stream documentStream, 
        string fileName, 
        CancellationToken cancellationToken)
    {
        // Get source container (assumes container already exists)
        var sourceContainer = _blobServiceClient.GetBlobContainerClient(_storageOptions.SourceContainerName);

        // Get target container (assumes container already exists)
        var targetContainer = _blobServiceClient.GetBlobContainerClient(_storageOptions.TargetContainerName);

        // Upload document with unique name
        var blobName = $"{Guid.NewGuid()}/{fileName}";
        var blobClient = sourceContainer.GetBlobClient(blobName);
        
        documentStream.Position = 0;
        await blobClient.UploadAsync(documentStream, overwrite: true, cancellationToken: cancellationToken);

        // Generate SAS URLs (supports both Entra ID and Account Key)
        var sourceSasUrl = await GenerateBlobSasUrlAsync(blobClient, BlobSasPermissions.Read, cancellationToken);
        var targetContainerSasUrl = await GenerateContainerSasUrlAsync(targetContainer, BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List, cancellationToken);

        _logger.LogInformation("Generated SAS URLs - Source: {SourceLength} chars, Target: {TargetLength} chars", 
            sourceSasUrl.Length, targetContainerSasUrl.Length);

        return (sourceSasUrl, targetContainerSasUrl);
    }

    private async Task<string> GenerateBlobSasUrlAsync(BlobClient blobClient, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        if (_storageOptions.UseAzureIdentity)
        {
            // Use User Delegation SAS (works with Entra ID/Managed Identity)
            // Note: User delegation key expiry must be within 7 days
            var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                startsOn: DateTimeOffset.UtcNow.AddMinutes(-5),
                expiresOn: DateTimeOffset.UtcNow.AddHours(48),
                cancellationToken: cancellationToken);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(48),
                Protocol = SasProtocol.Https  // Enforce HTTPS only
            };
            sasBuilder.SetPermissions(permissions);

            var sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, blobClient.AccountName);
            var sasUri = new UriBuilder(blobClient.Uri)
            {
                Query = sasToken.ToString()
            };

            _logger.LogDebug("Generated User Delegation SAS URL for blob: {BlobName}", blobClient.Name);
            return sasUri.ToString();
        }
        else
        {
            // Use Account Key SAS (traditional method)
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(48)
            };
            sasBuilder.SetPermissions(permissions);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return sasUri.ToString();
        }
    }

    private async Task<string> GenerateContainerSasUrlAsync(BlobContainerClient containerClient, BlobContainerSasPermissions permissions, CancellationToken cancellationToken)
    {
        if (_storageOptions.UseAzureIdentity)
        {
            // Use User Delegation SAS (works with Entra ID/Managed Identity)
            // Note: User delegation key expiry must be within 7 days
            var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                startsOn: DateTimeOffset.UtcNow.AddMinutes(-5),
                expiresOn: DateTimeOffset.UtcNow.AddHours(48),
                cancellationToken: cancellationToken);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                Resource = "c",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(48),
                Protocol = SasProtocol.Https  // Enforce HTTPS only
            };
            sasBuilder.SetPermissions(permissions);

            var sasToken = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, containerClient.AccountName);
            var sasUri = new UriBuilder(containerClient.Uri)
            {
                Query = sasToken.ToString()
            };

            _logger.LogDebug("Generated User Delegation SAS URL for container: {ContainerName}", containerClient.Name);
            return sasUri.ToString();
        }
        else
        {
            // Use Account Key SAS (traditional method)
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                Resource = "c",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(48)
            };
            sasBuilder.SetPermissions(permissions);

            var sasUri = containerClient.GenerateSasUri(sasBuilder);
            return sasUri.ToString();
        }
    }

    private async Task<string> SubmitAnalysisJobAsync(
        string sourceBlobUrl, 
        string targetContainerUrl, 
        string fileName,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            displayName = $"PII Redaction: {fileName}",
            analysisInput = new
            {
                documents = new[]
                {
                    new
                    {
                        language = "en-US",
                        id = "doc_0",
                        source = new
                        {
                            location = sourceBlobUrl
                        },
                        target = new
                        {
                            location = targetContainerUrl
                        }
                    }
                }
            },
            tasks = new[]
            {
                new
                {
                    kind = "PiiEntityRecognition",
                    taskName = "Redact PII Task",
                    parameters = new
                    {
                        redactionPolicy = new
                        {
                            policyKind = "entityMask"
                        },
                        excludeExtractionData = false
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = $"{_languageOptions.Endpoint.TrimEnd('/')}/language/analyze-documents/jobs?api-version=2024-11-15-preview";
        
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        // Add authentication header based on configuration
        if (_languageOptions.UseAzureIdentity)
        {
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(
                new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                cancellationToken);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }
        else if (!string.IsNullOrEmpty(_languageOptions.ApiKey))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", _languageOptions.ApiKey);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to submit analysis job: {response.StatusCode} - {error}");
        }

        // Extract job ID from operation-location header
        var operationLocation = response.Headers.GetValues("operation-location").FirstOrDefault();
        if (string.IsNullOrEmpty(operationLocation))
        {
            throw new InvalidOperationException("operation-location header not found in response");
        }

        var jobId = ExtractJobIdFromOperationLocation(operationLocation);
        return jobId;
    }

    private string ExtractJobIdFromOperationLocation(string operationLocation)
    {
        // Extract jobId from URL like: https://{endpoint}/language/analyze-documents/jobs/{jobId}?api-version=...
        var uri = new Uri(operationLocation);
        var segments = uri.AbsolutePath.Split('/');
        return segments[^1]; // Last segment is the jobId
    }

    private async Task<DocumentPiiAnalysisJob> PollJobStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        var endpoint = $"{_languageOptions.Endpoint.TrimEnd('/')}/language/analyze-documents/jobs/{jobId}?api-version=2024-11-15-preview";
        var maxAttempts = 60; // 60 attempts with 2 second intervals = 2 minutes max
        var delaySeconds = 2;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            // Add authentication
            if (_languageOptions.UseAzureIdentity)
            {
                var credential = new DefaultAzureCredential();
                var token = await credential.GetTokenAsync(
                    new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                    cancellationToken);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            }
            else if (!string.IsNullOrEmpty(_languageOptions.ApiKey))
            {
                request.Headers.Add("Ocp-Apim-Subscription-Key", _languageOptions.ApiKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var job = JsonSerializer.Deserialize<DocumentPiiAnalysisJob>(json);

            if (job == null)
            {
                throw new InvalidOperationException("Failed to deserialize job status response");
            }

            _logger.LogInformation("Job {JobId} status: {Status} (Attempt {Attempt}/{MaxAttempts})", 
                jobId, job.Status, attempt + 1, maxAttempts);

            if (job.Status == "succeeded" || job.Status == "failed" || job.Status == "cancelled")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        throw new TimeoutException($"Job {jobId} did not complete within the timeout period");
    }

    private async Task<DocumentRedactionResult> DownloadRedactedDocumentAsync(
        DocumentPiiAnalysisJob job, 
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var document = job.Tasks?.Items?.FirstOrDefault()?.Results?.Documents?.FirstOrDefault();
        if (document == null || !document.Targets.Any())
        {
            throw new InvalidOperationException("No redacted document found in job results");
        }

        // Find the redacted document (not the JSON result)
        var redactedDocLocation = document.Targets
            .FirstOrDefault(t => !t.Location.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        if (redactedDocLocation == null)
        {
            throw new InvalidOperationException("Redacted document not found in targets");
        }

        // Download the redacted document as binary data (supports PDF, DOCX, TXT)
        // The location URL from AI service might not include SAS, so we need to create a blob client with our credentials
        _logger.LogInformation("Downloading redacted document from: {Location}", redactedDocLocation.Location);
        
        // Parse the URL to extract container and blob name
        var uri = new Uri(redactedDocLocation.Location);
        var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
        if (pathParts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid blob URL format: {redactedDocLocation.Location}");
        }
        
        var containerName = pathParts[0];
        var blobName = Uri.UnescapeDataString(pathParts[1]);
        
        // Create a blob client using our authenticated BlobServiceClient
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobName);
        
        var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        
        // Store binary data for download
        var redactedBytes = downloadResult.Value.Content.ToArray();
        
        // For display purposes, try to get text content (works for TXT, may not work for binary formats)
        var redactedContent = TryGetTextContent(downloadResult.Value.Content, Path.GetExtension(originalFileName));

        // Download the JSON result for entity information
        var jsonResultLocation = document.Targets
            .FirstOrDefault(t => t.Location.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase));

        var entities = new List<PiiEntity>();
        if (jsonResultLocation != null)
        {
            try
            {
                var jsonBlobClient = new BlobClient(new Uri(jsonResultLocation.Location));
                var jsonDownload = await jsonBlobClient.DownloadContentAsync(cancellationToken);
                var jsonContent = jsonDownload.Value.Content.ToString();
                
                // Parse entities from JSON (simplified - you may need to adjust based on actual JSON structure)
                _logger.LogDebug("PII entities JSON: {Json}", jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download or parse PII entities JSON");
            }
        }

        return new DocumentRedactionResult
        {
            OriginalFileName = originalFileName,
            FileExtension = Path.GetExtension(originalFileName),
            OriginalText = "(Original document - view not available for native document processing)",
            RedactedText = redactedContent,
            DetectedEntities = entities,
            FileSizeBytes = downloadResult.Value.Details.ContentLength,
            ProcessedAt = DateTime.UtcNow,
            RedactedDocumentBytes = redactedBytes
        };
    }

    private string TryGetTextContent(BinaryData content, string extension)
    {
        try
        {
            // For text files, return the content directly
            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return content.ToString();
            }
            
            // For binary formats (PDF, DOCX), indicate that preview is not available
            return $"Binary document redacted successfully. File type: {extension}\n\n" +
                   $"Download the redacted document to view the content.\n" +
                   $"Size: {content.ToArray().Length:N0} bytes";
        }
        catch
        {
            return "(Unable to display document content - download to view)";
        }
    }
}
