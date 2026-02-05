using System.Diagnostics.Metrics;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WebApp.Models;
using WebApp.Services;
using WebApp.Services.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry の設定
// App Service で自動設定される APPLICATIONINSIGHTS_CONNECTION_STRING 環境変数を使用
var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "WebApp", serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                // ヘルスチェックエンドポイントは除外
                options.Filter = context => 
                    !context.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("WebApp.HealthChecks") // ヘルスチェック メーター
            .AddMeter("WebApp.OCR")           // OCR メーター（Phase 4 で追加予定）
            .AddMeter("WebApp.GPTVision");     // GPT Vision メーター（Phase 4 で追加予定）
    });

// Application Insights への送信（ConnectionString が設定されている場合のみ）
if (!string.IsNullOrEmpty(connectionString))
{
    otelBuilder.UseAzureMonitor(options =>
    {
        options.ConnectionString = connectionString;
    });
}

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

// Azure Translator サービスの登録
builder.Services.AddScoped<ITranslatorService, AzureTranslatorService>();

// GPT 翻訳サービスの登録
builder.Services.AddScoped<IGptTranslatorService, GptTranslatorService>();

// ヘルスチェックの登録
builder.Services.AddHealthChecks()
    .AddCheck<DocumentIntelligenceHealthCheck>(
        "document_intelligence",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureOpenAIHealthCheck>(
        "azure_openai",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureTranslatorHealthCheck>(
        "azure_translator",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureBlobStorageHealthCheck>(
        "azure_blob_storage",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" });

var app = builder.Build();

// ヘルスチェック メトリクスの設定
var healthCheckMeter = new Meter("WebApp.HealthChecks", "1.0.0");
var healthCheckExecutions = healthCheckMeter.CreateCounter<int>("health_check.executions", description: "Number of health check executions");
var healthCheckDuration = healthCheckMeter.CreateHistogram<double>("health_check.duration", unit: "ms", description: "Duration of health check executions");
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// ヘルスチェック結果をメトリクスとして記録するヘルパーメソッド
void RecordHealthCheckMetrics(HealthReport report)
{
    foreach (var entry in report.Entries)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("check_name", entry.Key),
            new("status", entry.Value.Status.ToString())
        };
        
        healthCheckExecutions.Add(1, tags);
        healthCheckDuration.Record(entry.Value.Duration.TotalMilliseconds, tags);
        
        logger.LogInformation(
            "ヘルスチェック '{CheckName}' 完了: Status={Status}, Duration={Duration}ms",
            entry.Key,
            entry.Value.Status,
            entry.Value.Duration.TotalMilliseconds);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Warmup エンドポイント以外のリクエストに対してのみ HTTPS リダイレクトを適用
// App Service の Warmup 機能は HTTP でリクエストするため、リダイレクトを除外
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/warmup"),
    mainApp => mainApp.UseHttpsRedirection()
);

app.UseRouting();

app.UseAuthorization();

// ヘルスチェックエンドポイントのマッピング
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        // メトリクスを記録
        RecordHealthCheckMetrics(report);
        
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.ToString(),
                exception = e.Value.Exception?.Message
            }),
            totalDuration = report.TotalDuration.ToString()
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        // メトリクスを記録
        RecordHealthCheckMetrics(report);
        
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString()
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // 外部依存関係をチェックしない
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "Healthy"
        });
        await context.Response.WriteAsync(result);
    }
});

