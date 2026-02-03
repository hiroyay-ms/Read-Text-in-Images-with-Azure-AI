using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Services;

namespace WebApp.Pages.OCR;

public class DocumentIntelligenceModel : PageModel
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<DocumentIntelligenceModel> _logger;

    public DocumentIntelligenceModel(IOcrService ocrService, ILogger<DocumentIntelligenceModel> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    public void OnGet()
    {
        // ページ初期表示
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile imageFile)
    {
        try
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return BadRequest(new { error = "画像ファイルが選択されていません" });
            }

            _logger.LogInformation("画像アップロード: {FileName} ({Length} bytes)",
                imageFile.FileName, imageFile.Length);

            var result = await _ocrService.ExtractTextAsync(imageFile);

            if (!result.Success)
            {
                return BadRequest(new { error = "テキストの抽出に失敗しました" });
            }

            return new JsonResult(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "バリデーションエラー");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラー");
            return StatusCode(500, new { error = "予期しないエラーが発生しました。しばらく待ってから再試行してください。" });
        }
    }
}
