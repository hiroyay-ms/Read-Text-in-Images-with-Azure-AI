using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenAI.Chat;

namespace WebApp.Services.HealthChecks;

public class AzureOpenAIHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureOpenAIHealthCheck> _logger;

    public AzureOpenAIHealthCheck(
        IConfiguration configuration,
        ILogger<AzureOpenAIHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Azure OpenAI サービスのヘルスチェックを開始します");

            // 設定からエンドポイントとデプロイ名を取得
            var endpoint = _configuration["AzureOpenAI:Endpoint"];
            var deploymentName = _configuration["AzureOpenAI:DeploymentName"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deploymentName))
            {
                _logger.LogWarning("Azure OpenAI の設定が不完全です");
                return HealthCheckResult.Degraded(
                    "Azure OpenAI の設定が不完全です（Endpoint または DeploymentName が未設定）");
            }

            // 単純な HTTP HEAD リクエストでエンドポイントの到達可能性をチェック
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
            var response = await httpClient.SendAsync(request, cancellationToken);

            // レスポンスを受け取れたら接続OK（ステータスコードは問わない）
            _logger.LogInformation(
                "Azure OpenAI サービスのヘルスチェックが成功しました（ステータス: {StatusCode}）",
                response.StatusCode);
            return HealthCheckResult.Healthy($"Azure OpenAI サービスは正常です（デプロイ: {deploymentName})");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI サービスのヘルスチェックがタイムアウトしました");
            return HealthCheckResult.Degraded(
                "Azure OpenAI サービスへの接続がタイムアウトしました",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Azure OpenAI サービスへの接続に失敗しました");
            return HealthCheckResult.Unhealthy(
                "Azure OpenAI サービスに接続できません",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI サービスのヘルスチェックに失敗しました");
            return HealthCheckResult.Unhealthy(
                "Azure OpenAI サービスに接続できません",
                ex);
        }
    }
}
