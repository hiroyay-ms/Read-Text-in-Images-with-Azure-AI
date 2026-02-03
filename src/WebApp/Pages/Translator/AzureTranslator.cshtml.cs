using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Services;

namespace WebApp.Pages.Translator;

public class AzureTranslatorModel : PageModel
{
    private readonly ITranslatorService _translatorService;
    private readonly ILogger<AzureTranslatorModel> _logger;

    public AzureTranslatorModel(ITranslatorService translatorService, ILogger<AzureTranslatorModel> logger)
    {
        _translatorService = translatorService;
        _logger = logger;
    }

    public void OnGet()
    {
        // ページ初期表示
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
    /// ドキュメント翻訳を実行（同期的に完了まで待機し、ファイルを返却）
    /// </summary>
    public async Task<IActionResult> OnPostTranslateAsync(IFormFile document, string targetLanguage, string? sourceLanguage = null)
    {
        try
        {
            // バリデーション
            if (document == null || document.Length == 0)
            {
                return BadRequest(new { error = "ドキュメントが選択されていません" });
            }

            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                return BadRequest(new { error = "翻訳先言語を選択してください" });
            }

            _logger.LogInformation("翻訳開始: {FileName} ({Length} bytes), Target: {TargetLanguage}, Source: {SourceLanguage}",
                document.FileName, document.Length, targetLanguage, sourceLanguage ?? "auto");

            // ドキュメント検証
            var isValid = await _translatorService.ValidateDocumentAsync(document);
            if (!isValid)
            {
                return BadRequest(new { error = "ドキュメントの形式またはサイズが無効です（最大 40MB、対応形式: PDF, DOCX, XLSX, PPTX, HTML, TXT）" });
            }

            // 翻訳実行（完了まで待機）
            var result = await _translatorService.TranslateDocumentAsync(document, targetLanguage, sourceLanguage);

            _logger.LogInformation("翻訳完了: {FileName}, 文字数: {CharactersTranslated}, 処理時間: {Duration}",
                result.TranslatedFileName, result.CharactersTranslated, result.Duration);

            // ファイル名を改善：元のファイル名_言語コード.拡張子
            var originalNameWithoutExt = Path.GetFileNameWithoutExtension(document.FileName);
            var extension = Path.GetExtension(document.FileName);
            var downloadFileName = $"{originalNameWithoutExt}_{targetLanguage}{extension}";

            // 翻訳情報をカスタムヘッダーとして追加
            Response.Headers.Append("X-Translation-Characters", result.CharactersTranslated.ToString());
            Response.Headers.Append("X-Translation-Duration", result.Duration.TotalSeconds.ToString("F1"));
            Response.Headers.Append("X-Translation-Source-Language", result.SourceLanguage ?? "auto");
            Response.Headers.Append("X-Translation-Target-Language", result.TargetLanguage);

            // 翻訳済みファイルを返却
            return File(result.TranslatedContent, result.ContentType, downloadFileName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "バリデーションエラー");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳中にエラーが発生しました");
            return StatusCode(500, new { error = "翻訳処理中にエラーが発生しました。しばらく待ってから再試行してください。" });
        }
    }
}
