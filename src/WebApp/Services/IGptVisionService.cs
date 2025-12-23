namespace WebApp.Services;

/// <summary>
/// GPT-4o を使用した画像からのテキスト抽出サービスのインターフェース
/// </summary>
public interface IGptVisionService
{
    /// <summary>
    /// 画像からテキストを抽出します
    /// </summary>
    /// <param name="imageStream">画像ストリーム</param>
    /// <param name="prompt">カスタムプロンプト（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>抽出されたテキスト</returns>
    Task<string> ExtractTextFromImageAsync(Stream imageStream, string? prompt = null, CancellationToken cancellationToken = default);
}
