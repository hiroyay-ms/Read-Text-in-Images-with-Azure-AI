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

            // テキスト行を抽出し、単語の信頼度から行の信頼度を計算
            var lines = new List<TextLine>();
            var allConfidences = new List<double>();

            foreach (var page in result.Pages)
            {
                foreach (var line in page.Lines)
                {
                    // 行に含まれる単語の信頼度を取得
                    var lineConfidences = new List<double>();
                    
                    // page.Words から、この行に含まれる単語を特定して信頼度を取得
                    if (page.Words != null)
                    {
                        foreach (var word in page.Words)
                        {
                            // 単語が行の範囲内にあるかチェック（Spanを使用）
                            if (line.Spans.Any(lineSpan => 
                                word.Span.Index >= lineSpan.Index && 
                                word.Span.Index < lineSpan.Index + lineSpan.Length))
                            {
                                if (word.Confidence > 0)
                                {
                                    lineConfidences.Add(word.Confidence);
                                }
                            }
                        }
                    }

                    // 行の平均信頼度を計算（単語がない場合は0）
                    var lineConfidence = lineConfidences.Any() ? lineConfidences.Average() : 0.0;
                    
                    lines.Add(new TextLine
                    {
                        Text = line.Content,
                        Confidence = lineConfidence
                    });

                    if (lineConfidence > 0)
                    {
                        allConfidences.Add(lineConfidence);
                    }
                }
            }

            // 全体の平均信頼度を計算
            var overallConfidence = allConfidences.Any() ? allConfidences.Average() : 0.0;

            _logger.LogInformation("OCR処理完了: {LineCount} 行抽出、平均信頼度: {Confidence:P1}", 
                lines.Count, overallConfidence);

            return new OcrResult
            {
                Success = true,
                ExtractedText = result.Content,
                Lines = lines,
                PageCount = result.Pages.Count,
                ConfidenceScore = overallConfidence
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Azure API rate limit exceeded");
            throw new InvalidOperationException("リクエストが多すぎます。しばらく待ってから再試行してください。");
        }
        catch (RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
        {
            _logger.LogError(ex, "Azure API 認証エラー");
            throw new InvalidOperationException("Azure Document Intelligence の認証に失敗しました。設定を確認してください。");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure API エラー: {StatusCode}", ex.Status);
            throw new InvalidOperationException($"OCR処理中にエラーが発生しました（エラーコード: {ex.Status}）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR処理中に予期しないエラーが発生しました");
            throw new InvalidOperationException("OCR処理中に予期しないエラーが発生しました。");
        }
    }
}
