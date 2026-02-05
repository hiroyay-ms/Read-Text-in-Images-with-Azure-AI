using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebApp.Services.HealthChecks;

/// <summary>
/// Azure Translator サービスのヘルスチェック
/// </summary>
public class AzureTranslatorHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureTranslatorHealthCheck> _logger;

    public AzureTranslatorHealthCheck(
        IConfiguration configuration,
        ILogger<AzureTranslatorHealthCheck> logger)
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
            _logger.LogInformation("Azure Translator サービスのヘルスチェックを開始します");

            // 設定からエンドポイントとリージョンを取得
            var endpoint = _configuration["AzureTranslator:Endpoint"];
            var region = _configuration["AzureTranslator:Region"];

            if (string.IsNullOrEmpty(endpoint))
            {
                _logger.LogWarning("Azure Translator のエンドポイントが設定されていません");
                return HealthCheckResult.Degraded(
                    "Azure Translator のエンドポイントが未設定です");
            }

            if (string.IsNullOrEmpty(region))
            {
                _logger.LogWarning("Azure Translator のリージョンが設定されていません");
                return HealthCheckResult.Degraded(
                    "Azure Translator のリージョンが未設定です");
            }

            // Translator API のヘルスエンドポイントにリクエスト
            // https://api.cognitive.microsofttranslator.com/languages?api-version=3.0
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var healthEndpoint = $"{endpoint.TrimEnd('/')}/languages?api-version=3.0";
            var request = new HttpRequestMessage(HttpMethod.Get, healthEndpoint);
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Azure Translator サービスのヘルスチェックが成功しました（リージョン: {Region}）",
                    region);
                return HealthCheckResult.Healthy(
                    $"Azure Translator サービスは正常です（リージョン: {region}）");
            }
            else
            {
                _logger.LogWarning(
                    "Azure Translator サービスが異常なステータスを返しました: {StatusCode}",
                    response.StatusCode);
                return HealthCheckResult.Degraded(
                    $"Azure Translator サービスが異常なステータスを返しました: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure Translator サービスのヘルスチェックがタイムアウトしました");
            return HealthCheckResult.Degraded(
                "Azure Translator サービスへの接続がタイムアウトしました",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Azure Translator サービスへの接続に失敗しました");
            return HealthCheckResult.Unhealthy(
                "Azure Translator サービスに接続できません",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Translator サービスのヘルスチェックに失敗しました");
            return HealthCheckResult.Unhealthy(
                "Azure Translator サービスに接続できません",
                ex);
        }
    }
}
