using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PiiRedactionWebApp.Services;
using System.Text;

namespace PiiRedactionWebApp.Pages;

public class UploadModel : PageModel
{
    private readonly IPiiRedactionService _piiRedactionService;
    private readonly ILogger<UploadModel> _logger;

    public UploadModel(IPiiRedactionService piiRedactionService, ILogger<UploadModel> logger)
    {
        _piiRedactionService = piiRedactionService;
        _logger = logger;
    }

    public RedactionResult? RedactionResult { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? documentFile, string? textInput)
    {
        try
        {
            string textToRedact;

            if (documentFile != null && documentFile.Length > 0)
            {
                _logger.LogInformation("Processing uploaded file: {FileName}", documentFile.FileName);

                // Check file size (limit to 10MB)
                if (documentFile.Length > 10 * 1024 * 1024)
                {
                    ErrorMessage = "File size exceeds 10MB limit.";
                    return Page();
                }

                // Read the file content
                using var reader = new StreamReader(documentFile.OpenReadStream());
                textToRedact = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(textToRedact))
                {
                    ErrorMessage = "The uploaded file is empty or could not be read.";
                    return Page();
                }
            }
            else if (!string.IsNullOrWhiteSpace(textInput))
            {
                _logger.LogInformation("Processing text input");
                textToRedact = textInput;
            }
            else
            {
                ErrorMessage = "Please upload a file or enter text to redact.";
                return Page();
            }

            // Perform PII redaction
            RedactionResult = await _piiRedactionService.RedactPiiAsync(textToRedact);

            _logger.LogInformation("Redaction completed successfully. Found {Count} PII entities", 
                RedactionResult.DetectedEntities.Count);

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing redaction request");
            ErrorMessage = $"An error occurred while processing your request: {ex.Message}";
            return Page();
        }
    }

    public IActionResult OnPostDownload(string redactedText)
    {
        if (string.IsNullOrEmpty(redactedText))
        {
            return RedirectToPage();
        }

        var bytes = Encoding.UTF8.GetBytes(redactedText);
        var fileName = $"redacted_document_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";

        return File(bytes, "text/plain", fileName);
    }
}
