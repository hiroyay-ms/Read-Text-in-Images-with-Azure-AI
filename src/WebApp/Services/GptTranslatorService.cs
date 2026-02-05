using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using OpenAI.Chat;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// GPT-4o を使用したドキュメント翻訳サービスの実装
/// </summary>
public class GptTranslatorService : IGptTranslatorService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly DocumentAnalysisClient _documentClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _deploymentName;
    private readonly string _translatedContainerName;
    private readonly ILogger<GptTranslatorService> _logger;

    // サポートされているファイル拡張子
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx"
    };

    // 最大ファイルサイズ（40MB）
    private const long MaxFileSizeBytes = 40 * 1024 * 1024;

    // サポートされている言語
    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "auto", "自動検出" },
        { "ja", "日本語" },
        { "en", "English" },
        { "zh-Hans", "中文（简体）" },
        { "zh-Hant", "中文（繁體）" },
        { "ko", "한국어" },
        { "fr", "Français" },
        { "de", "Deutsch" },
        { "es", "Español" },
        { "it", "Italiano" },
        { "pt", "Português" },
        { "ru", "Русский" },
        { "ar", "العربية" },
        { "th", "ไทย" },
        { "vi", "Tiếng Việt" },
        { "id", "Bahasa Indonesia" },
        { "ms", "Bahasa Melayu" },
        { "nl", "Nederlands" },
        { "pl", "Polski" },
        { "tr", "Türkçe" },
        { "uk", "Українська" },
        { "cs", "Čeština" },
        { "da", "Dansk" },
        { "fi", "Suomi" },
        { "el", "Ελληνικά" },
        { "he", "עברית" },
        { "hi", "हिन्दी" },
        { "hu", "Magyar" },
        { "no", "Norsk" },
        { "ro", "Română" },
        { "sk", "Slovenčina" },
        { "sv", "Svenska" }
    };

    public GptTranslatorService(
        IConfiguration configuration,
        DocumentAnalysisClient documentClient,
        ILogger<GptTranslatorService> logger)
    {
        _logger = logger;
        _documentClient = documentClient;

        // Azure OpenAI の設定
        var openAIEndpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint が設定されていません");
        _deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName が設定されていません");

        // Azure Storage の設定
        var storageAccountName = configuration["AzureStorage:AccountName"]
            ?? throw new InvalidOperationException("AzureStorage:AccountName が設定されていません");
        _translatedContainerName = configuration["AzureStorage:TranslatedContainerName"]
            ?? "translated";

        // DefaultAzureCredential を使用してクライアントを初期化
        var credential = new DefaultAzureCredential();

        _openAIClient = new AzureOpenAIClient(
            new Uri(openAIEndpoint),
            credential);

        var blobStorageUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(blobStorageUri, credential);

        _logger.LogInformation(
            "GptTranslatorService が初期化されました。OpenAI: {Endpoint}, Storage: {StorageAccount}, Container: {Container}",
            openAIEndpoint,
            storageAccountName,
            _translatedContainerName);
    }

    /// <summary>
    /// ドキュメントの妥当性を検証します（PDF/Word のみ許可）
    /// </summary>
    public Task<bool> ValidateDocumentAsync(IFormFile document)
    {
        if (document == null || document.Length == 0)
        {
            _logger.LogWarning("ドキュメントファイルが選択されていません");
            return Task.FromResult(false);
        }

        // ファイルサイズチェック
        if (document.Length > MaxFileSizeBytes)
        {
            _logger.LogWarning("ファイルサイズが {MaxSize}MB を超えています", MaxFileSizeBytes / (1024 * 1024));
            return Task.FromResult(false);
        }

        // 拡張子チェック
        var extension = Path.GetExtension(document.FileName);
        if (!SupportedExtensions.Contains(extension))
        {
            _logger.LogWarning("サポートされていないファイル形式です: {Extension}。PDF (.pdf) または Word (.docx) のみサポートされています。", extension);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// テキストを翻訳します
    /// </summary>
    public async Task<GptTranslationResult> TranslateTextAsync(
        string text,
        string targetLanguage,
        GptTranslationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new GptTranslationOptions();

        try
        {
            _logger.LogInformation("テキスト翻訳を開始します。ターゲット言語: {TargetLanguage}", targetLanguage);

            // 言語名を取得
            var targetLanguageName = GetLanguageName(targetLanguage);

            // プロンプトを取得
            var systemPrompt = options.GetEffectiveSystemPrompt();
            var userPrompt = options.GetEffectiveUserPrompt(targetLanguageName);

            // 追加指示がある場合は追加
            if (!string.IsNullOrWhiteSpace(options.CustomInstructions))
            {
                userPrompt += $"\n\n追加指示:\n{options.CustomInstructions}";
            }

            // 原文を追加
            userPrompt += $"\n\n--- 原文 ---\n{text}";

            // ChatClient を取得して翻訳を実行
            var chatClient = _openAIClient.GetChatClient(_deploymentName);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken);

            var translatedText = response.Value.Content[0].Text ?? string.Empty;
            var completedTime = DateTime.UtcNow;

            // トークン使用量を取得
            var inputTokens = response.Value.Usage.InputTokenCount;
            var outputTokens = response.Value.Usage.OutputTokenCount;

            _logger.LogInformation(
                "テキスト翻訳が完了しました。入力トークン: {InputTokens}, 出力トークン: {OutputTokens}",
                inputTokens,
                outputTokens);

            return GptTranslationResult.Success(
                originalFileName: "",
                originalText: text,
                translatedText: translatedText,
                sourceLanguage: options.SourceLanguage ?? "auto",
                targetLanguage: targetLanguage,
                blobName: "",
                blobUrl: "",
                imageUrls: new List<string>(),
                inputTokens: inputTokens,
                outputTokens: outputTokens,
                startedAt: startTime,
                completedAt: completedTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト翻訳中にエラーが発生しました");
            return GptTranslationResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// ドキュメントを翻訳します
    /// </summary>
    public async Task<GptTranslationResult> TranslateDocumentAsync(
        IFormFile document,
        string targetLanguage,
        GptTranslationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        options ??= new GptTranslationOptions();

        try
        {
            _logger.LogInformation(
                "ドキュメント翻訳を開始します。ファイル名: {FileName}, ターゲット言語: {TargetLanguage}",
                document.FileName,
                targetLanguage);

            // 1. ドキュメントの検証
            if (!await ValidateDocumentAsync(document))
            {
                return GptTranslationResult.Failure(
                    "無効なドキュメントです。PDF (.pdf) または Word (.docx) ファイルのみサポートされています。",
                    document.FileName);
            }

            // 2. Document Intelligence でテキスト抽出
            _logger.LogInformation("Document Intelligence でテキストを抽出中...");
            
            using var stream = document.OpenReadStream();
            var analyzeOperation = await _documentClient.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-layout",
                stream,
                cancellationToken: cancellationToken);

            var analyzeResult = analyzeOperation.Value;

            // テキストを抽出（Markdown 形式で構造を保持）
            var extractedText = ExtractTextWithStructure(analyzeResult);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return GptTranslationResult.Failure("ドキュメントからテキストを抽出できませんでした。", document.FileName);
            }

            _logger.LogInformation("テキスト抽出完了。文字数: {Length}", extractedText.Length);

            // 3. 画像を抽出して Blob Storage に保存
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var documentId = Path.GetFileNameWithoutExtension(document.FileName);
            var imageUrls = await ExtractAndSaveImagesAsync(
                analyzeResult,
                documentId,
                timestamp,
                cancellationToken);

            _logger.LogInformation("画像抽出完了。画像数: {Count}", imageUrls.Count);

            // 4. GPT-4o で翻訳
            _logger.LogInformation("GPT-4o で翻訳中...");

            var translationResult = await TranslateWithChunkingAsync(
                extractedText,
                targetLanguage,
                options,
                cancellationToken);

            if (!translationResult.IsSuccess)
            {
                return translationResult;
            }

            // 5. 画像プレースホルダーを実際の URL に置換
            var translatedText = ReplaceImagePlaceholders(translationResult.TranslatedText, imageUrls);

            // 6. 翻訳結果を Blob Storage に保存
            var blobName = $"{documentId}_{targetLanguage}_{timestamp}.md";
            var blobUrl = await SaveTranslationResultAsync(blobName, translatedText, cancellationToken);

            var completedTime = DateTime.UtcNow;

            _logger.LogInformation(
                "ドキュメント翻訳が完了しました。Blob: {BlobName}, 処理時間: {Duration}秒",
                blobName,
                (completedTime - startTime).TotalSeconds);

            return GptTranslationResult.Success(
                originalFileName: document.FileName,
                originalText: extractedText,
                translatedText: translatedText,
                sourceLanguage: options.SourceLanguage ?? "auto",
                targetLanguage: targetLanguage,
                blobName: blobName,
                blobUrl: blobUrl,
                imageUrls: imageUrls,
                inputTokens: translationResult.InputTokens,
                outputTokens: translationResult.OutputTokens,
                startedAt: startTime,
                completedAt: completedTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ドキュメント翻訳中にエラーが発生しました");
            return GptTranslationResult.Failure(ex.Message, document.FileName);
        }
    }

    /// <summary>
    /// Blob Storage から翻訳結果（Markdown）を取得します
    /// </summary>
    public async Task<string> GetTranslationResultAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_translatedContainerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException($"翻訳結果が見つかりません: {blobName}");
            }

            var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
            return downloadResult.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳結果の取得中にエラーが発生しました: {BlobName}", blobName);
            throw;
        }
    }

    /// <summary>
    /// Blob Storage の Markdown を PDF に変換します
    /// </summary>
    public async Task<byte[]> ConvertToPdfAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        // TODO: Phase 21 で実装
        throw new NotImplementedException("PDF 変換機能は Phase 21 で実装予定です");
    }

    /// <summary>
    /// サポートされている言語の一覧を取得します
    /// </summary>
    public Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
    {
        return Task.FromResult(new Dictionary<string, string>(SupportedLanguages));
    }

    #region Private Methods

    /// <summary>
    /// Document Intelligence の結果から構造を保持してテキストを抽出します
    /// </summary>
    private string ExtractTextWithStructure(AnalyzeResult analyzeResult)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var paragraph in analyzeResult.Paragraphs)
        {
            var role = paragraph.Role?.ToString();

            // 見出しの処理
            if (role != null && role.StartsWith("heading", StringComparison.OrdinalIgnoreCase))
            {
                // heading レベルを取得（heading1, heading2, etc.）
                var level = 1;
                if (role.Length > 7 && int.TryParse(role.Substring(7), out var parsedLevel))
                {
                    level = parsedLevel;
                }
                sb.AppendLine();
                sb.AppendLine($"{new string('#', level)} {paragraph.Content}");
                sb.AppendLine();
            }
            else if (role == "title")
            {
                sb.AppendLine();
                sb.AppendLine($"# {paragraph.Content}");
                sb.AppendLine();
            }
            else if (role == "sectionHeading")
            {
                sb.AppendLine();
                sb.AppendLine($"## {paragraph.Content}");
                sb.AppendLine();
            }
            else
            {
                // 通常の段落
                sb.AppendLine(paragraph.Content);
                sb.AppendLine();
            }
        }

        // テーブルの処理
        foreach (var table in analyzeResult.Tables)
        {
            sb.AppendLine();
            sb.AppendLine(ConvertTableToMarkdown(table));
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// テーブルを Markdown 形式に変換します
    /// </summary>
    private string ConvertTableToMarkdown(DocumentTable table)
    {
        var rows = new Dictionary<int, Dictionary<int, string>>();
        var maxCol = 0;

        foreach (var cell in table.Cells)
        {
            if (!rows.ContainsKey(cell.RowIndex))
            {
                rows[cell.RowIndex] = new Dictionary<int, string>();
            }
            rows[cell.RowIndex][cell.ColumnIndex] = cell.Content;
            maxCol = Math.Max(maxCol, cell.ColumnIndex);
        }

        var sb = new System.Text.StringBuilder();
        var sortedRows = rows.Keys.OrderBy(k => k).ToList();

        foreach (var rowIndex in sortedRows)
        {
            var row = rows[rowIndex];
            sb.Append("|");
            for (int col = 0; col <= maxCol; col++)
            {
                var content = row.ContainsKey(col) ? row[col].Replace("|", "\\|") : "";
                sb.Append($" {content} |");
            }
            sb.AppendLine();

            // ヘッダー行の後にセパレータを追加
            if (rowIndex == 0)
            {
                sb.Append("|");
                for (int col = 0; col <= maxCol; col++)
                {
                    sb.Append(" --- |");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 画像を抽出して Blob Storage に保存します
    /// </summary>
    private async Task<List<string>> ExtractAndSaveImagesAsync(
        AnalyzeResult analyzeResult,
        string documentId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        var imageUrls = new List<string>();

        // Document Intelligence の prebuilt-layout モデルでは直接画像を取得できないため、
        // 画像抽出は将来的な拡張として実装予定
        // 現時点では空のリストを返す

        _logger.LogInformation("画像抽出: 現在の実装では画像抽出はサポートされていません");

        return imageUrls;
    }

    /// <summary>
    /// 長文を分割して翻訳します
    /// </summary>
    private async Task<GptTranslationResult> TranslateWithChunkingAsync(
        string text,
        string targetLanguage,
        GptTranslationOptions options,
        CancellationToken cancellationToken)
    {
        // 簡易的なトークン見積もり（日本語は1文字≒1-2トークン、英語は1単語≒1トークン）
        const int MaxChunkSize = 8000; // 文字数ベースで制限
        
        if (text.Length <= MaxChunkSize)
        {
            // 分割不要
            return await TranslateTextAsync(text, targetLanguage, options, cancellationToken);
        }

        _logger.LogInformation("長文のため分割翻訳を実行します。総文字数: {Length}", text.Length);

        // 段落単位で分割
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var currentChunk = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (currentChunk.Length + paragraph.Length > MaxChunkSize)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
            }
            currentChunk.AppendLine(paragraph);
            currentChunk.AppendLine();
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        _logger.LogInformation("分割数: {ChunkCount}", chunks.Count);

        // 各チャンクを翻訳
        var translatedChunks = new List<string>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var startTime = DateTime.UtcNow;

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("チャンク {Current}/{Total} を翻訳中...", i + 1, chunks.Count);
            
            var chunkResult = await TranslateTextAsync(chunks[i], targetLanguage, options, cancellationToken);
            
            if (!chunkResult.IsSuccess)
            {
                return chunkResult;
            }

            translatedChunks.Add(chunkResult.TranslatedText);
            totalInputTokens += chunkResult.InputTokens;
            totalOutputTokens += chunkResult.OutputTokens;
        }

        var completedTime = DateTime.UtcNow;
        var combinedText = string.Join("\n\n", translatedChunks);

        return GptTranslationResult.Success(
            originalFileName: "",
            originalText: text,
            translatedText: combinedText,
            sourceLanguage: options.SourceLanguage ?? "auto",
            targetLanguage: targetLanguage,
            blobName: "",
            blobUrl: "",
            imageUrls: new List<string>(),
            inputTokens: totalInputTokens,
            outputTokens: totalOutputTokens,
            startedAt: startTime,
            completedAt: completedTime);
    }

    /// <summary>
    /// 画像プレースホルダーを実際の URL に置換します
    /// </summary>
    private string ReplaceImagePlaceholders(string text, List<string> imageUrls)
    {
        var result = text;
        
        for (int i = 0; i < imageUrls.Count; i++)
        {
            var placeholder = $"[IMAGE:{i + 1:D3}]";
            var imageMarkdown = $"![図{i + 1}]({imageUrls[i]})";
            result = result.Replace(placeholder, imageMarkdown);
        }

        return result;
    }

    /// <summary>
    /// 翻訳結果を Blob Storage に保存します
    /// </summary>
    private async Task<string> SaveTranslationResultAsync(
        string blobName,
        string content,
        CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_translatedContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);

        _logger.LogInformation("翻訳結果を保存しました: {BlobName}", blobName);

        // SAS トークン付き URL を生成（1時間有効）
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _translatedContainerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// 言語コードから言語名を取得します
    /// </summary>
    private string GetLanguageName(string languageCode)
    {
        return SupportedLanguages.TryGetValue(languageCode, out var name) ? name : languageCode;
    }

    #endregion
}
