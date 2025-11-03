using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PiiRedactionWebApp.Services;
using System.Text;

namespace PiiRedactionWebApp.Pages;

public class UploadModel : PageModel
{
    private readonly INativeDocumentPiiService _nativeDocumentPiiService;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(
        INativeDocumentPiiService nativeDocumentPiiService,
        ILogger<UploadModel> logger)
    {
        _nativeDocumentPiiService = nativeDocumentPiiService;
        _logger = logger;
    }

    public DocumentRedactionResult? DocumentResult { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? documentFile)
    {
        try
        {
            if (documentFile == null || documentFile.Length == 0)
            {
                ErrorMessage = "Please upload a file.";
                return Page();
            }

            _logger.LogInformation("Processing uploaded file: {FileName}", documentFile.FileName);

            // Check file size (limit to 10MB)
            if (documentFile.Length > 10 * 1024 * 1024)
            {
                ErrorMessage = "File size exceeds 10MB limit.";
                return Page();
            }

            var fileName = documentFile.FileName;
            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();

            // Check if file type is supported
            if (fileExtension != ".pdf" && fileExtension != ".docx" && fileExtension != ".txt")
            {
                ErrorMessage = $"File type '{fileExtension}' is not supported. Please upload TXT, PDF, or DOCX files.";
                return Page();
            }

            // Use Native Document PII API
            _logger.LogInformation("Using Native Document PII API for {FileName}", fileName);
            
            using var stream = documentFile.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            DocumentResult = await _nativeDocumentPiiService.RedactDocumentAsync(memoryStream, fileName);
            
            _logger.LogInformation("Native Document PII redaction completed for {FileName}", fileName);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing redaction request");
            ErrorMessage = $"An error occurred while processing your request: {ex.Message}";
            return Page();
        }
    }

    public IActionResult OnPostDownload(string originalFileName, string fileExtension, string? redactedDocumentBytesBase64)
    {
        try
        {
            // Prefer binary data if available (for PDF, DOCX, and TXT from Native Document PII API)
            if (!string.IsNullOrEmpty(redactedDocumentBytesBase64))
            {
                var bytes = Convert.FromBase64String(redactedDocumentBytesBase64);
                
                // Determine content type based on file extension
                var contentType = fileExtension.ToLowerInvariant() switch
                {
                    ".pdf" => "application/pdf",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ".txt" => "text/plain",
                    _ => "application/octet-stream"
                };
                
                // Generate output filename based on original
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
                var fileName = $"{fileNameWithoutExtension}_REDACTED_{DateTime.UtcNow:yyyyMMddHHmmss}{fileExtension}";

                return File(bytes, contentType, fileName);
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading redacted document");
            return RedirectToPage();
        }
    }
}
