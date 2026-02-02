using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Azure Translator を使用したドキュメント翻訳サービス
/// </summary>
public interface ITranslatorService
{
    /// <summary>
    /// ドキュメントを翻訳し、完了まで待機して結果を返します
    /// </summary>
    /// <param name="document">アップロードされたドキュメントファイル</param>
    /// <param name="targetLanguage">翻訳先言語コード（例: "en", "ja"）</param>
    /// <param name="sourceLanguage">翻訳元言語コード（null の場合は自動検出）</param>
    /// <returns>翻訳結果（翻訳済みドキュメントのバイナリデータを含む）</returns>
    Task<TranslationResult> TranslateDocumentAsync(
        IFormFile document, 
        string targetLanguage, 
        string? sourceLanguage = null);

    /// <summary>
    /// ドキュメントの妥当性を検証します
    /// </summary>
    /// <param name="document">検証するドキュメントファイル</param>
    /// <returns>検証結果（true: 有効、false: 無効）</returns>
    Task<bool> ValidateDocumentAsync(IFormFile document);

    /// <summary>
    /// サポートされている言語の一覧を取得します
    /// </summary>
    /// <returns>言語コードと言語名の辞書（例: {"en": "English", "ja": "Japanese"}）</returns>
    Task<Dictionary<string, string>> GetSupportedLanguagesAsync();
}
