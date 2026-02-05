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
    /// 重要: 画像は画像として抽出し、OCR によるテキスト抽出は行いません
    /// Document Intelligence の代わりに PdfPig/OpenXML でテキストと画像を別々に抽出
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

            using var stream = document.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            var fileBytes = memoryStream.ToArray();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var documentId = Path.GetFileNameWithoutExtension(document.FileName);
            var fileExtension = Path.GetExtension(document.FileName).ToLowerInvariant();

            // 2. ファイルからテキストと画像を別々に抽出（画像の OCR は行わない）
            _logger.LogInformation("ファイルからテキストと画像を別々に抽出中... ファイル形式: {Extension}", fileExtension);
            _logger.LogInformation("重要: 画像内のテキストは OCR で抽出しません。画像は画像として保持します。");

            string extractedText;
            List<ExtractedImageInfo> imageInfos;

            if (fileExtension == ".pdf")
            {
                // PDF からテキストのみを抽出（画像内テキストは抽出しない）
                extractedText = ExtractTextFromPdf(fileBytes);
                // PDF から画像を抽出（OCR は行わない）
                imageInfos = await ExtractImagesFromPdfAsync(fileBytes, documentId, timestamp, cancellationToken);
            }
            else if (fileExtension == ".docx")
            {
                // Word からテキストのみを抽出（画像内テキストは抽出しない）
                extractedText = ExtractTextFromDocx(fileBytes);
                // Word から画像を抽出（OCR は行わない）
                imageInfos = await ExtractImagesFromDocxAsync(fileBytes, documentId, timestamp, cancellationToken);
            }
            else
            {
                return GptTranslationResult.Failure(
                    $"サポートされていないファイル形式です: {fileExtension}",
                    document.FileName);
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return GptTranslationResult.Failure("ドキュメントからテキストを抽出できませんでした。", document.FileName);
            }

            _logger.LogInformation("テキスト抽出完了。文字数: {Length}, 画像数: {ImageCount}", 
                extractedText.Length, imageInfos.Count);

            // URL のリストを抽出（GptTranslationResult 用）
            var imageUrls = imageInfos.Select(i => i.Url).ToList();

            foreach (var info in imageInfos)
            {
                _logger.LogInformation("抽出された画像: ページ={Page}, 説明={Description}", 
                    info.PageNumber, info.Description);
            }

            // 3. テキストを Markdown 形式に整形し、ページ区切りに画像プレースホルダーを挿入
            var (markdownWithPlaceholders, placeholderMapping) = CreateMarkdownWithImagePlaceholders(extractedText, imageInfos);

            _logger.LogInformation("Markdown 作成完了。プレースホルダー数: {Count}", placeholderMapping.Count);
            
            // 挿入後のプレースホルダーをログ出力
            var placeholderPatterns = System.Text.RegularExpressions.Regex.Matches(markdownWithPlaceholders, @"\[\[IMG_PLACEHOLDER_\d+\]\]");
            _logger.LogInformation("挿入後のプレースホルダー数: {Count}", placeholderPatterns.Count);
            foreach (System.Text.RegularExpressions.Match match in placeholderPatterns)
            {
                _logger.LogInformation("プレースホルダー: {Pattern}", match.Value);
            }

            // 4. GPT-4o で翻訳（プレースホルダーを保持）
            _logger.LogInformation("GPT-4o で翻訳中...");

            var translationResult = await TranslateMarkdownAsync(
                markdownWithPlaceholders,
                targetLanguage,
                options,
                cancellationToken);

            if (!translationResult.IsSuccess)
            {
                return translationResult;
            }

            // 5. 翻訳後のテキストでプレースホルダーを実際の画像参照に置換
            var translatedTextWithImages = ReplacePlaceholdersWithImages(translationResult.TranslatedText, placeholderMapping);
            
            // 置換後の画像参照をログ出力
            var finalImagePatterns = System.Text.RegularExpressions.Regex.Matches(translatedTextWithImages, @"!\[.*?\]\([^)]+\)");
            _logger.LogInformation("最終的な画像参照数: {Count}", finalImagePatterns.Count);
            foreach (System.Text.RegularExpressions.Match match in finalImagePatterns)
            {
                _logger.LogInformation("最終画像参照: {Pattern}", match.Value);
            }

            // 6. 翻訳結果を Blob Storage に保存
            var blobName = $"{documentId}_{targetLanguage}_{timestamp}.md";
            var blobUrl = await SaveTranslationResultAsync(blobName, translatedTextWithImages, cancellationToken);

            var completedTime = DateTime.UtcNow;

            _logger.LogInformation(
                "ドキュメント翻訳が完了しました。Blob: {BlobName}, 処理時間: {Duration}秒",
                blobName,
                (completedTime - startTime).TotalSeconds);

            return GptTranslationResult.Success(
                originalFileName: document.FileName,
                originalText: markdownWithPlaceholders,
                translatedText: translatedTextWithImages,
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
    /// </summary>
    private string ReplacePlaceholdersWithImages(string translatedText, Dictionary<string, string> placeholderMapping)
    {
        if (placeholderMapping == null || placeholderMapping.Count == 0)
        {
            return translatedText;
        }

        var result = translatedText;
        var replacedCount = 0;
        var missingCount = 0;

        foreach (var kvp in placeholderMapping)
        {
            var placeholder = kvp.Key;
            var imageMarkdown = kvp.Value;

            if (result.Contains(placeholder))
            {
                result = result.Replace(placeholder, imageMarkdown);
                replacedCount++;
                _logger.LogInformation("プレースホルダー置換: {Placeholder} -> 画像参照", placeholder);
            }
            else
            {
                missingCount++;
                _logger.LogWarning("プレースホルダーが見つかりません（翻訳時に失われた可能性）: {Placeholder}", placeholder);
                
                // 失われたプレースホルダーの画像は末尾に追加
                result += $"\n\n{imageMarkdown}\n";
                _logger.LogInformation("失われたプレースホルダーの画像を末尾に追加: {Placeholder}", placeholder);
            }
        }

        _logger.LogInformation("プレースホルダー置換完了: 成功={Replaced}, 失われた={Missing}", replacedCount, missingCount);
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
