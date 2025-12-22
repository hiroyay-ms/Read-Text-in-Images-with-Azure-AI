using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.AspNetCore.Http;
using WebApp.Models;

namespace WebApp.Services;

public class DocumentIntelligenceService : IOcrService
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly FileUploadOptions _options;

    public DocumentIntelligenceService(
        DocumentAnalysisClient client,
        ILogger<DocumentIntelligenceService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _options = configuration.GetSection("FileUpload").Get<FileUploadOptions>() 
            ?? new FileUploadOptions();
    }

    public async Task<bool> ValidateImageAsync(IFormFile imageFile)
    {
        if (imageFile == null || imageFile.Length == 0)
        {
            _logger.LogWarning("画像ファイルが選択されていません");
            return false;
        }

        // ファイルサイズチェック
        var maxSizeBytes = _options.MaxFileSizeMB * 1024 * 1024;
        if (imageFile.Length > maxSizeBytes)
        {
            _logger.LogWarning("ファイルサイズが {MaxSize}MB を超えています", _options.MaxFileSizeMB);
            return false;
        }

        // 拡張子チェック
        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning("サポートされていないファイル形式です: {Extension}", extension);
            return false;
        }

        return true;
    }

    public async Task<OcrResult> ExtractTextAsync(IFormFile imageFile)
    {
        try
        {
            // ファイル検証
            if (!await ValidateImageAsync(imageFile))
            {
                return new OcrResult { Success = false };
            }

            _logger.LogInformation("OCR処理を開始: {FileName}", imageFile.FileName);

            using var stream = imageFile.OpenReadStream();
            
            // Document Intelligence API 呼び出し
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                stream
            );

            var result = operation.Value;

            // テキスト行を抽出
            var lines = new List<TextLine>();
            foreach (var page in result.Pages)
            {
                foreach (var line in page.Lines)
                {
                    lines.Add(new TextLine
                    {
                        Text = line.Content,
                        Confidence = 1.0 // DocumentLine には Confidence プロパティがないため固定値
                    });
                }
            }

            _logger.LogInformation("OCR処理完了: {LineCount} 行抽出", lines.Count);

            return new OcrResult
            {
                Success = true,
                ExtractedText = result.Content,
                Lines = lines,
                PageCount = result.Pages.Count,
                ConfidenceScore = 1.0 // 全体の信頼度も固定値
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure API エラー: {StatusCode}", ex.Status);
            return new OcrResult { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR処理中にエラーが発生しました");
            return new OcrResult { Success = false };
        }
    }
}
