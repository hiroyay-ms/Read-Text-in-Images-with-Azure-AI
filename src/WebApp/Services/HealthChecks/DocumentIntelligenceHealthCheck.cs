using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebApp.Services.HealthChecks;

public class DocumentIntelligenceHealthCheck : IHealthCheck
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<DocumentIntelligenceHealthCheck> _logger;
    private readonly IConfiguration _configuration;

    public DocumentIntelligenceHealthCheck(
        DocumentAnalysisClient client,
        ILogger<DocumentIntelligenceHealthCheck> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Document Intelligence サービスのヘルスチェックを開始します");

            // エンドポイント取得
            var endpoint = _configuration["DocumentIntelligence_Endpoint"];
            if (string.IsNullOrEmpty(endpoint))
            {
                return HealthCheckResult.Unhealthy("Document Intelligence エンドポイントが設定されていません");
            }

            // 単純な HTTP HEAD リクエストでエンドポイントの到達可能性をチェック
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
            var response = await httpClient.SendAsync(request, cancellationToken);

            // レスポンスを受け取れたら接続OK（ステータスコードは問わない）
            _logger.LogInformation(
                "Document Intelligence サービスのヘルスチェックが成功しました（ステータス: {StatusCode}）",
                response.StatusCode);
            return HealthCheckResult.Healthy("Document Intelligence サービスは正常です");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Document Intelligence サービスのヘルスチェックがタイムアウトしました");
            return HealthCheckResult.Degraded(
                "Document Intelligence サービスへの接続がタイムアウトしました",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Document Intelligence サービスへの接続に失敗しました");
            return HealthCheckResult.Unhealthy(
                "Document Intelligence サービスに接続できません",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document Intelligence サービスのヘルスチェックに失敗しました");
            return HealthCheckResult.Unhealthy(
                "Document Intelligence サービスに接続できません",
                ex);
        }
    }
}
