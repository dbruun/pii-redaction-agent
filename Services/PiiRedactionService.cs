using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PiiRedactionWebApp.Services;

public class PiiRedactionService : IPiiRedactionService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<PiiRedactionService> _logger;

    public PiiRedactionService(IChatClient chatClient, ILogger<PiiRedactionService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<RedactionResult> RedactPiiAsync(string text)
    {
        try
        {
            _logger.LogInformation("Starting PII redaction for text of length: {Length}", text.Length);

            var systemPrompt = @"You are a PII (Personally Identifiable Information) detection and redaction agent. 
Your task is to identify and redact all PII from the provided text.

PII includes but is not limited to:
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

Instructions:
1. Identify all PII entities in the text
2. Replace each PII entity with [REDACTED-TYPE] where TYPE is the category (e.g., [REDACTED-NAME], [REDACTED-EMAIL])
3. Return a JSON response with:
   - redactedText: The text with PII redacted
   - entities: Array of detected PII with type, value, startIndex, and endIndex

Example input: 'John Doe lives at 123 Main St and his email is john@example.com'
Example output:
{
  ""redactedText"": ""[REDACTED-NAME] lives at [REDACTED-ADDRESS] and his email is [REDACTED-EMAIL]"",
  ""entities"": [
    {""type"": ""NAME"", ""value"": ""John Doe"", ""startIndex"": 0, ""endIndex"": 8},
    {""type"": ""ADDRESS"", ""value"": ""123 Main St"", ""startIndex"": 19, ""endIndex"": 31},
    {""type"": ""EMAIL"", ""value"": ""john@example.com"", ""startIndex"": 49, ""endIndex"": 66}
  ]
}

Respond ONLY with valid JSON, no additional text.";

            var userPrompt = $"Text to redact:\n\n{text}";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: default);

            _logger.LogInformation("Received response from AI agent");

            var responseContent = response.Messages.LastOrDefault()?.Text ?? string.Empty;
            
            // Clean up the response - sometimes models add markdown code blocks
            responseContent = Regex.Replace(responseContent, @"^```json\s*", "", RegexOptions.Multiline);
            responseContent = Regex.Replace(responseContent, @"\s*```$", "", RegexOptions.Multiline);
            responseContent = responseContent.Trim();

            _logger.LogDebug("AI Response: {Response}", responseContent);

            // Parse the JSON response
            var aiResponse = JsonSerializer.Deserialize<AiRedactionResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (aiResponse == null)
            {
                _logger.LogWarning("Failed to parse AI response, using fallback");
                return CreateFallbackResult(text);
            }

            var result = new RedactionResult
            {
                OriginalText = text,
                RedactedText = aiResponse.RedactedText ?? text,
                DetectedEntities = aiResponse.Entities?.Select(e => new PiiEntity
                {
                    Type = e.Type ?? "UNKNOWN",
                    Value = e.Value ?? "",
                    StartIndex = e.StartIndex,
                    EndIndex = e.EndIndex
                }).ToList() ?? new List<PiiEntity>()
            };

            _logger.LogInformation("Successfully redacted PII. Found {Count} entities", result.DetectedEntities.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during PII redaction");
            throw;
        }
    }

    private RedactionResult CreateFallbackResult(string text)
    {
        // Basic fallback with simple regex patterns
        var redactedText = text;
        var entities = new List<PiiEntity>();

        // Simple email pattern
        var emailMatches = Regex.Matches(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
        foreach (Match match in emailMatches)
        {
            entities.Add(new PiiEntity
            {
                Type = "EMAIL",
                Value = match.Value,
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            });
            redactedText = redactedText.Replace(match.Value, "[REDACTED-EMAIL]");
        }

        // Simple phone pattern
        var phoneMatches = Regex.Matches(text, @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b");
        foreach (Match match in phoneMatches)
        {
            entities.Add(new PiiEntity
            {
                Type = "PHONE",
                Value = match.Value,
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            });
            redactedText = redactedText.Replace(match.Value, "[REDACTED-PHONE]");
        }

        return new RedactionResult
        {
            OriginalText = text,
            RedactedText = redactedText,
            DetectedEntities = entities
        };
    }

    private class AiRedactionResponse
    {
        public string? RedactedText { get; set; }
        public List<AiEntity>? Entities { get; set; }
    }

    private class AiEntity
    {
        public string? Type { get; set; }
        public string? Value { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }
}
