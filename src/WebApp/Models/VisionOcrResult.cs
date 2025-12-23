namespace WebApp.Models;

/// <summary>
/// GPT-4o によるテキスト抽出結果
/// </summary>
public class VisionOcrResult
{
    /// <summary>
    /// 抽出されたテキスト
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// 使用されたメソッド
    /// </summary>
    public string Method { get; set; } = "GPT-4o";

    /// <summary>
    /// 文字数
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// 処理日時（UTC）
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// 使用されたカスタムプロンプト（あれば）
    /// </summary>
    public string? CustomPrompt { get; set; }
}
