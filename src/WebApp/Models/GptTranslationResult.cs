namespace WebApp.Models;

/// <summary>
/// GPT-4o による翻訳結果
/// </summary>
public class GptTranslationResult
{
    /// <summary>
    /// 元のファイル名
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// 元のテキスト（抽出されたテキスト）
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳済みテキスト（Markdown 形式）
    /// </summary>
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳元言語（検出された言語）
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Blob Storage に保存された Markdown ファイル名
    /// </summary>
    public string BlobName { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳結果の URL（SAS トークン付きまたは公開 URL）
    /// </summary>
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// Blob Storage に保存された画像の URL 一覧
    /// </summary>
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>
    /// 抽出された画像の数
    /// </summary>
    public int ImageCount { get; set; }

    /// <summary>
    /// 使用したトークン数（入力 + 出力）
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// 入力トークン数
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// 出力トークン数
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 翻訳された文字数
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// 開始時刻（UTC）
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 完了時刻（UTC）
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// 処理が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// エラーメッセージ（失敗時のみ）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 成功結果を作成します
    /// </summary>
    public static GptTranslationResult Success(
        string originalFileName,
        string originalText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        string blobName,
        string blobUrl,
        List<string> imageUrls,
        int inputTokens,
        int outputTokens,
        DateTime startedAt,
        DateTime completedAt)
    {
        return new GptTranslationResult
        {
            OriginalFileName = originalFileName,
            OriginalText = originalText,
            TranslatedText = translatedText,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            BlobName = blobName,
            BlobUrl = blobUrl,
            ImageUrls = imageUrls,
            ImageCount = imageUrls.Count,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TokensUsed = inputTokens + outputTokens,
            CharacterCount = translatedText.Length,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IsSuccess = true
        };
    }

    /// <summary>
    /// 失敗結果を作成します
    /// </summary>
    public static GptTranslationResult Failure(string errorMessage, string originalFileName = "")
    {
        return new GptTranslationResult
        {
            OriginalFileName = originalFileName,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
    }
}