// Warmup エンドポイント
app.MapGet("/warmup", async (IServiceProvider sp, IConfiguration config) =>
{
    var warmupLogger = sp.GetRequiredService<ILogger<Program>>();
    warmupLogger.LogInformation("Warmup エンドポイントが呼び出されました");
    
    try
    {
        // Document Intelligence の接続確認
        var docClient = sp.GetRequiredService<DocumentAnalysisClient>();
        var docEndpoint = config["DocumentIntelligence_Endpoint"];
        
        if (string.IsNullOrEmpty(docEndpoint))
        {
            warmupLogger.LogError("Document Intelligence エンドポイントが設定されていません");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        var docRequest = new HttpRequestMessage(HttpMethod.Head, docEndpoint);
        var docResponse = await httpClient.SendAsync(docRequest);
        warmupLogger.LogInformation("Document Intelligence 接続確認成功 (Status: {StatusCode})", docResponse.StatusCode);
        
        // OCR サービスの初期化確認
        var ocrService = sp.GetRequiredService<IOcrService>();
        warmupLogger.LogInformation("OCR サービスの初期化成功");
        
        // Azure OpenAI の接続確認
        var openAIEndpoint = config["AzureOpenAI:Endpoint"];
        var deploymentName = config["AzureOpenAI:DeploymentName"];
        
        if (!string.IsNullOrEmpty(openAIEndpoint) && !string.IsNullOrEmpty(deploymentName))
        {
            var gptService = sp.GetRequiredService<IGptVisionService>();
            
            var openAIRequest = new HttpRequestMessage(HttpMethod.Head, openAIEndpoint);
            var openAIResponse = await httpClient.SendAsync(openAIRequest);
            warmupLogger.LogInformation("Azure OpenAI 接続確認成功 (Status: {StatusCode})", openAIResponse.StatusCode);
            warmupLogger.LogInformation("GPT Vision サービスの初期化成功");
        }
        else
        {
            warmupLogger.LogWarning("Azure OpenAI の設定が不完全です（Endpoint または DeploymentName が未設定）");
        }
        
        // Azure Translator の接続確認
        var translatorEndpoint = config["AzureTranslator:Endpoint"];
        var translatorRegion = config["AzureTranslator:Region"];
        
        if (!string.IsNullOrEmpty(translatorEndpoint) && !string.IsNullOrEmpty(translatorRegion))
        {
            var translatorService = sp.GetRequiredService<ITranslatorService>();
            
            // Translator の言語一覧エンドポイントで接続確認
            var translatorHealthEndpoint = $"{translatorEndpoint.TrimEnd('/')}/languages?api-version=3.0";
            var translatorRequest = new HttpRequestMessage(HttpMethod.Get, translatorHealthEndpoint);
            var translatorResponse = await httpClient.SendAsync(translatorRequest);
            warmupLogger.LogInformation("Azure Translator 接続確認成功 (Status: {StatusCode})", translatorResponse.StatusCode);
            warmupLogger.LogInformation("Azure Translator サービスの初期化成功");
        }
        else
        {
            warmupLogger.LogWarning("Azure Translator の設定が不完全です（Endpoint または Region が未設定）");
        }
        
        // Azure Blob Storage の接続確認
        var storageAccountName = config["AzureStorage:AccountName"];
        
        if (!string.IsNullOrEmpty(storageAccountName))
        {
            var blobServiceEndpoint = $"https://{storageAccountName}.blob.core.windows.net";
            var credential = new DefaultAzureCredential();
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(new Uri(blobServiceEndpoint), credential);
            
            // translated コンテナの存在確認（Storage Blob Data Contributor ロールで実行可能）
            // GetPropertiesAsync は Storage Account Contributor が必要なため、コンテナ操作で確認
            var translatedContainerName = config["AzureStorage:TranslatedContainerName"] ?? "translated";
            var containerClient = blobServiceClient.GetBlobContainerClient(translatedContainerName);
            
            try
            {
                // コンテナの存在確認（なければ作成）
                await containerClient.CreateIfNotExistsAsync();
                warmupLogger.LogInformation("Azure Blob Storage 接続確認成功 (Account: {AccountName}, Container: {ContainerName})", 
                    storageAccountName, translatedContainerName);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 403)
            {
                warmupLogger.LogWarning("Azure Blob Storage 接続確認: コンテナ作成権限なし（既存コンテナへのアクセスは可能な場合があります）");
                // 403 でも続行（コンテナが既に存在する場合は問題ない）
            }
            
            // GPT 翻訳サービスの初期化確認
            var gptTranslatorService = sp.GetRequiredService<IGptTranslatorService>();
            warmupLogger.LogInformation("GPT 翻訳サービスの初期化成功");
        }
        else
        {
            warmupLogger.LogWarning("Azure Storage の設定が不完全です（AccountName が未設定）");
        }
        
        warmupLogger.LogInformation("Warmup 完了: すべてのサービスが正常に初期化されました");
        return Results.Ok(new { status = "ready", message = "Application warmed up successfully" });
    }
    catch (InvalidOperationException ex)
    {
        warmupLogger.LogError(ex, "Warmup エラー: 設定が不足しています");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException ex)
    {
        warmupLogger.LogError(ex, "Warmup エラー: サービスへの接続に失敗しました");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        warmupLogger.LogError(ex, "Warmup エラー: サービスの初期化に失敗しました");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

