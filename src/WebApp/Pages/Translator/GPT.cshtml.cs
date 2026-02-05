using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Pages.Translator;

/// <summary>
/// GPT-4o を使用したドキュメント翻訳ページの PageModel
/// </summary>
public class GPTModel : PageModel
{
    private readonly IGptTranslatorService _translatorService;
    private readonly IPdfConverterService _pdfConverterService;
    private readonly ILogger<GPTModel> _logger;

    /// <summary>
    /// デフォルトプロンプト定数
    /// </summary>
    public static class DefaultPrompts
    {
        public const string SystemPrompt = @"あなたはプロフェッショナルな翻訳者です。
以下のルールに従って翻訳してください：

1. 原文の意味を正確に保ちながら、自然な表現で翻訳する
2. 文書の構造（見出し、リスト、表）を Markdown 形式で保持する
3. 専門用語は適切に翻訳し、必要に応じて原語を括弧内に残す
4. 文化的なニュアンスを考慮した翻訳を行う
5. 画像プレースホルダー（[IMAGE:xxx]）はそのまま保持し、翻訳しない
6. 元のドキュメントのレイアウト（段落構成、見出し階層）を可能な限り再現する";

        public const string UserPromptTemplate = @"以下のテキストを{targetLanguage}に翻訳してください。

注意事項:
- 画像プレースホルダー [IMAGE:xxx] は翻訳せず、そのままの位置に残してください
- 見出しの階層（#, ##, ### など）を維持してください
- 表形式は Markdown テーブル形式で保持してください";
    }

    public GPTModel(
        IGptTranslatorService translatorService,
        IPdfConverterService pdfConverterService,
        ILogger<GPTModel> logger)
    {
        _translatorService = translatorService;
        _pdfConverterService = pdfConverterService;
        _logger = logger;
    }

    /// <summary>
    /// デフォルトのシステムプロンプトを UI に公開
    /// </summary>
    public string DefaultSystemPrompt => DefaultPrompts.SystemPrompt;

    /// <summary>
    /// デフォルトのユーザープロンプトテンプレートを UI に公開
    /// </summary>
    public string DefaultUserPromptTemplate => DefaultPrompts.UserPromptTemplate;

    public void OnGet()
    {
        _logger.LogInformation("GPT-4o ドキュメント翻訳ページが読み込まれました");
    }

    /// <summary>
    /// サポートされている言語一覧を取得
    /// </summary>
    public async Task<IActionResult> OnGetLanguagesAsync()
    {
        try
        {
            var languages = await _translatorService.GetSupportedLanguagesAsync();
            return new JsonResult(languages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語一覧の取得中にエラーが発生しました");
            return StatusCode(500, new { error = "言語一覧の取得に失敗しました" });
        }
    }

    /// <summary>
    /// ドキュメント翻訳を実行
    /// </summary>
    /// <param name="document">アップロードされたドキュメント（PDF/Word）</param>
    /// <param name="targetLanguage">翻訳先言語コード</param>
    /// <param name="systemPrompt">システムプロンプト（空の場合はデフォルト使用）</param>
    /// <param name="userPrompt">ユーザープロンプト（空の場合はデフォルト使用）</param>
    /// <param name="sourceLanguage">翻訳元言語コード（オプション、自動検出）</param>
    /// <param name="tone">トーン（オプション）</param>
    /// <param name="domain">ドメイン（オプション）</param>
    public async Task<IActionResult> OnPostTranslateAsync(
        IFormFile document,
        string targetLanguage,
        string? systemPrompt = null,
        string? userPrompt = null,
        string? sourceLanguage = null,
        string? tone = null,
        string? domain = null)
    {
        try
        {
            // バリデーション: ドキュメント
            if (document == null || document.Length == 0)
            {
                _logger.LogWarning("ドキュメントが選択されていません");
                return BadRequest(new { error = "ドキュメントが選択されていません" });
            }

            // バリデーション: 翻訳先言語
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                _logger.LogWarning("翻訳先言語が選択されていません");
                return BadRequest(new { error = "翻訳先言語を選択してください" });
            }

            _logger.LogInformation(
                "GPT翻訳開始: {FileName} ({Length} bytes), Target: {TargetLanguage}, Source: {SourceLanguage}, Tone: {Tone}, Domain: {Domain}",
                document.FileName, document.Length, targetLanguage, sourceLanguage ?? "auto", tone ?? "default", domain ?? "general");

            // ドキュメント検証（PDF/Word のみ）
            var isValid = await _translatorService.ValidateDocumentAsync(document);
            if (!isValid)
            {
                _logger.LogWarning("ドキュメント形式が無効: {FileName}", document.FileName);
                return BadRequest(new { error = "ドキュメントの形式が無効です。対応形式: PDF (.pdf), Word (.docx)" });
            }

            // 翻訳オプションを構築
            var options = new GptTranslationOptions
            {
                SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? null : sourceLanguage,
                SystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
                UserPrompt = string.IsNullOrWhiteSpace(userPrompt) ? null : userPrompt,
                Tone = string.IsNullOrWhiteSpace(tone) ? null : tone,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                PreserveFormatting = true
            };

            // 翻訳実行
            var result = await _translatorService.TranslateDocumentAsync(document, targetLanguage, options);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("翻訳に失敗しました: {ErrorMessage}", result.ErrorMessage);
                return BadRequest(new { error = result.ErrorMessage });
            }

            _logger.LogInformation(
                "GPT翻訳完了: {FileName} → {BlobName}, 文字数: {CharacterCount}, 処理時間: {Duration}ms",
                document.FileName,
                result.BlobName,
                result.CharacterCount,
                result.Duration.TotalMilliseconds);

            // 翻訳結果を JSON で返却
            return new JsonResult(new
            {
                success = true,
                blobName = result.BlobName,
                blobUrl = result.BlobUrl,
                characterCount = result.CharacterCount,
                duration = result.Duration.TotalSeconds,
                sourceLanguage = result.SourceLanguage,
                targetLanguage = result.TargetLanguage,
                tokensUsed = result.TokensUsed,
                inputTokens = result.InputTokens,
                outputTokens = result.OutputTokens
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "バリデーションエラー");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPT翻訳中にエラーが発生しました");
            return StatusCode(500, new { error = "翻訳処理中にエラーが発生しました。しばらく待ってから再試行してください。" });
        }
    }

