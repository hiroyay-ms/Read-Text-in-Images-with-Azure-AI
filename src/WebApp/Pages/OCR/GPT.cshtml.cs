using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Pages.OCR;

public class GPTModel : PageModel
{
    private readonly IGptVisionService _visionService;
    private readonly ILogger<GPTModel> _logger;

    public GPTModel(
        IGptVisionService visionService,
        ILogger<GPTModel> logger)
    {
        _visionService = visionService;
        _logger = logger;
    }

    public void OnGet()
    {
        _logger.LogInformation("GPT-4o Vision OCR ページが読み込まれました");
    }

    public async Task<IActionResult> OnPostExtractAsync(IFormFile imageFile, string? customPrompt)
    {
        try
        {
            _logger.LogInformation("GPT-4o テキスト抽出リクエストを受信しました");

            // ファイルの存在チェック
            if (imageFile == null || imageFile.Length == 0)
            {
                _logger.LogWarning("画像ファイルが選択されていません");
                return BadRequest(new OcrError 
                { 
                    Message = "画像ファイルが選択されていません",
                    ErrorCode = "NO_FILE"
                });
            }

            _logger.LogInformation("画像アップロード: {FileName} ({Length} bytes), カスタムプロンプト: {HasPrompt}",
                imageFile.FileName, imageFile.Length, !string.IsNullOrWhiteSpace(customPrompt));

            // GPT-4o を使用してテキスト抽出
            using var stream = imageFile.OpenReadStream();
            var extractedText = await _visionService.ExtractTextFromImageAsync(
                stream, 
                customPrompt);

            // 結果を構築
            var result = new VisionOcrResult
            {
                ExtractedText = extractedText,
                Method = "GPT-4o",
                CharacterCount = extractedText.Length,
                ProcessedAt = DateTime.UtcNow,
                CustomPrompt = customPrompt
            };

            _logger.LogInformation("テキスト抽出が完了しました。文字数: {Count}", result.CharacterCount);

            return new JsonResult(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "GPT-4o 処理エラー");
            return BadRequest(new OcrError 
            { 
                Message = ex.Message,
                ErrorCode = "PROCESSING_ERROR"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラーが発生しました");
            return StatusCode(500, new OcrError 
            { 
                Message = "予期しないエラーが発生しました。しばらく待ってから再試行してください。",
                ErrorCode = "INTERNAL_ERROR"
            });
        }
    }
}
