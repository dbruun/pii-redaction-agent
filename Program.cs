using Azure.Identity;
using Microsoft.Extensions.AI;
using PiiRedactionWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Configure Azure AI options
builder.Services.Configure<AzureAiOptions>(
    builder.Configuration.GetSection(AzureAiOptions.SectionName));

// Register AI Chat Client using Microsoft.Extensions.AI (Agentic Framework)
var azureAiConfig = builder.Configuration.GetSection(AzureAiOptions.SectionName).Get<AzureAiOptions>();

if (azureAiConfig != null && !string.IsNullOrEmpty(azureAiConfig.Endpoint))
{
    if (azureAiConfig.UseAzureIdentity)
    {
        // Use Azure Identity (Managed Identity, DefaultAzureCredential)
        var credential = new DefaultAzureCredential();
        var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential("placeholder"), 
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(azureAiConfig.Endpoint) });
        builder.Services.AddSingleton<IChatClient>(new OpenAIChatClientWrapper(openAiClient, azureAiConfig.DeploymentName));
    }
    else if (!string.IsNullOrEmpty(azureAiConfig.ApiKey))
    {
        // Use API Key for Azure OpenAI
        var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(azureAiConfig.ApiKey), 
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(azureAiConfig.Endpoint) });
        builder.Services.AddSingleton<IChatClient>(new OpenAIChatClientWrapper(openAiClient, azureAiConfig.DeploymentName));
    }
    else
    {
        // Fallback to a simple chat client for development/testing
        builder.Services.AddSingleton<IChatClient>(new SimpleChatClient());
    }
}
else
{
    // Fallback to a simple chat client for development/testing
    builder.Services.AddSingleton<IChatClient>(new SimpleChatClient());
}

// Register PII Redaction Service
builder.Services.AddScoped<IPiiRedactionService, PiiRedactionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
