using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PiiRedactionWebApp.Services;

/// <summary>
/// A simple chat client for testing and development when Azure OpenAI is not available
/// </summary>
public class SimpleChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("simple-chat-client", null, "simple-model");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Extract the user message
        var messagesList = chatMessages.ToList();
        var userMessage = messagesList.LastOrDefault(m => m.Role == ChatRole.User);
        if (userMessage == null || string.IsNullOrEmpty(userMessage.Text))
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));
        }

        var text = userMessage.Text;

        // Simple PII detection using regex
        var entities = new List<object>();
        var redactedText = text;

        // Detect emails
        var emailMatches = Regex.Matches(text, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
        foreach (Match match in emailMatches)
        {
            entities.Add(new
            {
                type = "EMAIL",
                value = match.Value,
                startIndex = match.Index,
                endIndex = match.Index + match.Length
            });
        }

        // Detect phone numbers
        var phoneMatches = Regex.Matches(text, @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b");
        foreach (Match match in phoneMatches)
        {
            entities.Add(new
            {
                type = "PHONE",
                value = match.Value,
                startIndex = match.Index,
                endIndex = match.Index + match.Length
            });
        }

        // Detect SSN patterns
        var ssnMatches = Regex.Matches(text, @"\b\d{3}-\d{2}-\d{4}\b");
        foreach (Match match in ssnMatches)
        {
            entities.Add(new
            {
                type = "SSN",
                value = match.Value,
                startIndex = match.Index,
                endIndex = match.Index + match.Length
            });
        }

        // Detect credit card patterns (simple 16 digit)
        var ccMatches = Regex.Matches(text, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b");
        foreach (Match match in ccMatches)
        {
            entities.Add(new
            {
                type = "CREDIT_CARD",
                value = match.Value,
                startIndex = match.Index,
                endIndex = match.Index + match.Length
            });
        }

        // Detect ZIP codes
        var zipMatches = Regex.Matches(text, @"\b\d{5}(?:-\d{4})?\b");
        foreach (Match match in zipMatches)
        {
            entities.Add(new
            {
                type = "ZIP_CODE",
                value = match.Value,
                startIndex = match.Index,
                endIndex = match.Index + match.Length
            });
        }

        // Sort entities by position (descending) to replace from end to start
        var sortedEntities = entities
            .Cast<dynamic>()
            .OrderByDescending(e => e.startIndex)
            .ToList();

        // Replace PII with redaction markers
        foreach (var entity in sortedEntities)
        {
            var replacement = $"[REDACTED-{entity.type}]";
            redactedText = redactedText.Remove(entity.startIndex, entity.endIndex - entity.startIndex);
            redactedText = redactedText.Insert(entity.startIndex, replacement);
        }

        // Create JSON response
        var response = System.Text.Json.JsonSerializer.Serialize(new
        {
            redactedText = redactedText,
            entities = entities.Cast<dynamic>().OrderBy(e => e.startIndex).ToList()
        });

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        var text = response.Messages.LastOrDefault()?.Text ?? string.Empty;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
