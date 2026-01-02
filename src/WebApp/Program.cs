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
var connectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

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

// ヘルスチェックの登録
builder.Services.AddHealthChecks()
    .AddCheck<DocumentIntelligenceHealthCheck>(
        "document_intelligence",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureOpenAIHealthCheck>(
        "azure_openai",
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

app.UseHttpsRedirection();

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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
