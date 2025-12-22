using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using WebApp.Models;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// FileUploadOptions の登録
builder.Services.Configure<FileUploadOptions>(
    builder.Configuration.GetSection("FileUpload"));

// Azure Document Intelligence クライアントの登録（Entra ID 認証）
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["DocumentIntelligence_Endpoint"];

    if (string.IsNullOrEmpty(endpoint))
    {
        throw new InvalidOperationException(
            "DocumentIntelligence_Endpoint が appsettings.Development.json に設定されていません。");
    }

    // Entra ID (Azure AD) 認証を使用
    var credential = new DefaultAzureCredential();
    return new DocumentAnalysisClient(new Uri(endpoint), credential);
});

// OCR サービスの登録
builder.Services.AddScoped<IOcrService, DocumentIntelligenceService>();

// GPT-4o Vision サービスの登録
builder.Services.AddScoped<IGptVisionService, OpenAIVisionService>();

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
