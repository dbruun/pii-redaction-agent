using Azure.Identity;
using Azure.Storage.Blobs;
using PiiRedactionWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Configure Azure Language options (for Native Document PII API)
builder.Services.Configure<AzureAiOptions>(
    builder.Configuration.GetSection(AzureAiOptions.SectionName));

// Configure Azure Storage options (required for Native Document PII API)
builder.Services.Configure<AzureStorageOptions>(
    builder.Configuration.GetSection("AzureStorage"));

// Register BlobServiceClient for Native Document PII API
var storageConfig = builder.Configuration.GetSection("AzureStorage").Get<AzureStorageOptions>();

if (storageConfig != null && !string.IsNullOrEmpty(storageConfig.ConnectionString))
{
    BlobServiceClient blobServiceClient;
    
    if (storageConfig.UseAzureIdentity)
    {
        // Extract account name from connection string to build endpoint URL
        var accountName = ExtractStorageAccountName(storageConfig.ConnectionString);
        if (!string.IsNullOrEmpty(accountName))
        {
            var blobEndpoint = new Uri($"https://{accountName}.blob.core.windows.net");
            blobServiceClient = new BlobServiceClient(blobEndpoint, new DefaultAzureCredential());
        }
        else
        {
            // Fallback to connection string if account name cannot be extracted
            blobServiceClient = new BlobServiceClient(storageConfig.ConnectionString);
        }
    }
    else
    {
        blobServiceClient = new BlobServiceClient(storageConfig.ConnectionString);
    }

    builder.Services.AddSingleton(blobServiceClient);

    // Register HttpClient for Native Document PII Service
    builder.Services.AddHttpClient<INativeDocumentPiiService, NativeDocumentPiiService>();

    // Register Native Document PII service
    builder.Services.AddScoped<INativeDocumentPiiService, NativeDocumentPiiService>();
}
else
{
    throw new InvalidOperationException(
        "Azure Storage is not configured. Native Document PII API requires Azure Blob Storage. " +
        "Please configure AzureStorage:ConnectionString in appsettings.json or environment variables.");
}

// Helper method to extract storage account name from connection string
static string? ExtractStorageAccountName(string connectionString)
{
    try
    {
        var parts = connectionString.Split(';');
        var accountNamePart = parts.FirstOrDefault(p => p.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase));
        return accountNamePart?.Split('=')[1];
    }
    catch
    {
        return null;
    }
}

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
