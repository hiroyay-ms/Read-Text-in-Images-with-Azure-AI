using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebApp.Services.HealthChecks;

/// <summary>
/// Azure Blob Storage のヘルスチェック
/// 翻訳機能で使用するコンテナ（source, target, translated）の接続を確認
/// </summary>
public class AzureBlobStorageHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureBlobStorageHealthCheck> _logger;

    public AzureBlobStorageHealthCheck(
        IConfiguration configuration,
        ILogger<AzureBlobStorageHealthCheck> logger)
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
            _logger.LogInformation("Azure Blob Storage のヘルスチェックを開始します");

            // 設定からストレージアカウント名とコンテナ名を取得
            var accountName = _configuration["AzureStorage:AccountName"];
            var sourceContainer = _configuration["AzureStorage:SourceContainerName"];
            var targetContainer = _configuration["AzureStorage:TargetContainerName"];
            var translatedContainer = _configuration["AzureStorage:TranslatedContainerName"];

            if (string.IsNullOrEmpty(accountName))
            {
                _logger.LogWarning("Azure Storage のアカウント名が設定されていません");
                return HealthCheckResult.Degraded(
                    "Azure Storage のアカウント名が未設定です");
            }

            // Blob Service Client を作成（Entra ID 認証）
            var blobServiceEndpoint = $"https://{accountName}.blob.core.windows.net";
            var credential = new DefaultAzureCredential();
            var blobServiceClient = new BlobServiceClient(new Uri(blobServiceEndpoint), credential);

            // サービスプロパティを取得して接続確認
            var properties = await blobServiceClient.GetPropertiesAsync(cancellationToken);

            var containerStatus = new List<string>();

            // 各コンテナの存在確認
            if (!string.IsNullOrEmpty(sourceContainer))
            {
                var sourceContainerClient = blobServiceClient.GetBlobContainerClient(sourceContainer);
                var sourceExists = await sourceContainerClient.ExistsAsync(cancellationToken);
                containerStatus.Add($"source({sourceContainer}): {(sourceExists ? "OK" : "未作成")}");
            }

            if (!string.IsNullOrEmpty(targetContainer))
            {
                var targetContainerClient = blobServiceClient.GetBlobContainerClient(targetContainer);
                var targetExists = await targetContainerClient.ExistsAsync(cancellationToken);
                containerStatus.Add($"target({targetContainer}): {(targetExists ? "OK" : "未作成")}");
            }

            if (!string.IsNullOrEmpty(translatedContainer))
            {
                var translatedContainerClient = blobServiceClient.GetBlobContainerClient(translatedContainer);
                var translatedExists = await translatedContainerClient.ExistsAsync(cancellationToken);
                containerStatus.Add($"translated({translatedContainer}): {(translatedExists ? "OK" : "未作成")}");
            }

            var statusMessage = $"Azure Blob Storage は正常です（アカウント: {accountName}）";
            if (containerStatus.Count > 0)
            {
                statusMessage += $" - コンテナ: {string.Join(", ", containerStatus)}";
            }

            _logger.LogInformation(
                "Azure Blob Storage のヘルスチェックが成功しました（アカウント: {AccountName}）",
                accountName);

            return HealthCheckResult.Healthy(statusMessage);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Azure Blob Storage のヘルスチェックがタイムアウトしました");
            return HealthCheckResult.Degraded(
                "Azure Blob Storage への接続がタイムアウトしました",
                ex);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogError(ex, "Azure Blob Storage へのアクセス権限がありません");
            return HealthCheckResult.Unhealthy(
                "Azure Blob Storage へのアクセス権限がありません（RBAC 設定を確認してください）",
                ex);
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure Blob Storage へのリクエストに失敗しました");
            return HealthCheckResult.Unhealthy(
                $"Azure Blob Storage へのリクエストに失敗しました: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob Storage のヘルスチェックに失敗しました");
            return HealthCheckResult.Unhealthy(
                "Azure Blob Storage に接続できません",
                ex);
        }
    }
}
