using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using DocumentFormat.OpenXml.Packaging;
using OpenAI.Chat;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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
    /// 処理を明確に分離：1.画像抽出 → 2.テキスト抽出 → 3.OCR削除 → 4.翻訳 → 5.画像復元
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
            _logger.LogInformation("=== ドキュメント翻訳開始 ===");
            _logger.LogInformation("ファイル名: {FileName}, ターゲット言語: {TargetLanguage}", document.FileName, targetLanguage);

            // ステップ0: ドキュメントの検証
            if (!await ValidateDocumentAsync(document))
            {
                return GptTranslationResult.Failure(
                    "無効なドキュメントです。PDF (.pdf) または Word (.docx) ファイルのみサポートされています。",
                    document.FileName);
            }

            using var stream = document.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            var fileBytes = memoryStream.ToArray();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var documentId = Path.GetFileNameWithoutExtension(document.FileName);
            var fileExtension = Path.GetExtension(document.FileName).ToLowerInvariant();

            // ============================================
            // ステップ1: 画像を抽出し、Blob Storage にアップロード
            // ============================================
            _logger.LogInformation("=== ステップ1: 画像抽出 ===");
            
            List<ExtractedImageInfo> imageInfos;
            if (fileExtension == ".pdf")
            {
                imageInfos = await ExtractImagesFromPdfAsync(fileBytes, documentId, timestamp, cancellationToken);
            }
            else if (fileExtension == ".docx")
            {
                imageInfos = await ExtractImagesFromDocxAsync(fileBytes, documentId, timestamp, cancellationToken);
            }
            else
            {
                imageInfos = new List<ExtractedImageInfo>();
            }

            _logger.LogInformation("画像抽出完了: {Count} 枚", imageInfos.Count);
            for (int i = 0; i < imageInfos.Count; i++)
            {
                _logger.LogInformation("  画像[{Index}]: ページ {Page}", i, imageInfos[i].PageNumber);
            }

            // ============================================
            // ステップ2: Document Intelligence でテキストを抽出
            // ============================================
            _logger.LogInformation("=== ステップ2: テキスト抽出（Document Intelligence） ===");
            
            var base64Content = Convert.ToBase64String(fileBytes);
            using var requestContent = RequestContent.Create(new { base64Source = base64Content });
            
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

            var responseJson = JsonDocument.Parse(operation.Value.ToStream());
            var rootElement = responseJson.RootElement;

            JsonElement analyzeResult;
            if (rootElement.TryGetProperty("analyzeResult", out var analyzeResultElement))
            {
                analyzeResult = analyzeResultElement;
            }
            else
            {
                analyzeResult = rootElement;
            }

            string extractedMarkdown;
            if (analyzeResult.TryGetProperty("content", out var contentElement))
            {
                extractedMarkdown = contentElement.GetString() ?? string.Empty;
            }
            else
            {
                return GptTranslationResult.Failure("ドキュメントの解析結果を取得できませんでした。", document.FileName);
            }

            if (string.IsNullOrWhiteSpace(extractedMarkdown))
            {
                return GptTranslationResult.Failure("ドキュメントからテキストを抽出できませんでした。", document.FileName);
            }

            _logger.LogInformation("テキスト抽出完了: {Length} 文字", extractedMarkdown.Length);

            // ============================================
            // デバッグ: Document Intelligence の抽出結果をファイルに保存
            // ============================================
            _logger.LogInformation("=== Document Intelligence 抽出結果 ===");
            
            // デバッグ用：抽出されたMarkdown全体をファイルに保存
            try
            {
                var debugDir = Path.Combine(Path.GetTempPath(), "gpt-translator-debug");
                Directory.CreateDirectory(debugDir);
                var debugTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var debugFilePath = Path.Combine(debugDir, $"di_output_{debugTimestamp}.md");
                File.WriteAllText(debugFilePath, extractedMarkdown);
                _logger.LogInformation("[DI結果] 抽出Markdownを保存: {Path}", debugFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[DI結果] デバッグファイル保存失敗: {Error}", ex.Message);
            }
            
            // <figure>タグの検出確認
            var figureTagPattern = new System.Text.RegularExpressions.Regex(
                @"<figure[^>]*>[\s\S]*?</figure>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var figureMatches = figureTagPattern.Matches(extractedMarkdown);
            _logger.LogInformation("[DI結果] <figure>タグ検出数: {Count}", figureMatches.Count);
            
            for (int fIdx = 0; fIdx < figureMatches.Count; fIdx++)
            {
                var fMatch = figureMatches[fIdx];
                _logger.LogInformation("[DI結果] <figure>[{Index}]: 位置={Start}, 長さ={Length}", fIdx, fMatch.Index, fMatch.Length);
                _logger.LogInformation("[DI結果] <figure>[{Index}] 内容:\n---\n{Content}\n---", fIdx, 
                    fMatch.Value.Length > 500 ? fMatch.Value.Substring(0, 500) + "..." : fMatch.Value);
            }
            
            _logger.LogInformation("[DI結果] 抽出Markdown（最初の3000文字）:\n{Text}", 
                extractedMarkdown.Length > 3000 ? extractedMarkdown.Substring(0, 3000) + "\n... (省略)" : extractedMarkdown);
            
            // figures プロパティの詳細をログ出力
            if (analyzeResult.TryGetProperty("figures", out var figuresDebugElement) && figuresDebugElement.ValueKind == JsonValueKind.Array)
            {
                var figureCount = 0;
                foreach (var fig in figuresDebugElement.EnumerateArray())
                {
                    _logger.LogInformation("[DI結果] === 図 {Index} ===", figureCount);
                    
                    // boundingRegions
                    if (fig.TryGetProperty("boundingRegions", out var regions) && regions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var region in regions.EnumerateArray())
                        {
                            if (region.TryGetProperty("pageNumber", out var pageNum))
                            {
                                _logger.LogInformation("[DI結果]   ページ: {Page}", pageNum.GetInt32());
                            }
                        }
                    }
                    
                    // spans
                    if (fig.TryGetProperty("spans", out var spans) && spans.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var span in spans.EnumerateArray())
                        {
                            if (span.TryGetProperty("offset", out var offset) && span.TryGetProperty("length", out var length))
                            {
                                var off = offset.GetInt32();
                                var len = length.GetInt32();
                                _logger.LogInformation("[DI結果]   スパン: offset={Offset}, length={Length}", off, len);
                                
                                // 実際のテキストを表示
                                if (off >= 0 && off + len <= extractedMarkdown.Length)
                                {
                                    var spanText = extractedMarkdown.Substring(off, len);
                                    _logger.LogInformation("[DI結果]   スパンテキスト:\n---\n{Text}\n---", 
                                        spanText.Length > 500 ? spanText.Substring(0, 500) + "..." : spanText);
                                }
                            }
                        }
                    }
                    
                    figureCount++;
                }
                _logger.LogInformation("[DI結果] 図の総数: {Count}", figureCount);
            }
            else
            {
                _logger.LogWarning("[DI結果] figures プロパティが見つかりません");
            }

            // ============================================
            // ステップ3: 図のスパン情報を取得
            // ============================================
            _logger.LogInformation("=== ステップ3: 図のスパン情報取得 ===");
            
            var figureSpans = ExtractFigureSpans(analyzeResult, extractedMarkdown);
            _logger.LogInformation("図のスパン数: {Count}", figureSpans.Count);

            // ============================================
            // ステップ4: 図のOCRテキストを削除し、プレースホルダーに置換
            // ============================================
            _logger.LogInformation("=== ステップ4: OCRテキスト削除＆プレースホルダー挿入 ===");
            
            var (markdownWithPlaceholders, placeholderToImageMap) = ReplaceFigureOcrWithPlaceholders(
                extractedMarkdown, 
                figureSpans, 
                imageInfos);
            
            _logger.LogInformation("プレースホルダー挿入完了: {Count} 個", placeholderToImageMap.Count);
            _logger.LogInformation("処理前Markdown長: {Before}, 処理後: {After}", 
                extractedMarkdown.Length, markdownWithPlaceholders.Length);

            // ============================================
            // ステップ5: GPT-4o で翻訳
            // ============================================
            _logger.LogInformation("=== ステップ5: 翻訳（GPT-4o） ===");
            
            var translationResult = await TranslateMarkdownAsync(
                markdownWithPlaceholders,
                targetLanguage,
                options,
                cancellationToken);

            if (!translationResult.IsSuccess)
            {
                return translationResult;
            }

            _logger.LogInformation("翻訳完了");

            // ============================================
            // ステップ6: プレースホルダーを画像URLに置換
            // ============================================
            _logger.LogInformation("=== ステップ6: プレースホルダーを画像に置換 ===");
            
            var translatedTextWithImages = ReplacePlaceholdersWithImages(
                translationResult.TranslatedText, 
                placeholderToImageMap);
            
            var finalImageCount = System.Text.RegularExpressions.Regex.Matches(
                translatedTextWithImages, @"!\[.*?\]\([^)]+\)").Count;
            _logger.LogInformation("最終画像数: {Count}", finalImageCount);

            // ============================================
            // ステップ7: 結果を保存
            // ============================================
            _logger.LogInformation("=== ステップ7: 結果保存 ===");
            
            var blobName = $"{documentId}_{targetLanguage}_{timestamp}.md";
            var blobUrl = await SaveTranslationResultAsync(blobName, translatedTextWithImages, cancellationToken);

            var completedTime = DateTime.UtcNow;

            _logger.LogInformation("=== 完了 === 処理時間: {Duration}秒", (completedTime - startTime).TotalSeconds);

            return GptTranslationResult.Success(
                originalFileName: document.FileName,
                originalText: markdownWithPlaceholders,
                translatedText: translatedTextWithImages,
                sourceLanguage: options.SourceLanguage ?? "auto",
                targetLanguage: targetLanguage,
                blobName: blobName,
                blobUrl: blobUrl,
                imageUrls: imageInfos.Select(i => i.Url).ToList(),
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
    /// ステップ3: Document Intelligence のレスポンスから図のスパン情報を抽出
    /// </summary>
    private List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)> ExtractFigureSpans(
        JsonElement analyzeResult,
        string markdown)
    {
        var figureSpans = new List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)>();

        if (!analyzeResult.TryGetProperty("figures", out var figuresElement) || 
            figuresElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("[ステップ3] figures プロパティが見つかりません");
            return figureSpans;
        }

        // 各図の座標情報を収集
        var figureBounds = new List<(int FigureIndex, int PageNumber, double MinY, double MaxY)>();

        var figureIndex = 0;
        foreach (var figure in figuresElement.EnumerateArray())
        {
            // ページ番号と座標を取得
            int pageNumber = 0;
            double minY = double.MaxValue, maxY = double.MinValue;
            
            if (figure.TryGetProperty("boundingRegions", out var regionsElement) && 
                regionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var region in regionsElement.EnumerateArray())
                {
                    if (region.TryGetProperty("pageNumber", out var pageNumElement))
                    {
                        pageNumber = pageNumElement.GetInt32();
                    }
                    if (region.TryGetProperty("polygon", out var polygonElement) && 
                        polygonElement.ValueKind == JsonValueKind.Array)
                    {
                        var coords = polygonElement.EnumerateArray().Select(p => p.GetDouble()).ToArray();
                        // polygon は [x1,y1, x2,y2, x3,y3, x4,y4] 形式
                        for (int i = 1; i < coords.Length; i += 2)
                        {
                            minY = Math.Min(minY, coords[i]);
                            maxY = Math.Max(maxY, coords[i]);
                        }
                    }
                }
            }

            if (pageNumber > 0 && minY < double.MaxValue)
            {
                figureBounds.Add((figureIndex, pageNumber, minY, maxY));
                _logger.LogInformation("[ステップ3] 図[{Index}] 座標: ページ={Page}, Y範囲={MinY:F2}-{MaxY:F2}", 
                    figureIndex, pageNumber, minY, maxY);
            }

            // 図自体のスパン情報を取得
            if (figure.TryGetProperty("spans", out var spansElement) && 
                spansElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var span in spansElement.EnumerateArray())
                {
                    if (span.TryGetProperty("offset", out var offsetElement) && 
                        span.TryGetProperty("length", out var lengthElement))
                    {
                        var offset = offsetElement.GetInt32();
                        var length = lengthElement.GetInt32();

                        var spanText = "";
                        if (offset >= 0 && offset + length <= markdown.Length)
                        {
                            spanText = markdown.Substring(offset, length);
                        }

                        figureSpans.Add((figureIndex, pageNumber, offset, length, spanText));
                        
                        _logger.LogInformation("[ステップ3] 図[{Index}] スパン: offset={Offset}, length={Length}", 
                            figureIndex, offset, length);
                    }
                }
            }
            figureIndex++;
        }

        // 図の座標範囲と重なるコンテンツのスパンを追加
        // paragraphs を検索
        if (analyzeResult.TryGetProperty("paragraphs", out var paragraphsElement) && 
            paragraphsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var para in paragraphsElement.EnumerateArray())
            {
                AddOverlappingSpans(para, figureBounds, figureSpans, markdown, "段落", _logger);
            }
        }

        // tables を検索
        if (analyzeResult.TryGetProperty("tables", out var tablesElement) && 
            tablesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var table in tablesElement.EnumerateArray())
            {
                AddOverlappingSpans(table, figureBounds, figureSpans, markdown, "テーブル", _logger);
            }
        }

        // スパンをオフセット順にソートして重複を除去
        var uniqueSpans = figureSpans
            .GroupBy(s => s.Offset)
            .Select(g => g.OrderByDescending(s => s.Length).First())
            .OrderBy(s => s.Offset)
            .ToList();

        _logger.LogInformation("[ステップ3] 総スパン数: {Count} (重複除去後)", uniqueSpans.Count);
        foreach (var s in uniqueSpans)
        {
            _logger.LogInformation("[ステップ3]   スパン: offset={Offset}, length={Length}, ページ={Page}", 
                s.Offset, s.Length, s.PageNumber);
        }

        return uniqueSpans;
    }

    /// <summary>
    /// 図の座標範囲と重なるコンテンツのスパンを追加
    /// </summary>
    private static void AddOverlappingSpans(
        JsonElement element,
        List<(int FigureIndex, int PageNumber, double MinY, double MaxY)> figureBounds,
        List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)> figureSpans,
        string markdown,
        string elementType,
        ILogger logger)
    {
        int elementPage = 0;
        double elementMinY = double.MaxValue, elementMaxY = double.MinValue;

        if (element.TryGetProperty("boundingRegions", out var regions) && 
            regions.ValueKind == JsonValueKind.Array)
        {
            foreach (var region in regions.EnumerateArray())
            {
                if (region.TryGetProperty("pageNumber", out var pageNumElement))
                {
                    elementPage = pageNumElement.GetInt32();
                }
                if (region.TryGetProperty("polygon", out var polygonElement) && 
                    polygonElement.ValueKind == JsonValueKind.Array)
                {
                    var coords = polygonElement.EnumerateArray().Select(p => p.GetDouble()).ToArray();
                    for (int i = 1; i < coords.Length; i += 2)
                    {
                        elementMinY = Math.Min(elementMinY, coords[i]);
                        elementMaxY = Math.Max(elementMaxY, coords[i]);
                    }
                }
            }
        }

        // 図の座標範囲と重なるか確認
        foreach (var fig in figureBounds)
        {
            if (fig.PageNumber != elementPage) continue;

            // Y座標が重なるか確認（マージン10%を許容）
            var figHeight = fig.MaxY - fig.MinY;
            var margin = figHeight * 0.1;
            var overlap = elementMinY <= fig.MaxY + margin && elementMaxY >= fig.MinY - margin;

            if (overlap)
            {
                // このコンテンツのスパンを追加
                if (element.TryGetProperty("spans", out var spansElement) && 
                    spansElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var span in spansElement.EnumerateArray())
                    {
                        if (span.TryGetProperty("offset", out var offsetElement) && 
                            span.TryGetProperty("length", out var lengthElement))
                        {
                            var offset = offsetElement.GetInt32();
                            var length = lengthElement.GetInt32();

                            // 既存のスパンと重複していないか確認
                            var isDuplicate = figureSpans.Any(s => 
                                s.Offset == offset && s.Length == length);
                            
                            if (!isDuplicate)
                            {
                                var spanText = "";
                                if (offset >= 0 && offset + length <= markdown.Length)
                                {
                                    spanText = markdown.Substring(offset, length);
                                }

                                figureSpans.Add((fig.FigureIndex, elementPage, offset, length, spanText));
                                
                                logger.LogInformation("[ステップ3] 図[{FigIndex}]と重なる{Type}: offset={Offset}, length={Length}", 
                                    fig.FigureIndex, elementType, offset, length);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// ステップ4: 図のOCRテキストをプレースホルダーに置換
    /// 重なり合うスパンをマージし、各図に1つのプレースホルダーを割り当てる
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderToImage) ReplaceFigureOcrWithPlaceholders(
        string markdown,
        List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)> figureSpans,
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderToImage = new Dictionary<string, string>();

        _logger.LogInformation("[ステップ4] ========== 処理開始 ==========");
        _logger.LogInformation("[ステップ4] Markdown長: {Length}, スパン数: {SpanCount}, 画像数: {ImageCount}", 
            markdown?.Length ?? 0, figureSpans.Count, imageInfos.Count);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return (markdown ?? "", placeholderToImage);
        }

        if (figureSpans.Count == 0)
        {
            _logger.LogWarning("[ステップ4] スパン情報がありません");
            return (markdown, placeholderToImage);
        }

        // スパンを図インデックスでグループ化し、各図のスパンをマージ
        var mergedSpansByFigure = new Dictionary<int, (int Offset, int Length)>();
        
        foreach (var span in figureSpans)
        {
            var figIdx = span.FigureIndex;
            if (figIdx < 0) figIdx = -figIdx - 1; // 負のインデックスを正に変換
            
            if (!mergedSpansByFigure.ContainsKey(figIdx))
            {
                mergedSpansByFigure[figIdx] = (span.Offset, span.Length);
            }
            else
            {
                // 既存のスパンと現在のスパンをマージ（最小offsetから最大endまで）
                var existing = mergedSpansByFigure[figIdx];
                var existingEnd = existing.Offset + existing.Length;
                var newEnd = span.Offset + span.Length;
                
                var mergedOffset = Math.Min(existing.Offset, span.Offset);
                var mergedEnd = Math.Max(existingEnd, newEnd);
                mergedSpansByFigure[figIdx] = (mergedOffset, mergedEnd - mergedOffset);
            }
        }

        _logger.LogInformation("[ステップ4] マージ後の図スパン数: {Count}", mergedSpansByFigure.Count);
        foreach (var kvp in mergedSpansByFigure.OrderBy(k => k.Key))
        {
            _logger.LogInformation("[ステップ4]   図[{Index}]: offset={Offset}, length={Length}", 
                kvp.Key, kvp.Value.Offset, kvp.Value.Length);
        }

        // さらに、重なり合うスパンをマージ（図間で重なる場合）
        var allSpans = mergedSpansByFigure
            .Select(kvp => (FigureIndex: kvp.Key, Offset: kvp.Value.Offset, Length: kvp.Value.Length))
            .OrderBy(s => s.Offset)
            .ToList();

        var finalSpans = new List<(int FigureIndex, int Offset, int Length)>();
        foreach (var span in allSpans)
        {
            if (finalSpans.Count == 0)
            {
                finalSpans.Add(span);
            }
            else
            {
                var last = finalSpans[finalSpans.Count - 1];
                var lastEnd = last.Offset + last.Length;
                
                // 重なり合う場合はマージ
                if (span.Offset <= lastEnd)
                {
                    var newEnd = Math.Max(lastEnd, span.Offset + span.Length);
                    finalSpans[finalSpans.Count - 1] = (last.FigureIndex, last.Offset, newEnd - last.Offset);
                    _logger.LogInformation("[ステップ4] スパンをマージ: 図[{Index1}]と図[{Index2}]", last.FigureIndex, span.FigureIndex);
                }
                else
                {
                    finalSpans.Add(span);
                }
            }
        }

        _logger.LogInformation("[ステップ4] 最終スパン数: {Count}", finalSpans.Count);

        // 後ろから置換（オフセットのずれを防ぐ）
        var result = markdown;
        for (int i = finalSpans.Count - 1; i >= 0; i--)
        {
            var span = finalSpans[i];
            var placeholderNum = i + 1;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderNum:D3}]]";

            // オフセットの検証
            if (span.Offset < 0 || span.Offset >= result.Length)
            {
                _logger.LogWarning("[ステップ4] 無効なオフセット: {Offset}", span.Offset);
                continue;
            }

            var endPos = Math.Min(span.Offset + span.Length, result.Length);

            _logger.LogInformation("[ステップ4] 置換[{Index}]: offset={Offset}-{End} -> {Placeholder}", 
                i, span.Offset, endPos, placeholder);

            // 画像を割り当て
            if (i < imageInfos.Count)
            {
                var img = imageInfos[i];
                placeholderToImage[placeholder] = $"![{img.Description}]({img.Url})";
                _logger.LogInformation("[ステップ4]   画像割り当て: ページ {Page}", img.PageNumber);
            }
            else
            {
                placeholderToImage[placeholder] = "";
                _logger.LogWarning("[ステップ4]   対応する画像がありません");
            }

            // 文字列を切り取り・置換
            var before = result.Substring(0, span.Offset);
            var after = result.Substring(endPos);
            result = before + $"\n\n{placeholder}\n\n" + after;
        }

        // 未割り当ての画像を末尾に追加
        if (finalSpans.Count < imageInfos.Count)
        {
            _logger.LogInformation("[ステップ4] 未割り当て画像を末尾に追加: {Count} 件", imageInfos.Count - finalSpans.Count);
            var sb = new System.Text.StringBuilder(result);

            for (int i = finalSpans.Count; i < imageInfos.Count; i++)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{(i + 1):D3}]]";
                var img = imageInfos[i];
                placeholderToImage[placeholder] = $"![{img.Description}]({img.Url})";
                sb.AppendLine();
                sb.AppendLine(placeholder);
            }

            result = sb.ToString();
        }

        // 連続する空行を整理
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");

        // デバッグ: 処理後のMarkdownをファイルに保存
        try
        {
            var debugDir = Path.Combine(Path.GetTempPath(), "gpt-translator-debug");
            var debugTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.WriteAllText(Path.Combine(debugDir, $"processed_{debugTimestamp}.md"), result);
        }
        catch { }

        _logger.LogInformation("[ステップ4] ========== 完了 ==========");
        _logger.LogInformation("[ステップ4] プレースホルダー数: {Count}", placeholderToImage.Count);

        return (result, placeholderToImage);
    }

    /// <summary>
    /// ステップ4（旧）: 図のOCRテキストをプレースホルダーに置換
    /// 元のテキストを確実に削除し、プレースホルダーを挿入する
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderToImage) ReplaceFigureOcrWithPlaceholders_Old(
        string markdown,
        List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)> figureSpans,
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderToImage = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(markdown) || figureSpans.Count == 0)
        {
            _logger.LogWarning("[ステップ4] マークダウンが空、または図スパンがありません");
            // 図がない場合でも画像があれば末尾に追加
            if (imageInfos.Count > 0)
            {
                var sb = new System.Text.StringBuilder(markdown);
                sb.AppendLine();
                for (int i = 0; i < imageInfos.Count; i++)
                {
                    var ph = $"[[IMG_PLACEHOLDER_{(i + 1):D3}]]";
                    var imgMd = $"![{imageInfos[i].Description}]({imageInfos[i].Url})";
                    placeholderToImage[ph] = imgMd;
                    sb.AppendLine();
                    sb.AppendLine(ph);
                }
                return (sb.ToString(), placeholderToImage);
            }
            return (markdown, placeholderToImage);
        }

        _logger.LogInformation("[ステップ4] 処理開始: Markdown長={Length}, 図数={FigureCount}, 画像数={ImageCount}", 
            markdown.Length, figureSpans.Count, imageInfos.Count);
        
        // 元のMarkdownを文字配列として保持
        var chars = markdown.ToCharArray();
        var deleted = new bool[chars.Length]; // 削除マーク

        // オフセット昇順でソート
        var sortedSpans = figureSpans.OrderBy(s => s.Offset).ToList();

        // 各スパンの範囲を削除マーク
        foreach (var span in sortedSpans)
        {
            if (span.Offset < 0 || span.Offset >= chars.Length)
            {
                _logger.LogWarning("[ステップ4] 無効なオフセット: {Offset}", span.Offset);
                continue;
            }

            var endPos = Math.Min(span.Offset + span.Length, chars.Length);
            
            _logger.LogInformation("[ステップ4] 削除範囲: [{Start}..{End}]", span.Offset, endPos);
            
            for (int i = span.Offset; i < endPos; i++)
            {
                deleted[i] = true;
            }
        }

        // 削除されなかった部分と、各スパン位置にプレースホルダーを挿入して再構築
        var result = new System.Text.StringBuilder();
        var lastWasDeleted = false;
        var spanIndex = 0;
        var processedSpanOffsets = new HashSet<int>();

        for (int i = 0; i < chars.Length; i++)
        {
            if (deleted[i])
            {
                // 削除位置の開始時にプレースホルダーを挿入
                if (!lastWasDeleted)
                {
                    // このオフセットに対応するスパンを見つける
                    var matchingSpan = sortedSpans.FirstOrDefault(s => s.Offset == i);
                    if (matchingSpan.Length > 0 && !processedSpanOffsets.Contains(matchingSpan.Offset))
                    {
                        spanIndex++;
                        var placeholder = $"[[IMG_PLACEHOLDER_{spanIndex:D3}]]";
                        result.Append($"\n\n{placeholder}\n\n");
                        processedSpanOffsets.Add(matchingSpan.Offset);
                        
                        // 画像を割り当て
                        var imgIndex = spanIndex - 1;
                        if (imgIndex < imageInfos.Count)
                        {
                            var img = imageInfos[imgIndex];
                            var imageMarkdown = $"![{img.Description}]({img.Url})";
                            placeholderToImage[placeholder] = imageMarkdown;
                            _logger.LogInformation("[ステップ4] {Placeholder} -> 画像[{Index}] (ページ {Page})", 
                                placeholder, imgIndex, img.PageNumber);
                        }
                        else
                        {
                            placeholderToImage[placeholder] = "";
                            _logger.LogWarning("[ステップ4] {Placeholder} に対応する画像がありません", placeholder);
                        }
                    }
                }
                lastWasDeleted = true;
            }
            else
            {
                result.Append(chars[i]);
                lastWasDeleted = false;
            }
        }

        // 未使用の画像を末尾に追加
        var usedImageCount = spanIndex;
        if (usedImageCount < imageInfos.Count)
        {
            _logger.LogInformation("[ステップ4] 未使用画像を末尾に追加: {Count} 件", imageInfos.Count - usedImageCount);
            result.AppendLine();
            
            for (int i = usedImageCount; i < imageInfos.Count; i++)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{(i + 1):D3}]]";
                var img = imageInfos[i];
                var imageMarkdown = $"![{img.Description}]({img.Url})";
                placeholderToImage[placeholder] = imageMarkdown;
                result.AppendLine();
                result.AppendLine(placeholder);
                _logger.LogInformation("[ステップ4] 未使用: {Placeholder} -> 画像[{Index}]", placeholder, i);
            }
        }

        var resultString = result.ToString();
        
        // 連続する空行を整理
        resultString = System.Text.RegularExpressions.Regex.Replace(resultString, @"\n{3,}", "\n\n");

        // 検証: 削除したテキストが結果に残っていないか
        _logger.LogInformation("[ステップ4] 検証開始...");
        foreach (var span in sortedSpans)
        {
            if (!string.IsNullOrWhiteSpace(span.Content) && span.Content.Length > 20)
            {
                // 最初の20文字で検証（プレースホルダーを除外）
                var checkText = span.Content.Substring(0, Math.Min(20, span.Content.Length));
                if (!checkText.Contains("[[IMG_PLACEHOLDER") && resultString.Contains(checkText))
                {
                    _logger.LogError("[ステップ4] ⚠️ 図[{Index}]のOCRテキストが残っています: '{Text}'", 
                        span.FigureIndex, checkText);
                }
                else
                {
                    _logger.LogInformation("[ステップ4] ✓ 図[{Index}]のOCRテキストは削除されました", span.FigureIndex);
                }
            }
        }

        _logger.LogInformation("[ステップ4] 完了: プレースホルダー数={Count}, 処理前長={Before}, 処理後長={After}", 
            placeholderToImage.Count, markdown.Length, resultString.Length);

        return (resultString, placeholderToImage);
    }

    /// <summary>
    /// スパン情報を使って図のOCRテキストを確実に削除し、プレースホルダーを挿入します
    /// ページ番号を使って画像との正確な対応付けを行います
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderMapping) RemoveFigureOcrBySpans(
        string markdown,
        List<ExtractedImageInfo> imageInfos,
        List<(int FigureIndex, int PageNumber, int Offset, int Length, string Content)> figureInfos)
    {
        var placeholderMapping = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return (markdown, placeholderMapping);
        }

        _logger.LogInformation("[スパン削除] 開始: Markdown長={Length}, 画像数={ImageCount}, 図数={FigureCount}", 
            markdown.Length, imageInfos.Count, figureInfos.Count);


        // 図をオフセット昇順でソート
        var sortedFigures = figureInfos
            .OrderBy(f => f.Offset)
            .ToList();

        // 画像をページ番号でグループ化（同じページに複数の画像がある場合に対応）
        var imagesByPage = imageInfos
            .GroupBy(i => i.PageNumber)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        _logger.LogInformation("[スパン削除] ページ別画像: {Pages}", 
            string.Join(", ", imagesByPage.Select(kv => $"ページ{kv.Key}={kv.Value.Count}枚")));

        // 結果を構築
        var resultBuilder = new System.Text.StringBuilder();
        var lastEndPosition = 0;
        var usedImageIndices = new HashSet<int>();
        var placeholderIndex = 0;

        // ページごとに使用済み画像インデックスを追跡
        var pageImageIndex = new Dictionary<int, int>();

        foreach (var figure in sortedFigures)
        {
            // オフセットの検証
            if (figure.Offset < 0 || figure.Offset > markdown.Length)
            {
                _logger.LogWarning("[スパン削除] 無効なオフセット: {Offset}", figure.Offset);
                continue;
            }

            // スパン開始位置が前のスパン終了位置より前の場合はスキップ
            if (figure.Offset < lastEndPosition)
            {
                _logger.LogWarning("[スパン削除] スパンが重複: Offset={Offset} < LastEnd={LastEnd}", figure.Offset, lastEndPosition);
                continue;
            }

            var endPos = Math.Min(figure.Offset + figure.Length, markdown.Length);
            
            // スパンより前のテキストを追加
            if (figure.Offset > lastEndPosition)
            {
                var beforeText = markdown.Substring(lastEndPosition, figure.Offset - lastEndPosition);
                resultBuilder.Append(beforeText);
            }

            // プレースホルダーを作成
            placeholderIndex++;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
            
            // 削除されるテキストをログ出力
            var removedText = markdown.Substring(figure.Offset, endPos - figure.Offset);
            _logger.LogInformation("[スパン削除] 図{FigureIndex}: offset={Offset}, length={Length} を削除", 
                figure.FigureIndex, figure.Offset, figure.Length);
            _logger.LogInformation("[スパン削除] 削除テキスト: '{Text}'", 
                removedText.Length > 100 ? removedText.Substring(0, 100).Replace("\n", "\\n") + "..." : removedText.Replace("\n", "\\n"));

            // プレースホルダーを追加
            resultBuilder.Append($"\n\n{placeholder}\n\n");

            // ページ番号で画像を割り当て
            ExtractedImageInfo? matchedImage = null;
            if (figure.PageNumber > 0 && imagesByPage.TryGetValue(figure.PageNumber, out var pageImages))
            {
                // そのページで次に使用可能な画像を取得
                if (!pageImageIndex.ContainsKey(figure.PageNumber))
                {
                    pageImageIndex[figure.PageNumber] = 0;
                }
                
                var imgIdx = pageImageIndex[figure.PageNumber];
                if (imgIdx < pageImages.Count)
                {
                    matchedImage = pageImages[imgIdx];
                    pageImageIndex[figure.PageNumber]++;
                    
                    var globalIndex = imageInfos.IndexOf(matchedImage);
                    if (globalIndex >= 0)
                    {
                        usedImageIndices.Add(globalIndex);
                    }
                }
            }

            if (matchedImage != null)
            {
                var imageMarkdown = $"![{matchedImage.Description}]({matchedImage.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                _logger.LogInformation("[スパン削除] {Placeholder} -> 画像 (ページ {Page}) ✓", 
                    placeholder, matchedImage.PageNumber);
            }
            else
            {
                // ページ番号でマッチしない場合は順番で割り当て
                var availableImage = imageInfos
                    .Select((img, idx) => new { img, idx })
                    .FirstOrDefault(x => !usedImageIndices.Contains(x.idx));
                    
                if (availableImage != null)
                {
                    var imageMarkdown = $"![{availableImage.img.Description}]({availableImage.img.Url})";
                    placeholderMapping[placeholder] = imageMarkdown;
                    usedImageIndices.Add(availableImage.idx);
                    _logger.LogInformation("[スパン削除] {Placeholder} -> 画像 (順番割り当て、ページ {Page})", 
                        placeholder, availableImage.img.PageNumber);
                }
                else
                {
                    placeholderMapping[placeholder] = "";
                    _logger.LogWarning("[スパン削除] {Placeholder} に対応する画像がありません", placeholder);
                }
            }

            lastEndPosition = endPos;
        }

        // 残りのテキストを追加
        if (lastEndPosition < markdown.Length)
        {
            resultBuilder.Append(markdown.Substring(lastEndPosition));
        }

        var result = resultBuilder.ToString();

        // 未使用の画像を末尾に追加
        var unusedImages = imageInfos
            .Select((img, idx) => new { img, idx })
            .Where(x => !usedImageIndices.Contains(x.idx))
            .ToList();

        if (unusedImages.Count > 0)
        {
            _logger.LogInformation("[スパン削除] 未使用画像を末尾に追加: {Count} 件", unusedImages.Count);
            var sb = new System.Text.StringBuilder(result);
            sb.AppendLine();
            
            foreach (var item in unusedImages)
            {
                placeholderIndex++;
                var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
                var imageMarkdown = $"![{item.img.Description}]({item.img.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                sb.AppendLine();
                sb.AppendLine(placeholder);
                _logger.LogInformation("[スパン削除] 未使用画像 -> {Placeholder} (ページ {Page})", placeholder, item.img.PageNumber);
            }
            
            result = sb.ToString();
        }

        // 連続する空行を整理
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");

        _logger.LogInformation("[スパン削除] 完了: プレースホルダー数={Count}, 元のMarkdown長={OrigLen}, 処理後長={NewLen}", 
            placeholderMapping.Count, markdown.Length, result.Length);

        // 検証: 削除したテキストが結果に残っていないか確認
        foreach (var figure in sortedFigures)
        {
            if (!string.IsNullOrWhiteSpace(figure.Content) && figure.Content.Length > 30)
            {
                // 最初の30文字で検証
                var checkText = figure.Content.Substring(0, Math.Min(30, figure.Content.Length));
                var normalizedCheck = System.Text.RegularExpressions.Regex.Replace(checkText, @"\s+", " ").Trim();
                var normalizedResult = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
                
                if (normalizedResult.Contains(normalizedCheck))
                {
                    _logger.LogError("[スパン削除] ⚠️ 図{FigureIndex}のOCRテキストが削除されていません: '{Text}'", 
                        figure.FigureIndex, normalizedCheck);
                }
                else
                {
                    _logger.LogInformation("[スパン削除] ✓ 図{FigureIndex}のOCRテキストは正常に削除されました", figure.FigureIndex);
                }
            }
        }

        return (result, placeholderMapping);
    }

    /// <summary>
    /// Document Intelligence が OCR で抽出した図のテキストを削除し、代わりに画像プレースホルダーを挿入します
    /// 画像内テキストは翻訳せず、実際の画像として表示します
    /// 正規表現で図関連コンテンツを直接検出・削除します
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderMapping) RemoveFigureOcrAndInsertPlaceholders(
        string markdown,
        List<ExtractedImageInfo> imageInfos,
        List<(int Offset, int Length, int FigureIndex, string Content)> figureSpans)
    {
        var placeholderMapping = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return (markdown, placeholderMapping);
        }

        _logger.LogInformation("[図削除] 開始: Markdown長={Length}, 画像数={ImageCount}", 
            markdown.Length, imageInfos.Count);

        // 元のMarkdownをログに出力（デバッグ用）
        _logger.LogDebug("[図削除] 元のMarkdown:\n{Markdown}", markdown);

        var result = markdown;
        var placeholderIndex = 0;
        var usedImageIndices = new HashSet<int>();

        // パターン1: <figure>...</figure> セクション全体を削除
        // このパターンは <figure> タグから </figure> までのすべてのコンテンツをキャプチャ
        var figureTagPattern = new System.Text.RegularExpressions.Regex(
            @"<figure[^>]*>[\s\S]*?</figure>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        
        var figureTagMatches = figureTagPattern.Matches(result);
        _logger.LogInformation("[図削除] <figure>タグセクション検出数: {Count}", figureTagMatches.Count);
        
        foreach (System.Text.RegularExpressions.Match match in figureTagMatches)
        {
            _logger.LogInformation("[図削除] <figure>セクション発見: 位置={Index}, 長さ={Length}, 内容='{Content}'", 
                match.Index, match.Length, 
                match.Value.Length > 100 ? match.Value.Substring(0, 100).Replace("\n", "\\n") + "..." : match.Value.Replace("\n", "\\n"));
        }
        
        result = figureTagPattern.Replace(result, match =>
        {
            placeholderIndex++;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
            
            // 対応する画像を割り当て
            var imgIndex = placeholderIndex - 1;
            if (imgIndex < imageInfos.Count && !usedImageIndices.Contains(imgIndex))
            {
                var imageInfo = imageInfos[imgIndex];
                var imageMarkdown = $"![{imageInfo.Description}]({imageInfo.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                usedImageIndices.Add(imgIndex);
                _logger.LogInformation("[図削除] <figure> -> {Placeholder} (画像: ページ {Page})", placeholder, imageInfo.PageNumber);
            }
            else
            {
                placeholderMapping[placeholder] = "";
                _logger.LogWarning("[図削除] <figure> -> {Placeholder} (対応画像なし)", placeholder);
            }
            
            return $"\n\n{placeholder}\n\n";
        });

        // パターン2: ![...](figures/N) 参照を削除（<figure>タグなしで存在する場合）
        var figureRefPattern = new System.Text.RegularExpressions.Regex(
            @"!\[[^\]]*\]\(figures/\d+\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        var figureRefMatches = figureRefPattern.Matches(result);
        _logger.LogInformation("[図削除] ![](figures/N)パターン検出数: {Count}", figureRefMatches.Count);
        
        foreach (System.Text.RegularExpressions.Match match in figureRefMatches)
        {
            _logger.LogInformation("[図削除] 図参照発見: '{Content}'", match.Value);
        }
        
        result = figureRefPattern.Replace(result, match =>
        {
            placeholderIndex++;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
            
            // 対応する画像を割り当て
            var imgIndex = placeholderIndex - 1;
            if (imgIndex < imageInfos.Count && !usedImageIndices.Contains(imgIndex))
            {
                var imageInfo = imageInfos[imgIndex];
                var imageMarkdown = $"![{imageInfo.Description}]({imageInfo.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                usedImageIndices.Add(imgIndex);
                _logger.LogInformation("[図削除] figures参照 -> {Placeholder} (画像: ページ {Page})", placeholder, imageInfo.PageNumber);
            }
            else
            {
                placeholderMapping[placeholder] = "";
            }
            
            return $"\n\n{placeholder}\n\n";
        });

        // パターン3: 図のキャプション行を削除（"Figure N:" または "図 N:" で始まる行）
        var captionPattern = new System.Text.RegularExpressions.Regex(
            @"^.*(?:Figure|図)\s*\d+[:\s].*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        
        var captionMatches = captionPattern.Matches(result);
        _logger.LogInformation("[図削除] キャプション行検出数: {Count}", captionMatches.Count);
        
        result = captionPattern.Replace(result, "");

        // 未使用の画像を末尾に追加
        var unusedImages = imageInfos
            .Select((img, idx) => new { img, idx })
            .Where(x => !usedImageIndices.Contains(x.idx))
            .ToList();

        if (unusedImages.Count > 0)
        {
            _logger.LogInformation("[図削除] 未使用画像を末尾に追加: {Count} 件", unusedImages.Count);
            var sb = new System.Text.StringBuilder(result);
            sb.AppendLine();
            sb.AppendLine();
            
            foreach (var item in unusedImages)
            {
                placeholderIndex++;
                var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
                var imageMarkdown = $"![{item.img.Description}]({item.img.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                sb.AppendLine(placeholder);
                sb.AppendLine();
                _logger.LogInformation("[図削除] 未使用画像 -> {Placeholder} (画像: ページ {Page})", placeholder, item.img.PageNumber);
            }
            
            result = sb.ToString();
        }

        // 連続する空行を整理
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");

        _logger.LogInformation("[図削除] 完了: プレースホルダー数={Count}, 使用画像数={UsedCount}", 
            placeholderMapping.Count, usedImageIndices.Count);
        
        // 処理後のMarkdownをログに出力（デバッグ用）
        _logger.LogDebug("[図削除] 処理後のMarkdown:\n{Markdown}", result);

        return (result, placeholderMapping);
    }

    /// <summary>
    /// スパン情報（offset, length）を使って図のOCRテキストをプレースホルダーに置換
    /// 置換後に元のテキストが確実に削除されたことを検証します
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderMapping) ReplaceUsingSpans(
        string markdown,
        List<ExtractedImageInfo> imageInfos,
        List<(int Offset, int Length, int FigureIndex, string Content)> figureSpans)
    {
        var placeholderMapping = new Dictionary<string, string>();
        
        // スパンをオフセット昇順でソート
        var sortedSpansAsc = figureSpans
            .OrderBy(s => s.Offset)
            .ToList();
        
        _logger.LogInformation("[スパン置換] {Count} 個のスパンを処理します", sortedSpansAsc.Count);
        
        // 各スパンの詳細をログ出力
        for (int i = 0; i < sortedSpansAsc.Count; i++)
        {
            var span = sortedSpansAsc[i];
            _logger.LogInformation("[スパン置換] スパン[{Index}]: FigureIndex={FigureIndex}, Offset={Offset}, Length={Length}", 
                i, span.FigureIndex, span.Offset, span.Length);
        }
        
        // 画像とスパンのマッピングを事前に作成（昇順で対応付け）
        // スパンのインデックス順に画像を割り当てる
        var spanIndexToImageMap = new Dictionary<int, int>(); // spanIndex -> imageIndex
        for (int i = 0; i < sortedSpansAsc.Count && i < imageInfos.Count; i++)
        {
            spanIndexToImageMap[i] = i;
            _logger.LogInformation("[スパン置換] スパン[{SpanIndex}] -> 画像[{ImageIndex}] (ページ {Page})", 
                i, i, imageInfos[i].PageNumber);
        }
        
        var usedImageIndices = new HashSet<int>();
        
        // 新しい文字列を構築（元のマークダウンをスパン位置で分割して再構築）
        var resultBuilder = new System.Text.StringBuilder();
        var lastEndPosition = 0;
        
        for (int spanIndex = 0; spanIndex < sortedSpansAsc.Count; spanIndex++)
        {
            var span = sortedSpansAsc[spanIndex];
            
            // オフセットの検証
            if (span.Offset < 0 || span.Offset > markdown.Length)
            {
                _logger.LogWarning("[スパン置換] 無効なオフセット: {Offset}, Markdown長: {Length}", span.Offset, markdown.Length);
                continue;
            }
            
            // スパン開始位置が前のスパン終了位置より前の場合はスキップ（重複防止）
            if (span.Offset < lastEndPosition)
            {
                _logger.LogWarning("[スパン置換] スパンが重複しています: Offset={Offset} < LastEnd={LastEnd}", span.Offset, lastEndPosition);
                continue;
            }
            
            var endPos = Math.Min(span.Offset + span.Length, markdown.Length);
            var figureText = markdown.Substring(span.Offset, endPos - span.Offset);
            
            // スパンより前のテキストを追加
            if (span.Offset > lastEndPosition)
            {
                var beforeText = markdown.Substring(lastEndPosition, span.Offset - lastEndPosition);
                resultBuilder.Append(beforeText);
            }
            
            // プレースホルダー番号はスパンインデックス + 1 を使用
            var placeholderNum = spanIndex + 1;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderNum:D3}]]";
            
            _logger.LogInformation("[スパン置換] スパン[{SpanIndex}]: offset={Offset}, length={Length}, テキスト先頭='{TextStart}'", 
                spanIndex, span.Offset, span.Length, 
                figureText.Length > 50 ? figureText.Substring(0, 50).Replace("\n", "\\n") + "..." : figureText.Replace("\n", "\\n"));
            
            // プレースホルダーを追加
            resultBuilder.Append($"\n\n{placeholder}\n\n");
            
            // 対応する画像を割り当て
            if (spanIndexToImageMap.TryGetValue(spanIndex, out var imageIndex) && !usedImageIndices.Contains(imageIndex))
            {
                var imageInfo = imageInfos[imageIndex];
                var imageMarkdown = $"![{imageInfo.Description}]({imageInfo.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                usedImageIndices.Add(imageIndex);
                
                _logger.LogInformation("[スパン置換] {Placeholder} -> 画像[{ImageIndex}] (ページ {Page}) ✓", 
                    placeholder, imageIndex, imageInfo.PageNumber);
            }
            else
            {
                placeholderMapping[placeholder] = "";
                _logger.LogWarning("[スパン置換] {Placeholder} に対応する画像がありません", placeholder);
            }
            
            lastEndPosition = endPos;
        }
        
        // 残りのテキストを追加
        if (lastEndPosition < markdown.Length)
        {
            resultBuilder.Append(markdown.Substring(lastEndPosition));
        }
        
        // 未使用の画像を末尾に追加
        var unusedImages = imageInfos
            .Select((img, idx) => new { img, idx })
            .Where(x => !usedImageIndices.Contains(x.idx))
            .ToList();
        
        if (unusedImages.Count > 0)
        {
            _logger.LogInformation("[スパン置換] 未使用画像を末尾に追加: {Count} 件", unusedImages.Count);
            resultBuilder.AppendLine();
            resultBuilder.AppendLine();
            
            foreach (var item in unusedImages)
            {
                var placeholderNum = sortedSpansAsc.Count + item.idx + 1;
                var placeholder = $"[[IMG_PLACEHOLDER_{placeholderNum:D3}]]";
                var imageMarkdown = $"![{item.img.Description}]({item.img.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                resultBuilder.AppendLine(placeholder);
                resultBuilder.AppendLine();
                
                _logger.LogInformation("[スパン置換] 未使用画像: {Placeholder} -> 画像[{ImageIndex}]", placeholder, item.idx);
            }
        }
        
        var resultString = resultBuilder.ToString();
        
        // 置換後の検証: 元のOCRテキストが結果に残っていないことを確認
        _logger.LogInformation("[スパン置換] 置換後の検証開始...");
        foreach (var span in sortedSpansAsc)
        {
            if (!string.IsNullOrWhiteSpace(span.Content) && span.Content.Length > 20)
            {
                // OCRテキストの最初の部分（最大50文字）で検索
                var searchText = span.Content.Length > 50 ? span.Content.Substring(0, 50) : span.Content;
                // 改行や空白を正規化して検索
                var normalizedSearch = System.Text.RegularExpressions.Regex.Replace(searchText, @"\s+", " ").Trim();
                var normalizedResult = System.Text.RegularExpressions.Regex.Replace(resultString, @"\s+", " ");
                
                if (normalizedResult.Contains(normalizedSearch))
                {
                    _logger.LogWarning("[スパン置換] ⚠️ 図{Index}のOCRテキストが結果に残っています: '{Text}'", 
                        span.FigureIndex, normalizedSearch.Length > 30 ? normalizedSearch.Substring(0, 30) + "..." : normalizedSearch);
                }
                else
                {
                    _logger.LogInformation("[スパン置換] ✓ 図{Index}のOCRテキストは正常に削除されました", span.FigureIndex);
                }
            }
        }
        
        _logger.LogInformation("[スパン置換] 完了: プレースホルダー数={Count}, 使用画像数={UsedCount}", 
            placeholderMapping.Count, usedImageIndices.Count);
        
        return (resultString, placeholderMapping);
    }

    /// <summary>
    /// パターンマッチングで図参照を検出してプレースホルダーに置換（スパン情報がない場合のフォールバック）
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderMapping) ReplaceUsingPatternMatching(
        string markdown,
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderMapping = new Dictionary<string, string>();
        var usedImageIndices = new HashSet<int>();
        var placeholderIndex = 0;

        // 図参照パターン: ![...](figures/N)
        var figureRefPattern = new System.Text.RegularExpressions.Regex(
            @"!\[([^\]]*)\]\(figures/(\d+)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        var result = figureRefPattern.Replace(markdown, match =>
        {
            placeholderIndex++;
            var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
            
            var imgIndex = placeholderIndex - 1;
            if (imgIndex < imageInfos.Count && !usedImageIndices.Contains(imgIndex))
            {
                var imageInfo = imageInfos[imgIndex];
                var imageMarkdown = $"![{imageInfo.Description}]({imageInfo.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                usedImageIndices.Add(imgIndex);
            }
            else
            {
                placeholderMapping[placeholder] = "";
            }
            
            return $"\n\n{placeholder}\n\n";
        });
        
        // 未使用の画像を末尾に追加
        var unusedImages = imageInfos
            .Select((img, idx) => new { img, idx })
            .Where(x => !usedImageIndices.Contains(x.idx))
            .ToList();
        
        if (unusedImages.Count > 0)
        {
            var sb = new System.Text.StringBuilder(result);
            sb.AppendLine();
            sb.AppendLine();
            
            foreach (var item in unusedImages)
            {
                placeholderIndex++;
                var placeholder = $"[[IMG_PLACEHOLDER_{placeholderIndex:D3}]]";
                var imageMarkdown = $"![{item.img.Description}]({item.img.Url})";
                placeholderMapping[placeholder] = imageMarkdown;
                sb.AppendLine(placeholder);
                sb.AppendLine();
            }
            
            result = sb.ToString();
        }
        
        _logger.LogInformation("[パターン置換] 完了: プレースホルダー数={Count}", placeholderMapping.Count);
        
        return (result, placeholderMapping);
    }

    /// <summary>
    /// PDF ファイルからテキストのみを抽出します（画像内テキストは抽出しない）
    /// PdfPig を使用して埋め込みテキストのみを取得
    /// </summary>
    private string ExtractTextFromPdf(byte[] pdfBytes)
    {
        _logger.LogInformation("[PDFテキスト抽出] 開始: PDFサイズ={Size} bytes", pdfBytes.Length);

        var textBuilder = new System.Text.StringBuilder();

        try
        {
            using var pdfDocument = PdfDocument.Open(pdfBytes);
            
            _logger.LogInformation("[PDFテキスト抽出] PDF を開きました。ページ数: {PageCount}", pdfDocument.NumberOfPages);

            foreach (var page in pdfDocument.GetPages())
            {
                // ページ区切りマーカーを追加
                if (textBuilder.Length > 0)
                {
                    textBuilder.AppendLine();
                    textBuilder.AppendLine("---");
                    textBuilder.AppendLine();
                }

                // ページからテキストのみを抽出（画像内テキストは含まれない）
                var pageText = page.Text;
                
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    textBuilder.AppendLine(pageText);
                }
                
                _logger.LogInformation("[PDFテキスト抽出] ページ {PageNumber}: テキスト長={Length}", 
                    page.Number, pageText?.Length ?? 0);
            }

            _logger.LogInformation("[PDFテキスト抽出] 完了: 総テキスト長={Length}", textBuilder.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PDFテキスト抽出] エラーが発生しました");
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// Word (.docx) ファイルからテキストのみを抽出します（画像内テキストは抽出しない）
    /// OpenXML を使用してテキストのみを取得
    /// </summary>
    private string ExtractTextFromDocx(byte[] docxBytes)
    {
        _logger.LogInformation("[Wordテキスト抽出] 開始: DOCXサイズ={Size} bytes", docxBytes.Length);

        var textBuilder = new System.Text.StringBuilder();

        try
        {
            using var memoryStream = new MemoryStream(docxBytes);
            using var wordDocument = WordprocessingDocument.Open(memoryStream, false);

            if (wordDocument.MainDocumentPart?.Document?.Body == null)
            {
                _logger.LogWarning("[Wordテキスト抽出] ドキュメント本文が見つかりません");
                return string.Empty;
            }

            var body = wordDocument.MainDocumentPart.Document.Body;
            var paragraphCount = 0;

            // 段落ごとにテキストを抽出
            foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var paragraphText = paragraph.InnerText;
                
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    textBuilder.AppendLine(paragraphText);
                    textBuilder.AppendLine();
                    paragraphCount++;
                }
            }

            _logger.LogInformation("[Wordテキスト抽出] 完了: 段落数={ParagraphCount}, 総テキスト長={Length}", 
                paragraphCount, textBuilder.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wordテキスト抽出] エラーが発生しました");
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// 抽出したテキストを Markdown 形式に整形し、ページ区切りの位置に画像プレースホルダーを挿入します
    /// </summary>
    private (string MarkdownWithPlaceholders, Dictionary<string, string> PlaceholderMapping) CreateMarkdownWithImagePlaceholders(
        string extractedText,
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderMapping = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return (extractedText, placeholderMapping);
        }

        // 各画像にプレースホルダーを割り当て
        for (int i = 0; i < imageInfos.Count; i++)
        {
            var placeholder = $"[[IMG_PLACEHOLDER_{i + 1:D3}]]";
            var imageMarkdown = $"![{imageInfos[i].Description}]({imageInfos[i].Url})";
            placeholderMapping[placeholder] = imageMarkdown;
        }

        // ページごとに画像をグループ化
        var imagesByPage = imageInfos
            .Select((img, idx) => new { Image = img, Index = idx })
            .GroupBy(x => x.Image.PageNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Image.IndexInPage).ToList());

        _logger.LogInformation("テキストに画像プレースホルダーを挿入: 画像数={ImageCount}, ページ分布={Distribution}", 
            imageInfos.Count,
            string.Join(", ", imagesByPage.Select(kvp => $"P{kvp.Key}:{kvp.Value.Count}")));

        // ページ区切り（---）を検出して、各ページの後に画像を挿入
        var lines = extractedText.Split('\n');
        var result = new System.Text.StringBuilder();
        var currentPage = 1;

        foreach (var line in lines)
        {
            result.AppendLine(line);

            // ページ区切りを検出
            if (line.Trim() == "---" || line.Trim() == "***" || line.Trim() == "___")
            {
                // このページの画像を挿入
                if (imagesByPage.TryGetValue(currentPage, out var pageImages) && pageImages.Count > 0)
                {
                    result.AppendLine();
                    foreach (var item in pageImages)
                    {
                        var placeholder = $"[[IMG_PLACEHOLDER_{item.Index + 1:D3}]]";
                        result.AppendLine(placeholder);
                        result.AppendLine();
                    }
                    _logger.LogInformation("ページ {Page} の画像 {Count} 件をプレースホルダーとして挿入", currentPage, pageImages.Count);
                }
                currentPage++;
            }
        }

        // 最後のページの画像を末尾に追加
        var remainingPages = imagesByPage.Keys.Where(p => p >= currentPage).OrderBy(p => p);
        foreach (var page in remainingPages)
        {
            if (imagesByPage.TryGetValue(page, out var pageImages) && pageImages.Count > 0)
            {
                result.AppendLine();
                foreach (var item in pageImages)
                {
                    var placeholder = $"[[IMG_PLACEHOLDER_{item.Index + 1:D3}]]";
                    result.AppendLine(placeholder);
                    result.AppendLine();
                }
                _logger.LogInformation("ページ {Page} の画像 {Count} 件を末尾に追加", page, pageImages.Count);
            }
        }

        return (result.ToString(), placeholderMapping);
    }

    /// <summary>
    /// PDF ファイルから画像を抽出して Blob Storage に保存します
    /// ページ番号情報を保持して、元の位置に近い場所に画像を挿入できるようにします
    /// </summary>
    private async Task<List<ExtractedImageInfo>> ExtractImagesFromPdfAsync(
        byte[] pdfBytes,
        string documentId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        var imageInfos = new List<ExtractedImageInfo>();

        _logger.LogInformation("[PDF画像抽出] 開始: DocumentId={DocumentId}, Timestamp={Timestamp}, PDFサイズ={Size} bytes", 
            documentId, timestamp, pdfBytes.Length);

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_translatedContainerName);
            
            _logger.LogInformation("[PDF画像抽出] コンテナ作成を試行: {ContainerName}", _translatedContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("[PDF画像抽出] コンテナ準備完了: {ContainerName}", _translatedContainerName);

            using var pdfDocument = PdfDocument.Open(pdfBytes);
            var globalImageIndex = 0;
            var totalImagesFound = 0;

            _logger.LogInformation("[PDF画像抽出] PDF を開きました。ページ数: {PageCount}", pdfDocument.NumberOfPages);

            foreach (var page in pdfDocument.GetPages())
            {
                // ページ内の画像を取得
                var images = page.GetImages().ToList();
                var pageImageCount = images.Count;
                totalImagesFound += pageImageCount;
                
                _logger.LogInformation("[PDF画像抽出] ページ {PageNumber}/{TotalPages}: 検出された画像数={ImageCount}", 
                    page.Number, pdfDocument.NumberOfPages, pageImageCount);

                if (pageImageCount == 0)
                {
                    _logger.LogInformation("[PDF画像抽出] ページ {PageNumber} には画像がありません", page.Number);
                    continue;
                }

                var imageIndexInPage = 0;
                foreach (var image in images)
                {
                    try
                    {
                        globalImageIndex++;
                        var imageBytes = image.RawBytes.ToArray();
                        
                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: RawBytes サイズ={Size} bytes, Bounds={Bounds}", 
                            globalImageIndex, imageBytes.Length, image.Bounds);
                        
                        if (imageBytes.Length == 0)
                        {
                            _logger.LogWarning("[PDF画像抽出] 画像 {Index}: データが空のためスキップ", globalImageIndex);
                            continue;
                        }

                        // 画像形式を判定してファイル名を決定
                        var extension = DetermineImageExtension(imageBytes);
                        var imageName = $"images/{documentId}_{timestamp}/page{page.Number:D3}_image{imageIndexInPage + 1:D3}{extension}";
                        
                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: ページ={Page}, 形式={Extension}, 保存先={ImageName}", 
                            globalImageIndex, page.Number, extension, imageName);
                        
                        // Blob Storage に保存
                        var blobClient = containerClient.GetBlobClient(imageName);
                        using var imageStream = new MemoryStream(imageBytes);
                        
                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: Blob Storage にアップロード中...", globalImageIndex);
                        await blobClient.UploadAsync(imageStream, overwrite: true, cancellationToken);
                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: アップロード成功", globalImageIndex);

                        // SAS トークン付き URL を生成（24時間有効）
                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: SAS URL を生成中...", globalImageIndex);
                        var imageUrl = await GenerateImageUrlAsync(containerClient, imageName, cancellationToken);
                        
                        imageInfos.Add(new ExtractedImageInfo
                        {
                            PageNumber = page.Number,
                            IndexInPage = imageIndexInPage,
                            Url = imageUrl,
                            Description = $"ページ {page.Number} の画像 {imageIndexInPage + 1}"
                        });
                        
                        imageIndexInPage++;

                        _logger.LogInformation("[PDF画像抽出] 画像 {Index}: 完了 URL={Url}, Page={Page}", globalImageIndex, imageUrl, page.Number);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[PDF画像抽出] 画像 {Index}: 抽出に失敗しました", globalImageIndex);
                    }
                }
            }

            _logger.LogInformation("[PDF画像抽出] 完了: 検出画像数={TotalFound}, 抽出成功数={ExtractedCount}", 
                totalImagesFound, imageInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PDF画像抽出] PDF 処理中にエラーが発生しました");
        }

        return imageInfos;
    }

    /// <summary>
    /// Word (.docx) ファイルから画像を抽出して Blob Storage に保存します
    /// Word は明示的なページ情報がないため、出現順序に基づいて擬似的にページを割り当てます
    /// </summary>
    private async Task<List<ExtractedImageInfo>> ExtractImagesFromDocxAsync(
        byte[] docxBytes,
        string documentId,
        string timestamp,
        CancellationToken cancellationToken)
    {
        var imageInfos = new List<ExtractedImageInfo>();

        _logger.LogInformation("[Word画像抽出] 開始: DocumentId={DocumentId}, Timestamp={Timestamp}, DOCXサイズ={Size} bytes", 
            documentId, timestamp, docxBytes.Length);

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_translatedContainerName);
            
            _logger.LogInformation("[Word画像抽出] コンテナ作成を試行: {ContainerName}", _translatedContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("[Word画像抽出] コンテナ準備完了: {ContainerName}", _translatedContainerName);

            using var memoryStream = new MemoryStream(docxBytes);
            using var wordDocument = WordprocessingDocument.Open(memoryStream, false);

            if (wordDocument.MainDocumentPart == null)
            {
                _logger.LogWarning("[Word画像抽出] MainDocumentPart が見つかりません。画像なしとして処理を続行します。");
                return imageInfos;
            }

            var imageIndex = 0;
            var imageParts = wordDocument.MainDocumentPart.ImageParts.ToList();
            var totalImagesFound = imageParts.Count;
            
            _logger.LogInformation("[Word画像抽出] 検出された ImageParts 数: {Count}", totalImagesFound);

            if (totalImagesFound == 0)
            {
                _logger.LogInformation("[Word画像抽出] ドキュメントに埋め込み画像がありません");
                return imageInfos;
            }

            // Word は明示的なページ情報がないため、画像を3つごとに1ページとして扱う（目安）
            // 実際の配置は Markdown のセクション区切りを使用して調整
            var estimatedImagesPerPage = 3;

            // 埋め込み画像を取得
            foreach (var imagePart in imageParts)
            {
                try
                {
                    imageIndex++;
                    // 推定ページ番号を計算（1から始まる）
                    var estimatedPage = ((imageIndex - 1) / estimatedImagesPerPage) + 1;
                    var indexInPage = (imageIndex - 1) % estimatedImagesPerPage;
                    
                    _logger.LogInformation("[Word画像抽出] 画像 {Index}/{Total}: ContentType={ContentType}, Uri={Uri}, 推定ページ={Page}", 
                        imageIndex, totalImagesFound, imagePart.ContentType, imagePart.Uri, estimatedPage);
                    
                    using var imageStream = imagePart.GetStream();
                    using var imageMemoryStream = new MemoryStream();
                    await imageStream.CopyToAsync(imageMemoryStream, cancellationToken);
                    var imageBytes = imageMemoryStream.ToArray();

                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: 読み込みサイズ={Size} bytes", imageIndex, imageBytes.Length);

                    if (imageBytes.Length == 0)
                    {
                        _logger.LogWarning("[Word画像抽出] 画像 {Index}: データが空のためスキップ", imageIndex);
                        continue;
                    }

                    // 画像形式を判定してファイル名を決定
                    var extension = DetermineImageExtension(imageBytes);
                    var imageName = $"images/{documentId}_{timestamp}/image_{imageIndex:D3}{extension}";

                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: 形式={Extension}, 保存先={ImageName}", 
                        imageIndex, extension, imageName);

                    // Blob Storage に保存
                    var blobClient = containerClient.GetBlobClient(imageName);
                    imageMemoryStream.Position = 0;
                    
                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: Blob Storage にアップロード中...", imageIndex);
                    await blobClient.UploadAsync(imageMemoryStream, overwrite: true, cancellationToken);
                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: アップロード成功", imageIndex);

                    // SAS トークン付き URL を生成（24時間有効）
                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: SAS URL を生成中...", imageIndex);
                    var imageUrl = await GenerateImageUrlAsync(containerClient, imageName, cancellationToken);
                    
                    imageInfos.Add(new ExtractedImageInfo
                    {
                        PageNumber = estimatedPage,
                        IndexInPage = indexInPage,
                        Url = imageUrl,
                        Description = $"画像 {imageIndex}"
                    });

                    _logger.LogInformation("[Word画像抽出] 画像 {Index}: 完了 URL={Url}", imageIndex, imageUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Word画像抽出] 画像 {Index}: 抽出に失敗しました", imageIndex);
                }
            }

            _logger.LogInformation("[Word画像抽出] 完了: 検出画像数={TotalFound}, 抽出成功数={ExtractedCount}", 
                totalImagesFound, imageInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Word画像抽出] Word 処理中にエラーが発生しました");
        }

        return imageInfos;
    }

    /// <summary>
    /// 画像のバイトデータから拡張子を判定します
    /// </summary>
    private static string DetermineImageExtension(byte[] imageBytes)
    {
        if (imageBytes.Length < 4)
            return ".bin";

        // PNG: 89 50 4E 47
        if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            return ".png";

        // JPEG: FF D8 FF
        if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
            return ".jpg";

        // GIF: 47 49 46 38
        if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x38)
            return ".gif";

        // BMP: 42 4D
        if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
            return ".bmp";

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (imageBytes.Length > 12 && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && 
            imageBytes[2] == 0x46 && imageBytes[3] == 0x46 &&
            imageBytes[8] == 0x57 && imageBytes[9] == 0x45 && imageBytes[10] == 0x42 && imageBytes[11] == 0x50)
            return ".webp";

        // TIFF: 49 49 2A 00 or 4D 4D 00 2A
        if ((imageBytes[0] == 0x49 && imageBytes[1] == 0x49 && imageBytes[2] == 0x2A && imageBytes[3] == 0x00) ||
            (imageBytes[0] == 0x4D && imageBytes[1] == 0x4D && imageBytes[2] == 0x00 && imageBytes[3] == 0x2A))
            return ".tiff";

        return ".png";  // デフォルト
    }

    /// <summary>
    /// Document Intelligence の Markdown 出力に含まれる図参照を検出し、プレースホルダーに置換します
    /// 図参照パターン:
    /// - :figure:N （Document Intelligence v4.0 形式）
    /// - ![図N](figures/N) （Markdown 画像形式）
    /// - <figure id="N">...</figure> （HTML figure タグ）
    /// </summary>
    private (string ProcessedMarkdown, Dictionary<string, string> PlaceholderMapping) ReplaceFigureReferencesWithPlaceholders(
        string markdown,
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderMapping = new Dictionary<string, string>();
        var processedMarkdown = markdown;
        var replacementCount = 0;

        if (imageInfos == null || imageInfos.Count == 0)
        {
            _logger.LogInformation("画像がないため図参照の置換をスキップします");
            return (markdown, placeholderMapping);
        }

        // 各画像にプレースホルダーを割り当て
        for (int i = 0; i < imageInfos.Count; i++)
        {
            var placeholder = $"[[IMG_PLACEHOLDER_{i + 1:D3}]]";
            var imageMarkdown = $"![{imageInfos[i].Description}]({imageInfos[i].Url})";
            placeholderMapping[placeholder] = imageMarkdown;
        }

        // パターン1: :figure:N 形式（Document Intelligence v4.0）
        // 例: ":figure:0", ":figure:1"
        var figureColonPattern = new System.Text.RegularExpressions.Regex(@":figure:(\d+)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        processedMarkdown = figureColonPattern.Replace(processedMarkdown, match =>
        {
            var figureIndex = int.Parse(match.Groups[1].Value);
            if (figureIndex < imageInfos.Count)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{figureIndex + 1:D3}]]";
                replacementCount++;
                _logger.LogInformation("図参照を置換: {Original} -> {Placeholder}", match.Value, placeholder);
                return $"\n\n{placeholder}\n\n";
            }
            return match.Value;
        });

        // パターン2: ![...](figures/N) 形式
        // 例: "![Figure 0](figures/0)", "![図1](figures/1)"
        var figureImagePattern = new System.Text.RegularExpressions.Regex(@"!\[([^\]]*)\]\(figures?[/\\](\d+)[^)]*\)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        processedMarkdown = figureImagePattern.Replace(processedMarkdown, match =>
        {
            var figureIndex = int.Parse(match.Groups[2].Value);
            if (figureIndex < imageInfos.Count)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{figureIndex + 1:D3}]]";
                replacementCount++;
                _logger.LogInformation("図参照を置換: {Original} -> {Placeholder}", match.Value, placeholder);
                return placeholder;
            }
            return match.Value;
        });

        // パターン3: <figure> タグ形式
        // 例: <figure id="figure-0">...</figure>
        var figureTagPattern = new System.Text.RegularExpressions.Regex(
            @"<figure[^>]*id=[""']?figure-?(\d+)[""']?[^>]*>.*?</figure>", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        
        processedMarkdown = figureTagPattern.Replace(processedMarkdown, match =>
        {
            var figureIndex = int.Parse(match.Groups[1].Value);
            if (figureIndex < imageInfos.Count)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{figureIndex + 1:D3}]]";
                replacementCount++;
                _logger.LogInformation("figure タグを置換: figure-{Index} -> {Placeholder}", figureIndex, placeholder);
                return $"\n\n{placeholder}\n\n";
            }
            return match.Value;
        });

        // パターン4: ::: figure 形式（一部の Markdown 拡張）
        // 例: ::: figure
        //     内容
        //     :::
        var figureFencePattern = new System.Text.RegularExpressions.Regex(
            @":::\s*figure[^\n]*\n(.*?):::", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        
        var fenceIndex = 0;
        processedMarkdown = figureFencePattern.Replace(processedMarkdown, match =>
        {
            if (fenceIndex < imageInfos.Count)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{fenceIndex + 1:D3}]]";
                replacementCount++;
                fenceIndex++;
                _logger.LogInformation("figure フェンスを置換 -> {Placeholder}", placeholder);
                return $"\n\n{placeholder}\n\n";
            }
            fenceIndex++;
            return match.Value;
        });

        _logger.LogInformation("図参照の置換完了: {Count} 件置換しました", replacementCount);

        return (processedMarkdown, placeholderMapping);
    }

    /// <summary>
    /// 画像の URL を生成します（SAS トークン付き）
    /// </summary>
    private async Task<string> GenerateImageUrlAsync(
        Azure.Storage.Blobs.BlobContainerClient containerClient,
        string imageName,
        CancellationToken cancellationToken)
    {
        var blobClient = containerClient.GetBlobClient(imageName);

        // マネージド ID 認証の場合は CanGenerateSasUri が false になる
        if (blobClient.CanGenerateSasUri)
        {
            _logger.LogInformation("アカウントキーで SAS を生成します: {ImageName}", imageName);
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _translatedContainerName,
                BlobName = imageName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }
        else
        {
            _logger.LogInformation("マネージド ID のためユーザー委任 SAS を生成します: {ImageName}", imageName);
            return await GenerateUserDelegationSasAsync(containerClient, imageName, cancellationToken);
        }
    }

    /// <summary>
    /// Markdown 内の適切な位置にプレースホルダーを挿入し、プレースホルダーと画像URLのマッピングを返します
    /// プレースホルダー形式: [[IMG_PLACEHOLDER_001]]
    /// </summary>
    private (string MarkdownWithPlaceholders, Dictionary<string, string> PlaceholderMapping) InsertImagePlaceholdersIntoMarkdown(
        string markdown, 
        List<ExtractedImageInfo> imageInfos)
    {
        var placeholderMapping = new Dictionary<string, string>();

        if (imageInfos == null || imageInfos.Count == 0)
        {
            _logger.LogInformation("挿入する画像がありません");
            return (markdown, placeholderMapping);
        }

        _logger.LogInformation("Markdown に {Count} 件の画像プレースホルダーを挿入します", imageInfos.Count);

        // 各画像にプレースホルダーを割り当て
        for (int i = 0; i < imageInfos.Count; i++)
        {
            var placeholder = $"[[IMG_PLACEHOLDER_{i + 1:D3}]]";
            var imageMarkdown = $"![{imageInfos[i].Description}]({imageInfos[i].Url})";
            placeholderMapping[placeholder] = imageMarkdown;
            
            _logger.LogInformation("プレースホルダー割り当て: {Placeholder} -> {ImageMarkdown}", 
                placeholder, $"![{imageInfos[i].Description}](...)");
        }

        // ページごとに画像をグループ化
        var imagesByPage = imageInfos
            .Select((img, idx) => new { Image = img, Index = idx })
            .GroupBy(x => x.Image.PageNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Image.IndexInPage).ToList());

        _logger.LogInformation("画像のページ分布: {Distribution}", 
            string.Join(", ", imagesByPage.Select(kvp => $"ページ{kvp.Key}:{kvp.Value.Count}件")));

        // Document Intelligence の Markdown からページ区切りを検出
        var pageBreakPatterns = new[] 
        { 
            "<!-- PageBreak -->",
            "<!-- PageNumber=",
            "\n---\n",
            "\n***\n",
            "\n___\n"
        };

        // ページ区切りの位置を検出
        var pageBreakPositions = new List<(int Position, int PageEndNumber)>();
        var currentPosition = 0;
        var pageNumber = 1;

        while (currentPosition < markdown.Length)
        {
            var nearestBreak = -1;
            var nearestPattern = "";

            foreach (var pattern in pageBreakPatterns)
            {
                var pos = markdown.IndexOf(pattern, currentPosition, StringComparison.OrdinalIgnoreCase);
                if (pos >= 0 && (nearestBreak == -1 || pos < nearestBreak))
                {
                    nearestBreak = pos;
                    nearestPattern = pattern;
                }
            }

            if (nearestBreak >= 0)
            {
                var insertPosition = nearestBreak + nearestPattern.Length;
                pageBreakPositions.Add((insertPosition, pageNumber));
                pageNumber++;
                currentPosition = insertPosition;
            }
            else
            {
                break;
            }
        }

        // ページ区切りが見つからない場合は、見出しベースまたは末尾に配置
        if (pageBreakPositions.Count == 0)
        {
            _logger.LogInformation("ページ区切りが見つからないため、見出しベースでプレースホルダーを配置します");
            return InsertPlaceholdersAtHeadings(markdown, imageInfos, placeholderMapping);
        }

        // 後ろから挿入していく
        var sb = new System.Text.StringBuilder(markdown);
        var sortedBreaks = pageBreakPositions.OrderByDescending(b => b.Position).ToList();

        foreach (var (position, pageEndNumber) in sortedBreaks)
        {
            if (imagesByPage.TryGetValue(pageEndNumber, out var pageImageData) && pageImageData.Count > 0)
            {
                var placeholderSection = new System.Text.StringBuilder();
                placeholderSection.AppendLine();
                placeholderSection.AppendLine();
                
                foreach (var item in pageImageData)
                {
                    var placeholder = $"[[IMG_PLACEHOLDER_{item.Index + 1:D3}]]";
                    placeholderSection.AppendLine(placeholder);
                    placeholderSection.AppendLine();
                }

                sb.Insert(position, placeholderSection.ToString());
                _logger.LogInformation("ページ {Page} のプレースホルダー {Count} 件を位置 {Position} に挿入しました", 
                    pageEndNumber, pageImageData.Count, position);
            }
        }

        // 最後のページの画像を末尾に追加
        var maxPageWithBreak = pageBreakPositions.Count > 0 ? pageBreakPositions.Max(b => b.PageEndNumber) : 0;
        var remainingPages = imagesByPage.Keys.Where(p => p > maxPageWithBreak).OrderBy(p => p);

        foreach (var page in remainingPages)
        {
            if (imagesByPage.TryGetValue(page, out var pageImageData) && pageImageData.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                
                foreach (var item in pageImageData)
                {
                    var placeholder = $"[[IMG_PLACEHOLDER_{item.Index + 1:D3}]]";
                    sb.AppendLine(placeholder);
                    sb.AppendLine();
                }

                _logger.LogInformation("ページ {Page} のプレースホルダー {Count} 件を末尾に追加しました", page, pageImageData.Count);
            }
        }

        return (sb.ToString(), placeholderMapping);
    }

    /// <summary>
    /// ページ区切りが見つからない場合、見出しの位置を基準にプレースホルダーを配置します
    /// </summary>
    private (string MarkdownWithPlaceholders, Dictionary<string, string> PlaceholderMapping) InsertPlaceholdersAtHeadings(
        string markdown, 
        List<ExtractedImageInfo> imageInfos,
        Dictionary<string, string> placeholderMapping)
    {
        // 見出し（#で始まる行）の位置を検出
        var headingPattern = new System.Text.RegularExpressions.Regex(@"^(#{1,6})\s+", 
            System.Text.RegularExpressions.RegexOptions.Multiline);
        var headings = headingPattern.Matches(markdown);

        if (headings.Count == 0)
        {
            _logger.LogInformation("見出しが見つからないため、プレースホルダーを末尾に追加します");
            return AppendPlaceholdersAtEnd(markdown, imageInfos, placeholderMapping);
        }

        _logger.LogInformation("見出し数: {Count}, 画像数: {ImageCount}", headings.Count, imageInfos.Count);

        var headingPositions = headings.Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Index)
            .ToList();

        var sb = new System.Text.StringBuilder();
        var lastPosition = 0;
        var imageIndex = 0;
        var imagesPerSection = Math.Max(1, (int)Math.Ceiling((double)imageInfos.Count / headingPositions.Count));

        for (int i = 0; i < headingPositions.Count; i++)
        {
            var headingPos = headingPositions[i];
            sb.Append(markdown.Substring(lastPosition, headingPos - lastPosition));
            
            var sectionImages = imageInfos.Skip(imageIndex).Take(imagesPerSection).ToList();
            if (sectionImages.Count > 0 && i > 0)
            {
                sb.AppendLine();
                for (int j = 0; j < sectionImages.Count; j++)
                {
                    var placeholder = $"[[IMG_PLACEHOLDER_{imageIndex + j + 1:D3}]]";
                    sb.AppendLine(placeholder);
                    sb.AppendLine();
                }
                imageIndex += sectionImages.Count;
            }
            
            lastPosition = headingPos;
        }

        sb.Append(markdown.Substring(lastPosition));

        // 残りのプレースホルダーを末尾に追加
        var remainingCount = imageInfos.Count - imageIndex;
        if (remainingCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            for (int i = imageIndex; i < imageInfos.Count; i++)
            {
                var placeholder = $"[[IMG_PLACEHOLDER_{i + 1:D3}]]";
                sb.AppendLine(placeholder);
                sb.AppendLine();
            }
            _logger.LogInformation("残りのプレースホルダー {Count} 件を末尾に追加しました", remainingCount);
        }

        return (sb.ToString(), placeholderMapping);
    }

    /// <summary>
    /// プレースホルダーを末尾に追加します（フォールバック）
    /// </summary>
    private (string MarkdownWithPlaceholders, Dictionary<string, string> PlaceholderMapping) AppendPlaceholdersAtEnd(
        string markdown, 
        List<ExtractedImageInfo> imageInfos,
        Dictionary<string, string> placeholderMapping)
    {
        var sb = new System.Text.StringBuilder(markdown);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 抽出された画像");
        sb.AppendLine();

        for (int i = 0; i < imageInfos.Count; i++)
        {
            var placeholder = $"[[IMG_PLACEHOLDER_{i + 1:D3}]]";
            sb.AppendLine($"### {imageInfos[i].Description}");
            sb.AppendLine();
            sb.AppendLine(placeholder);
            sb.AppendLine();
        }

        _logger.LogInformation("Markdown の末尾に {Count} 件のプレースホルダーを追加しました", imageInfos.Count);
        return (sb.ToString(), placeholderMapping);
    }

    /// <summary>
    /// 翻訳後のテキストでプレースホルダーを実際の画像参照に置換します
    /// GPTが形式を変更する可能性があるため、複数のパターンに対応
    /// </summary>
    private string ReplacePlaceholdersWithImages(string translatedText, Dictionary<string, string> placeholderMapping)
    {
        if (placeholderMapping == null || placeholderMapping.Count == 0)
        {
            return translatedText;
        }

        _logger.LogInformation("[ステップ6] プレースホルダー置換開始: {Count} 個", placeholderMapping.Count);
        
        // 翻訳後のテキストに含まれるプレースホルダーパターンをログ出力
        // シングルブラケットとダブルブラケット両方に対応
        var allPlaceholderPatterns = new System.Text.RegularExpressions.Regex(
            @"\[+(?:IMG_PLACEHOLDER|IMAGE:?(?:IMG_PLACEHOLDER)?|PLACEHOLDER)[_:\s]*(\d+)\]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var foundPatterns = allPlaceholderPatterns.Matches(translatedText);
        _logger.LogInformation("[ステップ6] 検出されたパターン数: {Count}", foundPatterns.Count);
        foreach (System.Text.RegularExpressions.Match match in foundPatterns)
        {
            _logger.LogInformation("[ステップ6]   発見: '{Pattern}' at position {Pos}", match.Value, match.Index);
        }

        var result = translatedText;
        var replacedCount = 0;

        foreach (var kvp in placeholderMapping)
        {
            var placeholder = kvp.Key;  // 例: [[IMG_PLACEHOLDER_001]]
            var imageMarkdown = kvp.Value;
            
            if (string.IsNullOrEmpty(imageMarkdown))
            {
                continue;
            }
            
            // プレースホルダー番号を抽出
            var numMatch = System.Text.RegularExpressions.Regex.Match(placeholder, @"(\d+)");
            if (!numMatch.Success)
            {
                _logger.LogWarning("[ステップ6] プレースホルダー番号が見つかりません: {Placeholder}", placeholder);
                continue;
            }
            var num = numMatch.Value;
            var numInt = int.Parse(num);
            
            // 正規表現で柔軟に検索（シングル/ダブルブラケット、様々な形式に対応）
            var flexPattern = new System.Text.RegularExpressions.Regex(
                $@"\[+(?:IMG_PLACEHOLDER|IMAGE:?(?:IMG_PLACEHOLDER)?|PLACEHOLDER)[_:\s]*0*{numInt}\]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            var flexMatch = flexPattern.Match(result);
            if (flexMatch.Success)
            {
                result = result.Replace(flexMatch.Value, imageMarkdown);
                replacedCount++;
                _logger.LogInformation("[ステップ6] 置換成功: '{Pattern}' -> 画像", flexMatch.Value);
                continue;
            }
            
            // 見つからない場合は完全一致を試す
            if (result.Contains(placeholder))
            {
                result = result.Replace(placeholder, imageMarkdown);
                replacedCount++;
                _logger.LogInformation("[ステップ6] 完全一致で置換成功: '{Pattern}' -> 画像", placeholder);
                continue;
            }

            _logger.LogWarning("[ステップ6] プレースホルダーが見つかりません: {Placeholder} (番号: {Num})", placeholder, num);
            
            // 見つからない場合は末尾に追加
            result += $"\n\n{imageMarkdown}\n";
            _logger.LogInformation("[ステップ6] 画像を末尾に追加");
        }

        _logger.LogInformation("[ステップ6] 完了: 置換成功={Replaced}", replacedCount);
        return result;
    }

    /// <summary>
    /// Markdown 内のページ区切りを検出し、各ページの末尾に対応する画像を挿入します
    /// （後方互換性のため残しています）
    /// </summary>
    [Obsolete("Use InsertImagePlaceholdersIntoMarkdown instead")]
    private string InsertImagesIntoMarkdown(string markdown, List<ExtractedImageInfo> imageInfos)
    {
        if (imageInfos == null || imageInfos.Count == 0)
        {
            _logger.LogInformation("挿入する画像がありません");
            return markdown;
        }

        _logger.LogInformation("Markdown に {Count} 件の画像を適切な位置に挿入します", imageInfos.Count);

        // ページごとに画像をグループ化
        var imagesByPage = imageInfos
            .GroupBy(img => img.PageNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(img => img.IndexInPage).ToList());

        _logger.LogInformation("画像のページ分布: {Distribution}", 
            string.Join(", ", imagesByPage.Select(kvp => $"ページ{kvp.Key}:{kvp.Value.Count}件")));

        // Document Intelligence の Markdown からページ区切りを検出
        // 一般的なパターン: "<!-- PageBreak -->", "---", "***", "___"
        var pageBreakPatterns = new[] 
        { 
            "<!-- PageBreak -->",
            "<!-- PageNumber=",  // Document Intelligence のページマーカー
            "\n---\n",
            "\n***\n",
            "\n___\n"
        };

        // ページ区切りの位置を検出
        var pageBreakPositions = new List<(int Position, int PageEndNumber)>();
        var currentPosition = 0;
        var pageNumber = 1;

        // 各ページ区切りパターンを検索
        while (currentPosition < markdown.Length)
        {
            var nearestBreak = -1;
            var nearestPattern = "";

            foreach (var pattern in pageBreakPatterns)
            {
                var pos = markdown.IndexOf(pattern, currentPosition, StringComparison.OrdinalIgnoreCase);
                if (pos >= 0 && (nearestBreak == -1 || pos < nearestBreak))
                {
                    nearestBreak = pos;
                    nearestPattern = pattern;
                }
            }

            if (nearestBreak >= 0)
            {
                // ページ区切りの後に画像を挿入する位置を記録
                var insertPosition = nearestBreak + nearestPattern.Length;
                pageBreakPositions.Add((insertPosition, pageNumber));
                
                _logger.LogDebug("ページ区切り検出: ページ {Page}, 位置 {Position}, パターン: {Pattern}", 
                    pageNumber, nearestBreak, nearestPattern.Trim());
                
                pageNumber++;
                currentPosition = insertPosition;
            }
            else
            {
                break;
            }
        }

        // ページ区切りが見つからない場合は、見出しの前に画像を分散配置
        if (pageBreakPositions.Count == 0)
        {
            _logger.LogInformation("ページ区切りが見つからないため、見出しベースで画像を配置します");
            return InsertImagesAtHeadings(markdown, imageInfos);
        }

        // 後ろから挿入していく（位置がずれないように）
        var sb = new System.Text.StringBuilder(markdown);
        var sortedBreaks = pageBreakPositions.OrderByDescending(b => b.Position).ToList();

        foreach (var (position, pageEndNumber) in sortedBreaks)
        {
            if (imagesByPage.TryGetValue(pageEndNumber, out var pageImages) && pageImages.Count > 0)
            {
                var imageSection = new System.Text.StringBuilder();
                imageSection.AppendLine();
                imageSection.AppendLine();
                
                foreach (var img in pageImages)
                {
                    imageSection.AppendLine($"![{img.Description}]({img.Url})");
                    imageSection.AppendLine();
                }

                sb.Insert(position, imageSection.ToString());
                _logger.LogInformation("ページ {Page} の画像 {Count} 件を位置 {Position} に挿入しました", 
                    pageEndNumber, pageImages.Count, position);
            }
        }

        // 最後のページの画像（ページ区切りの後）を末尾に追加
        var maxPageWithBreak = pageBreakPositions.Count > 0 ? pageBreakPositions.Max(b => b.PageEndNumber) : 0;
        var remainingPages = imagesByPage.Keys.Where(p => p > maxPageWithBreak).OrderBy(p => p);

        foreach (var page in remainingPages)
        {
            if (imagesByPage.TryGetValue(page, out var pageImages) && pageImages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                
                foreach (var img in pageImages)
                {
                    sb.AppendLine($"![{img.Description}]({img.Url})");
                    sb.AppendLine();
                }

                _logger.LogInformation("ページ {Page} の画像 {Count} 件を末尾に追加しました", page, pageImages.Count);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// ページ区切りが見つからない場合、見出しの位置を基準に画像を配置します
    /// </summary>
    private string InsertImagesAtHeadings(string markdown, List<ExtractedImageInfo> imageInfos)
    {
        // 見出し（#で始まる行）の位置を検出
        var headingPattern = new System.Text.RegularExpressions.Regex(@"^(#{1,6})\s+", 
            System.Text.RegularExpressions.RegexOptions.Multiline);
        var headings = headingPattern.Matches(markdown);

        if (headings.Count == 0)
        {
            // 見出しも見つからない場合は末尾に追加（従来の動作）
            _logger.LogInformation("見出しが見つからないため、画像を末尾に追加します");
            return AppendImagesAtEnd(markdown, imageInfos);
        }

        _logger.LogInformation("見出し数: {Count}, 画像数: {ImageCount}", headings.Count, imageInfos.Count);

        // 見出しの数に基づいて画像を分散配置
        var headingPositions = headings.Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Index)
            .ToList();

        // 画像をセクション（見出し間）に分散
        var sb = new System.Text.StringBuilder();
        var lastPosition = 0;
        var imageIndex = 0;
        var imagesPerSection = Math.Max(1, (int)Math.Ceiling((double)imageInfos.Count / headingPositions.Count));

        for (int i = 0; i < headingPositions.Count; i++)
        {
            var headingPos = headingPositions[i];
            
            // 前の見出しから現在の見出しまでのテキストを追加
            sb.Append(markdown.Substring(lastPosition, headingPos - lastPosition));
            
            // このセクションに割り当てる画像を追加（見出しの前に配置）
            var sectionImages = imageInfos.Skip(imageIndex).Take(imagesPerSection).ToList();
            if (sectionImages.Count > 0 && i > 0) // 最初の見出しの前には追加しない
            {
                sb.AppendLine();
                foreach (var img in sectionImages)
                {
                    sb.AppendLine($"![{img.Description}]({img.Url})");
                    sb.AppendLine();
                }
                imageIndex += sectionImages.Count;
            }
            
            lastPosition = headingPos;
        }

        // 残りのテキストを追加
        sb.Append(markdown.Substring(lastPosition));

        // 残りの画像を末尾に追加
        var remainingImages = imageInfos.Skip(imageIndex).ToList();
        if (remainingImages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            foreach (var img in remainingImages)
            {
                sb.AppendLine($"![{img.Description}]({img.Url})");
                sb.AppendLine();
            }
            _logger.LogInformation("残りの画像 {Count} 件を末尾に追加しました", remainingImages.Count);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 画像を末尾に追加します（フォールバック）
    /// </summary>
    private string AppendImagesAtEnd(string markdown, List<ExtractedImageInfo> imageInfos)
    {
        var sb = new System.Text.StringBuilder(markdown);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 抽出された画像");
        sb.AppendLine();

        foreach (var img in imageInfos)
        {
            sb.AppendLine($"### {img.Description}");
            sb.AppendLine();
            sb.AppendLine($"![{img.Description}]({img.Url})");
            sb.AppendLine();
        }

        _logger.LogInformation("Markdown の末尾に {Count} 件の画像を追加しました", imageInfos.Count);
        return sb.ToString();
    }

    /// <summary>
    /// ユーザー委任 SAS を生成します
    /// マネージド ID 認証時に使用。Storage Blob Data Contributor または
    /// Storage Blob Delegator ロールが必要です。
    /// </summary>
    private async Task<string> GenerateUserDelegationSasAsync(
        Azure.Storage.Blobs.BlobContainerClient containerClient,
        string blobName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("ユーザー委任キーを取得中...");
            
            var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(24),
                cancellationToken);

            _logger.LogInformation("ユーザー委任キーを取得しました。SAS を生成中...");

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var blobUriBuilder = new BlobUriBuilder(containerClient.GetBlobClient(blobName).Uri)
            {
                Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, _blobServiceClient.AccountName)
            };

            var sasUrl = blobUriBuilder.ToUri().ToString();
            _logger.LogInformation("ユーザー委任 SAS URL を生成しました: {Url}", sasUrl);
            return sasUrl;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogError(ex, 
                "ユーザー委任 SAS の生成に失敗しました（403 Forbidden）。" +
                "マネージド ID に 'Storage Blob Data Contributor' または 'Storage Blob Delegator' ロールが必要です。" +
                "または、Storage Account の 'Allow storage account key access' を有効にしてください。");
            
            // フォールバック: 直接 URL を返す（匿名アクセスが有効な場合のみ機能）
            var directUrl = containerClient.GetBlobClient(blobName).Uri.ToString();
            _logger.LogWarning("直接 URL を返します（匿名アクセスが無効の場合は表示されません）: {Url}", directUrl);
            return directUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ユーザー委任 SAS の生成中に予期しないエラーが発生しました");
            return containerClient.GetBlobClient(blobName).Uri.ToString();
        }
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
- [[IMG_PLACEHOLDER_XXX]] 形式のプレースホルダーは絶対に変更・削除せず、そのまま出力してください。これらは翻訳後に画像に置換されます
- <figure> タグ内の構造も保持してください
- HTML タグがある場合は構造を保持してください
- プレースホルダーは翻訳対象外です。元の位置に必ず残してください";
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

    #region Inner Classes

    /// <summary>
    /// 抽出された画像の情報を保持する内部クラス
    /// </summary>
    private class ExtractedImageInfo
    {
        /// <summary>
        /// 画像があるページ番号（1から始まる）。Word の場合は推定値。
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// ページ内での画像のインデックス（0から始まる）
        /// </summary>
        public int IndexInPage { get; set; }

        /// <summary>
        /// Blob Storage に保存された画像の URL（SAS トークン付き）
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 画像の説明（altテキスト用）
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    #endregion
}
