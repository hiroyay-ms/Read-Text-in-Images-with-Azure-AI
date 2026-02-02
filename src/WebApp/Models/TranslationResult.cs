namespace WebApp.Models;

/// <summary>
/// Azure Translator によるドキュメント翻訳結果
/// </summary>
public class TranslationResult
{
    /// <summary>
    /// 元のファイル名
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳後のファイル名
    /// </summary>
    public string TranslatedFileName { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳済みドキュメントのバイナリデータ
    /// </summary>
    public byte[] TranslatedContent { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// ファイルの MIME タイプ
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳元言語（検出された言語）
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳された文字数
    /// </summary>
    public int CharactersTranslated { get; set; }

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
}