    /// <summary>
    /// Blob Storage から翻訳結果（Markdown）を取得
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    public async Task<IActionResult> OnGetResultAsync(string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                return BadRequest(new { error = "Blob 名が指定されていません" });
            }

            _logger.LogInformation("翻訳結果を取得: {BlobName}", blobName);

            var markdown = await _translatorService.GetTranslationResultAsync(blobName);

            return new JsonResult(new
            {
                success = true,
                blobName = blobName,
                markdown = markdown,
                characterCount = markdown.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳結果の取得中にエラーが発生しました: {BlobName}", blobName);
            return StatusCode(500, new { error = "翻訳結果の取得に失敗しました" });
        }
    }

    /// <summary>
    /// Blob Storage の Markdown を PDF に変換してダウンロード
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    public async Task<IActionResult> OnPostConvertToPdfAsync(string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                return BadRequest(new { error = "Blob 名が指定されていません" });
            }

            _logger.LogInformation("PDF 変換開始: {BlobName}", blobName);

            // Blob から Markdown を取得
            var markdown = await _translatorService.GetTranslationResultAsync(blobName);

            // Markdown を PDF に変換
            var pdfBytes = await _pdfConverterService.ConvertMarkdownToPdfAsync(markdown);

            // ファイル名を生成（.md → .pdf）
            var pdfFileName = Path.ChangeExtension(blobName, ".pdf");

            _logger.LogInformation("PDF 変換完了: {BlobName} → {PdfFileName}, サイズ: {Size} bytes",
                blobName, pdfFileName, pdfBytes.Length);

            // PDF ファイルをダウンロード用に返却
            return File(pdfBytes, "application/pdf", pdfFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF 変換中にエラーが発生しました: {BlobName}", blobName);
            return StatusCode(500, new { error = "PDF 変換に失敗しました" });
        }
    }

    /// <summary>
    /// Markdown を HTML に変換（プレビュー用）
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    public async Task<IActionResult> OnGetPreviewHtmlAsync(string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                return BadRequest(new { error = "Blob 名が指定されていません" });
            }

            _logger.LogInformation("HTML プレビュー生成: {BlobName}", blobName);

            // Blob から Markdown を取得
            var markdown = await _translatorService.GetTranslationResultAsync(blobName);

            // Markdown を HTML に変換
            var html = _pdfConverterService.ConvertMarkdownToHtml(markdown);

            return new JsonResult(new
            {
                success = true,
                blobName = blobName,
                html = html
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTML プレビュー生成中にエラーが発生しました: {BlobName}", blobName);
            return StatusCode(500, new { error = "HTML プレビューの生成に失敗しました" });
        }
    }

    /// <summary>
    /// Markdown ファイルをダウンロード
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    public async Task<IActionResult> OnGetDownloadMarkdownAsync(string blobName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                return BadRequest(new { error = "Blob 名が指定されていません" });
            }

            _logger.LogInformation("Markdown ダウンロード: {BlobName}", blobName);

            // Blob から Markdown を取得
            var markdown = await _translatorService.GetTranslationResultAsync(blobName);

            // Markdown をバイト配列に変換（UTF-8）
            var markdownBytes = System.Text.Encoding.UTF8.GetBytes(markdown);

            return File(markdownBytes, "text/markdown", blobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Markdown ダウンロード中にエラーが発生しました: {BlobName}", blobName);
            return StatusCode(500, new { error = "Markdown ダウンロードに失敗しました" });
        }
    }
}
