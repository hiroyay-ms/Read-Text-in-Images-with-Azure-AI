using Azure;
using Azure.AI.Translation.Document;
using Azure.Identity;
using Azure.Storage.Blobs;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Azure Translator を使用したドキュメント翻訳サービスの実装
/// </summary>
public class AzureTranslatorService : ITranslatorService
{
    private readonly DocumentTranslationClient _translationClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _sourceContainerName;
    private readonly string _targetContainerName;
    private readonly ILogger<AzureTranslatorService> _logger;
    private readonly string _region;

    // サポートされている言語のキャッシュ
    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "af", "Afrikaans" },
        { "ar", "Arabic" },
        { "bn", "Bangla" },
        { "bs", "Bosnian" },
        { "bg", "Bulgarian" },
        { "ca", "Catalan" },
        { "zh-Hans", "Chinese Simplified" },
        { "zh-Hant", "Chinese Traditional" },
        { "hr", "Croatian" },
        { "cs", "Czech" },
        { "da", "Danish" },
        { "nl", "Dutch" },
        { "en", "English" },
        { "et", "Estonian" },
        { "fj", "Fijian" },
        { "fil", "Filipino" },
        { "fi", "Finnish" },
        { "fr", "French" },
        { "de", "German" },
        { "el", "Greek" },
        { "ht", "Haitian Creole" },
        { "he", "Hebrew" },
        { "hi", "Hindi" },
        { "hu", "Hungarian" },
        { "is", "Icelandic" },
        { "id", "Indonesian" },
        { "it", "Italian" },
        { "ja", "Japanese" },
        { "ko", "Korean" },
        { "lv", "Latvian" },
        { "lt", "Lithuanian" },
        { "mg", "Malagasy" },
        { "ms", "Malay" },
        { "mt", "Maltese" },
        { "nb", "Norwegian" },
        { "fa", "Persian" },
        { "pl", "Polish" },
        { "pt", "Portuguese" },
        { "ro", "Romanian" },
        { "ru", "Russian" },
        { "sm", "Samoan" },
        { "sr-Cyrl", "Serbian (Cyrillic)" },
        { "sr-Latn", "Serbian (Latin)" },
        { "sk", "Slovak" },
        { "sl", "Slovenian" },
        { "es", "Spanish" },
        { "sv", "Swedish" },
        { "ty", "Tahitian" },
        { "th", "Thai" },
        { "to", "Tongan" },
        { "tr", "Turkish" },
        { "uk", "Ukrainian" },
        { "ur", "Urdu" },
        { "vi", "Vietnamese" },
        { "cy", "Welsh" }
    };

    public AzureTranslatorService(
        IConfiguration configuration,
        ILogger<AzureTranslatorService> logger)
    {
        _logger = logger;

        // Azure Translator の設定を取得
        var translatorEndpoint = configuration["AzureTranslator:Endpoint"]
            ?? throw new InvalidOperationException("Azure Translator endpoint not configured");
        _region = configuration["AzureTranslator:Region"]
            ?? throw new InvalidOperationException("Azure Translator region not configured");

        // Azure Storage の設定を取得
        var storageAccountName = configuration["AzureStorage:AccountName"]
            ?? throw new InvalidOperationException("Azure Storage account name not configured");
        _sourceContainerName = configuration["AzureStorage:SourceContainerName"]
            ?? throw new InvalidOperationException("Source container name not configured");
        _targetContainerName = configuration["AzureStorage:TargetContainerName"]
            ?? throw new InvalidOperationException("Target container name not configured");

        // DefaultAzureCredential を使用してクライアントを初期化
        var credential = new DefaultAzureCredential();
        
        _translationClient = new DocumentTranslationClient(
            new Uri(translatorEndpoint),
            credential);

        var blobStorageUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(blobStorageUri, credential);

        _logger.LogInformation(
            "AzureTranslatorService が初期化されました。リージョン: {Region}, ストレージアカウント: {StorageAccount}",
            _region,
            storageAccountName);
    }

    public async Task<TranslationResult> TranslateDocumentAsync(
        IFormFile document,
        string targetLanguage,
        string? sourceLanguage = null)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation(
                "ドキュメント翻訳を開始します。ファイル名: {FileName}, ターゲット言語: {TargetLanguage}",
                document.FileName,
                targetLanguage);

            // 1. ドキュメントの検証
            if (!await ValidateDocumentAsync(document))
            {
                throw new InvalidOperationException("無効なドキュメントです");
            }

            // 2. 一意のファイル名を生成
            var uniqueId = Guid.NewGuid().ToString();
            var fileExtension = Path.GetExtension(document.FileName);
            var sourceFileName = $"{uniqueId}{fileExtension}";
            var targetFileName = $"{uniqueId}_translated{fileExtension}";

            // 3. ソースコンテナにドキュメントをアップロード
            var sourceContainerClient = _blobServiceClient.GetBlobContainerClient(_sourceContainerName);
            var sourceBlobClient = sourceContainerClient.GetBlobClient(sourceFileName);

            _logger.LogInformation("ドキュメントをアップロード中: {SourceFileName}", sourceFileName);
            
            await using var stream = document.OpenReadStream();
            await sourceBlobClient.UploadAsync(stream, overwrite: true);

            // 4. 翻訳ジョブを開始
            var sourceUri = sourceBlobClient.Uri;
            var targetContainerClient = _blobServiceClient.GetBlobContainerClient(_targetContainerName);
            var targetUri = targetContainerClient.Uri;

            var input = new DocumentTranslationInput(sourceUri, targetUri, targetLanguage);
            if (!string.IsNullOrEmpty(sourceLanguage))
            {
                input.Source.LanguageCode = sourceLanguage;
            }

            _logger.LogInformation(
                "翻訳ジョブを開始します。ソース: {SourceUri}, ターゲット: {TargetUri}",
                sourceUri,
                targetUri);

            var operation = await _translationClient.StartTranslationAsync(input);

            // 5. 翻訳完了まで同期的に待機
            _logger.LogInformation("翻訳完了を待機中...");
            await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(2));

            if (!operation.HasCompleted)
            {
                throw new TimeoutException("翻訳がタイムアウトしました");
            }

            if (operation.GetRawResponse().IsError)
            {
                throw new InvalidOperationException(
                    $"翻訳に失敗しました: {operation.GetRawResponse().ReasonPhrase}");
            }

            // 6. 翻訳済みドキュメントをダウンロード
            var translatedBlobClient = targetContainerClient.GetBlobClient(targetFileName);
            
            // Blob が存在するまで少し待機（Azure 側の遅延対策）
            for (int i = 0; i < 10; i++)
            {
                if (await translatedBlobClient.ExistsAsync())
                {
                    break;
                }
                await Task.Delay(1000);
            }

            if (!await translatedBlobClient.ExistsAsync())
            {
                throw new InvalidOperationException("翻訳済みドキュメントが見つかりません");
            }

            _logger.LogInformation("翻訳済みドキュメントをダウンロード中: {TargetFileName}", targetFileName);

            var downloadResponse = await translatedBlobClient.DownloadContentAsync();
            var translatedContent = downloadResponse.Value.Content.ToArray();

            // 7. 翻訳統計情報を取得
            int totalCharacters = 0;
            
            await foreach (var docStatus in operation.Value)
            {
                totalCharacters += (int)docStatus.CharactersCharged;
            }

            var completedTime = DateTime.UtcNow;

            var result = new TranslationResult
            {
                OriginalFileName = document.FileName,
                TranslatedFileName = $"{Path.GetFileNameWithoutExtension(document.FileName)}_translated{fileExtension}",
                TranslatedContent = translatedContent,
                ContentType = document.ContentType,
                SourceLanguage = sourceLanguage ?? "auto",
                TargetLanguage = targetLanguage,
                CharactersTranslated = totalCharacters,
                StartedAt = startTime,
                CompletedAt = completedTime
            };

            _logger.LogInformation(
                "翻訳が完了しました。文字数: {Characters}, 処理時間: {Duration}秒",
                result.CharactersTranslated,
                result.Duration.TotalSeconds);

            // 8. 一時ファイルをクリーンアップ
            try
            {
                await sourceBlobClient.DeleteIfExistsAsync();
                await translatedBlobClient.DeleteIfExistsAsync();
                _logger.LogInformation("一時ファイルをクリーンアップしました");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "一時ファイルのクリーンアップに失敗しました");
            }

            return result;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Azure Translator API エラー: StatusCode={StatusCode}, ErrorCode={ErrorCode}",
                ex.Status,
                ex.ErrorCode);

            var errorMessage = ex.Status switch
            {
                401 => "認証エラーが発生しました。Azure Translator の設定を確認してください。",
                403 => "アクセスが拒否されました。Storage Account の権限を確認してください。",
                404 => "リソースが見つかりません。コンテナの設定を確認してください。",
                429 => "リクエストが多すぎます。しばらく待ってから再試行してください。",
                500 => "Azure Translator サービスでエラーが発生しました。",
                _ => $"翻訳処理中にエラーが発生しました: {ex.Message}"
            };

            throw new InvalidOperationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラーが発生しました");
            throw new InvalidOperationException("翻訳処理中に予期しないエラーが発生しました。", ex);
        }
    }

    public Task<bool> ValidateDocumentAsync(IFormFile document)
    {
        if (document == null || document.Length == 0)
        {
            _logger.LogWarning("ドキュメントが選択されていません");
            return Task.FromResult(false);
        }

        // ファイルサイズチェック（40MB）
        const long maxSizeBytes = 40L * 1024 * 1024;
        if (document.Length > maxSizeBytes)
        {
            _logger.LogWarning(
                "ファイルサイズが大きすぎます。最大: 40MB, 実際: {ActualSize}MB",
                document.Length / 1024.0 / 1024.0);
            return Task.FromResult(false);
        }

        // 拡張子チェック
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        var supportedExtensions = new[]
        {
            ".pdf", ".docx", ".xlsx", ".pptx",
            ".html", ".htm", ".txt", ".csv", ".tsv"
        };

        if (!supportedExtensions.Contains(extension))
        {
            _logger.LogWarning("サポートされていないファイル形式です: {Extension}", extension);
            return Task.FromResult(false);
        }

        _logger.LogInformation(
            "ドキュメント検証成功: {FileName} ({Size}MB)",
            document.FileName,
            document.Length / 1024.0 / 1024.0);

        return Task.FromResult(true);
    }

    public Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
    {
        _logger.LogInformation("サポート言語一覧を返します: {Count}言語", SupportedLanguages.Count);
        return Task.FromResult(new Dictionary<string, string>(SupportedLanguages));
    }
}
