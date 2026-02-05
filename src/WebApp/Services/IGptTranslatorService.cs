using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// GPT-4o を使用したドキュメント翻訳サービスのインターフェース
/// </summary>
public interface IGptTranslatorService
{
    /// <summary>
    /// ドキュメントの妥当性を検証します（PDF/Word のみ許可）
    /// </summary>
    /// <param name="document">検証するドキュメントファイル</param>
    /// <returns>検証結果（true: 有効、false: 無効）</returns>
    Task<bool> ValidateDocumentAsync(IFormFile document);

    /// <summary>
    /// テキストを翻訳します
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="targetLanguage">翻訳先言語コード（例: "en", "ja"）</param>
    /// <param name="options">翻訳オプション（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳結果</returns>
    Task<GptTranslationResult> TranslateTextAsync(
        string text,
        string targetLanguage,
        GptTranslationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ドキュメントを翻訳します（テキスト抽出 → 画像保存 → 翻訳 → Markdown 保存）
    /// </summary>
    /// <param name="document">アップロードされたドキュメントファイル（PDF/Word のみ）</param>
    /// <param name="targetLanguage">翻訳先言語コード（例: "en", "ja"）</param>
    /// <param name="options">翻訳オプション（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳結果（Blob Storage に保存された Markdown の情報を含む）</returns>
    Task<GptTranslationResult> TranslateDocumentAsync(
        IFormFile document,
        string targetLanguage,
        GptTranslationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Blob Storage から翻訳結果（Markdown）を取得します
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>Markdown テキスト</returns>
    Task<string> GetTranslationResultAsync(
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Blob Storage の Markdown を PDF に変換します
    /// </summary>
    /// <param name="blobName">Blob 名</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>PDF のバイナリデータ</returns>
    Task<byte[]> ConvertToPdfAsync(
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サポートされている言語の一覧を取得します
    /// </summary>
    /// <returns>言語コードと言語名の辞書（例: {"en": "English", "ja": "日本語"}）</returns>
    Task<Dictionary<string, string>> GetSupportedLanguagesAsync();
}
