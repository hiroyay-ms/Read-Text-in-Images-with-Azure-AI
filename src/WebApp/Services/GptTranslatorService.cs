using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using OpenAI.Chat;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// GPT-4o を使用したドキュメント翻訳サービスの実装
/// Document Intelligence v4.0 の Markdown 出力と Figures API を使用して画像を保持
/// </summary>
public class GptTranslatorService : IGptTranslatorService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly DocumentIntelligenceClient _documentIntelligenceClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _deploymentName;
    private readonly string _translatedContainerName;
    private readonly string _documentIntelligenceEndpoint;
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
        { "hu", "Magyar" },
        { "no", "Norsk" },
        { "ro", "Română" },
        { "sk", "Slovenčina" },
        { "sv", "Svenska" }
    };

    public GptTranslatorService(
        IConfiguration configuration,
        ILogger<GptTranslatorService> logger)
    {
        _logger = logger;

        // Azure OpenAI の設定
        var openAIEndpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint が設定されていません");
        _deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName が設定されていません");

        // Document Intelligence の設定
        _documentIntelligenceEndpoint = configuration["DocumentIntelligence_Endpoint"]
            ?? throw new InvalidOperationException("DocumentIntelligence_Endpoint が設定されていません");

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

        _documentIntelligenceClient = new DocumentIntelligenceClient(
            new Uri(_documentIntelligenceEndpoint),
            credential);

        var blobStorageUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(blobStorageUri, credential);

        _logger.LogInformation(
            "GptTranslatorService が初期化されました。OpenAI: {OpenAIEndpoint}, DocumentIntelligence: {DIEndpoint}, Storage: {StorageAccount}, Container: {Container}",
            openAIEndpoint,
            _documentIntelligenceEndpoint,
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
    /// Document Intelligence v4.0 の Markdown 出力を使用し、画像を保持
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

            // 2. Document Intelligence v4.0 で Markdown 形式でテキスト抽出（画像情報を含む）
            _logger.LogInformation("Document Intelligence v4.0 で Markdown 形式でテキストと画像を抽出中...");

            using var stream = document.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            // Base64 エンコードしたドキュメントを送信
            var base64Content = Convert.ToBase64String(memoryStream.ToArray());

            using var requestContent = RequestContent.Create(new
            {
                base64Source = base64Content
            });

            // Markdown 形式で出力し、Figures も取得
            // RequestContext を使用して追加パラメータを指定
            var requestContext = new RequestContext();
            var operation = await _documentIntelligenceClient.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-layout",
                requestContent,
                pages: null,
                locale: null,
                stringIndexType: null,
                features: null,
                queryFields: null,
                outputContentFormat: "markdown",
                output: null,
                context: requestContext);

            // レスポンスを JSON としてパース
            var responseJson = JsonDocument.Parse(operation.Value.ToStream());
            var analyzeResult = responseJson.RootElement;

            // Markdown テキストを取得（図は <figure> タグで埋め込まれている）
            string extractedMarkdown;
            if (analyzeResult.TryGetProperty("content", out var contentElement))
            {
                extractedMarkdown = contentElement.GetString() ?? string.Empty;
            }
            else
            {
                _logger.LogWarning("Document Intelligence レスポンスに content プロパティがありません");
                return GptTranslationResult.Failure("ドキュメントの解析結果を取得できませんでした。", document.FileName);
            }

            if (string.IsNullOrWhiteSpace(extractedMarkdown))
            {
                return GptTranslationResult.Failure("ドキュメントからテキストを抽出できませんでした。", document.FileName);
            }

            _logger.LogInformation("Markdown 抽出完了。文字数: {Length}", extractedMarkdown.Length);

            // 3. 図（Figures）を抽出して Blob Storage に保存
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var documentId = Path.GetFileNameWithoutExtension(document.FileName);
            var operationId = operation.Id;

            // Figures 情報を取得
            var figures = analyzeResult.TryGetProperty("figures", out var figuresElement)
                ? figuresElement.EnumerateArray().ToList()
                : new List<JsonElement>();

            var imageUrls = await ExtractAndSaveFiguresAsync(
                figures,
                operationId,
                documentId,
                timestamp,
                cancellationToken);

            _logger.LogInformation("画像抽出完了。画像数: {Count}", imageUrls.Count);

            // 4. Markdown 内の図参照を Blob URL に置換
            var markdownWithImages = ReplaceFigureReferences(extractedMarkdown, figures, imageUrls);

            // 5. GPT-4o で翻訳（Markdown 形式を保持）
            _logger.LogInformation("GPT-4o で翻訳中...");

            var translationResult = await TranslateMarkdownAsync(
                markdownWithImages,
                targetLanguage,
                options,
                cancellationToken);

            if (!translationResult.IsSuccess)
            {
                return translationResult;
            }

            // 6. 翻訳結果を Blob Storage に保存
            var blobName = $"{documentId}_{targetLanguage}_{timestamp}.md";
            var blobUrl = await SaveTranslationResultAsync(blobName, translationResult.TranslatedText, cancellationToken);

            var completedTime = DateTime.UtcNow;

            _logger.LogInformation(
                "ドキュメント翻訳が完了しました。Blob: {BlobName}, 処理時間: {Duration}秒",
                blobName,
                (completedTime - startTime).TotalSeconds);

            return GptTranslationResult.Success(
                originalFileName: document.FileName,
                originalText: markdownWithImages,
                translatedText: translationResult.TranslatedText,
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
    /// サポートされている言語の一覧を取得します
    /// </summary>
    public Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
    {
        return Task.FromResult(new Dictionary<string, string>(SupportedLanguages));
    }

    #region Private Methods

    /// <summary>
    /// Document Intelligence の Figures を抽出して Blob Storage に保存します
    /// </summary>
    private async Task<List<string>> ExtractAndSaveFiguresAsync(
        List<JsonElement> figures,
        string operationId,
        string documentId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        var imageUrls = new List<string>();

        if (figures == null || figures.Count == 0)
        {
            _logger.LogInformation("ドキュメントに画像が含まれていません");
            return imageUrls;
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(_translatedContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        foreach (var figure in figures)
        {
            try
            {
                // Figure ID を使用して画像を取得
                if (!figure.TryGetProperty("id", out var idElement))
                {
                    _logger.LogWarning("図に id プロパティがありません。スキップします。");
                    continue;
                }
                var figureId = idElement.GetString() ?? "";
                if (string.IsNullOrEmpty(figureId))
                {
                    _logger.LogWarning("図の id が空です。スキップします。");
                    continue;
                }

                _logger.LogInformation("図を取得中: {FigureId}", figureId);

                // GetAnalyzeResultFigure API を使用して画像を取得
                var figureResponse = await _documentIntelligenceClient.GetAnalyzeResultFigureAsync(
                    "prebuilt-layout",
                    operationId,
                    figureId,
                    cancellationToken);

                if (figureResponse != null && figureResponse.Value != null)
                {
                    // 画像を Blob Storage に保存
                    var safeImageName = figureId.Replace(".", "_");
                    var imageName = $"images/{documentId}_{timestamp}/{safeImageName}.png";
                    var blobClient = containerClient.GetBlobClient(imageName);

                    using var imageStream = figureResponse.Value.ToStream();
                    await blobClient.UploadAsync(imageStream, overwrite: true, cancellationToken);

                    // SAS トークン付き URL を生成（24時間有効）
                    string imageUrl;
                    if (blobClient.CanGenerateSasUri)
                    {
                        var sasBuilder = new BlobSasBuilder
                        {
                            BlobContainerName = _translatedContainerName,
                            BlobName = imageName,
                            Resource = "b",
                            ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
                        };
                        sasBuilder.SetPermissions(BlobSasPermissions.Read);
                        imageUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
                    }
                    else
                    {
                        imageUrl = blobClient.Uri.ToString();
                    }

                    imageUrls.Add(imageUrl);
                    _logger.LogInformation("図を保存しました: {ImageName}", imageName);
                }
            }
            catch (Exception ex)
            {
                var errorFigureId = figure.TryGetProperty("id", out var idElement) ? idElement.GetString() : "unknown";
                _logger.LogWarning(ex, "図の取得に失敗しました: {FigureId}", errorFigureId);
                // 個別の画像取得失敗は処理を継続
            }
        }

        return imageUrls;
    }

    /// <summary>
    /// Markdown 内の図参照を実際の Blob URL に置換します
    /// </summary>
    private string ReplaceFigureReferences(string markdown, List<JsonElement> figures, List<string> imageUrls)
    {
        if (figures == null || figures.Count == 0 || imageUrls.Count == 0)
        {
            return markdown;
        }

        var result = markdown;

        // Document Intelligence v4.0 の Markdown では図は以下の形式で出力される:
        // <figure>
        // <figcaption>キャプション</figcaption>
        // ![](figures/0)
        // FigureContent="..."
        // </figure>
        //
        // または単純に:
        // ![](figures/0)
        // ![](figures/1.1)  <- pageNumber.figureIndex 形式

        for (int i = 0; i < Math.Min(figures.Count, imageUrls.Count); i++)
        {
            var figure = figures[i];
            var figureId = figure.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            var imageUrl = imageUrls[i];

            // figures/ID 形式の参照を実際の URL に置換
            // 例: figures/1.1, figures/2.1 など
            result = result.Replace($"![]({figureId})", $"![図{i + 1}]({imageUrl})");
            result = result.Replace($"](figures/{figureId})", $"]({imageUrl})");
            
            // 単純な連番形式も対応
            result = result.Replace($"![](figures/{i})", $"![図{i + 1}]({imageUrl})");
        }

        return result;
    }

    /// <summary>
    /// Markdown テキストを翻訳します（構造と画像参照を保持）
    /// </summary>
    private async Task<GptTranslationResult> TranslateMarkdownAsync(
        string markdown,
        string targetLanguage,
        GptTranslationOptions options,
        CancellationToken cancellationToken)
    {
        // 翻訳用のオプションを調整（Markdown 構造を保持するよう指示を追加）
        var markdownOptions = new GptTranslationOptions
        {
            SourceLanguage = options.SourceLanguage,
            Tone = options.Tone,
            Domain = options.Domain,
            CustomInstructions = options.CustomInstructions,
            SystemPrompt = options.SystemPrompt,
            UserPrompt = options.UserPrompt
        };

        // システムプロンプトに Markdown 保持の指示を追加
        var defaultSystemPrompt = markdownOptions.GetEffectiveSystemPrompt();
        if (!defaultSystemPrompt.Contains("Markdown"))
        {
            markdownOptions.SystemPrompt = defaultSystemPrompt + @"

重要な追加指示:
- 入力は Markdown 形式です。Markdown の構造（見出し、リスト、表、コードブロック）をそのまま保持してください
- 画像参照 ![...](URL) は翻訳せず、そのまま保持してください
- <figure> タグ内の構造も保持してください
- HTML タグがある場合は構造を保持してください";
        }

        // 長文の場合は分割翻訳
        const int MaxChunkSize = 8000;

        if (markdown.Length <= MaxChunkSize)
        {
            return await TranslateTextAsync(markdown, targetLanguage, markdownOptions, cancellationToken);
        }

        _logger.LogInformation("長文のため分割翻訳を実行します。総文字数: {Length}", markdown.Length);

        // Markdown を論理的なブロック単位で分割
        var chunks = SplitMarkdownIntoChunks(markdown, MaxChunkSize);

        _logger.LogInformation("分割数: {ChunkCount}", chunks.Count);

        var translatedChunks = new List<string>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var startTime = DateTime.UtcNow;

        for (int i = 0; i < chunks.Count; i++)
        {
            _logger.LogInformation("チャンク {Current}/{Total} を翻訳中...", i + 1, chunks.Count);

            var chunkResult = await TranslateTextAsync(chunks[i], targetLanguage, markdownOptions, cancellationToken);

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
            originalText: markdown,
            translatedText: combinedText,
            sourceLanguage: markdownOptions.SourceLanguage ?? "auto",
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
    /// Markdown を論理的なブロック単位で分割します
    /// 見出しや段落の境界で分割し、画像参照は分割しない
    /// </summary>
    private List<string> SplitMarkdownIntoChunks(string markdown, int maxChunkSize)
    {
        var chunks = new List<string>();

        // 見出し（#）で分割
        var sections = System.Text.RegularExpressions.Regex.Split(
            markdown,
            @"(?=^#{1,6}\s)",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var currentChunk = new System.Text.StringBuilder();

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section))
                continue;

            if (currentChunk.Length + section.Length > maxChunkSize)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                // セクション自体が大きすぎる場合はさらに分割
                if (section.Length > maxChunkSize)
                {
                    var paragraphs = section.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var para in paragraphs)
                    {
                        if (currentChunk.Length + para.Length > maxChunkSize)
                        {
                            if (currentChunk.Length > 0)
                            {
                                chunks.Add(currentChunk.ToString().Trim());
                                currentChunk.Clear();
                            }
                        }
                        currentChunk.AppendLine(para);
                        currentChunk.AppendLine();
                    }
                }
                else
                {
                    currentChunk.Append(section);
                }
            }
            else
            {
                currentChunk.Append(section);
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
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
