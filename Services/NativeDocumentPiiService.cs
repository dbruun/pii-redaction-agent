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
        // Get source container
        var sourceContainer = _blobServiceClient.GetBlobContainerClient(_storageOptions.SourceContainerName);
        await sourceContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // Get target container
        var targetContainer = _blobServiceClient.GetBlobContainerClient(_storageOptions.TargetContainerName);
        await targetContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // Upload document with unique name
        var blobName = $"{Guid.NewGuid()}/{fileName}";
        var blobClient = sourceContainer.GetBlobClient(blobName);
        
        documentStream.Position = 0;
        await blobClient.UploadAsync(documentStream, overwrite: true, cancellationToken: cancellationToken);

        // Generate SAS URLs
        var sourceSasUrl = GenerateSasUrl(blobClient, BlobSasPermissions.Read);
        var targetContainerSasUrl = GenerateContainerSasUrl(targetContainer, BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List);

        return (sourceSasUrl, targetContainerSasUrl);
    }

    private string GenerateSasUrl(BlobClient blobClient, BlobSasPermissions permissions)
    {
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(2)
        };
        sasBuilder.SetPermissions(permissions);

        var sasToken = blobClient.GenerateSasUri(sasBuilder);
        return sasToken.ToString();
    }

    private string GenerateContainerSasUrl(BlobContainerClient containerClient, BlobContainerSasPermissions permissions)
    {
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerClient.Name,
            Resource = "c",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(2)
        };
        sasBuilder.SetPermissions(permissions);

        var sasToken = containerClient.GenerateSasUri(sasBuilder);
        return sasToken.ToString();
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

        // Download the redacted document
        var blobClient = new BlobClient(new Uri(redactedDocLocation.Location));
        var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
        var redactedContent = downloadResult.Value.Content.ToString();

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
            ProcessedAt = DateTime.UtcNow
        };
    }
}
