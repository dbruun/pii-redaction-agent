using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.Runtime.CompilerServices;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace PiiRedactionWebApp.Services;

/// <summary>
/// Wrapper to adapt OpenAI Client to IChatClient interface
/// </summary>
public class OpenAIChatClientWrapper : IChatClient
{
    private readonly OpenAIClient _client;
    private readonly string _deploymentName;

    public OpenAIChatClientWrapper(OpenAIClient client, string deploymentName)
    {
        _client = client;
        _deploymentName = deploymentName;
    }

    public ChatClientMetadata Metadata => new("openai-wrapper", null, _deploymentName);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Convert Microsoft.Extensions.AI ChatMessage to OpenAI ChatMessage
        var openAiMessages = new List<OpenAI.Chat.ChatMessage>();
        
        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                openAiMessages.Add(new SystemChatMessage(msg.Text ?? string.Empty));
            }
            else if (msg.Role == ChatRole.User)
            {
                openAiMessages.Add(new UserChatMessage(msg.Text ?? string.Empty));
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                openAiMessages.Add(new AssistantChatMessage(msg.Text ?? string.Empty));
            }
        }

        var chatClient = _client.GetChatClient(_deploymentName);
        var completion = await chatClient.CompleteChatAsync(openAiMessages, cancellationToken: cancellationToken);

        var assistantMessage = completion.Value.Content.FirstOrDefault()?.Text ?? string.Empty;
        
        return new ChatResponse([new AIChatMessage(ChatRole.Assistant, assistantMessage)]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AIChatMessage> chatMessages,
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
