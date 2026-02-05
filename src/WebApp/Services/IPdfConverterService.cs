using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Markdown から PDF への変換サービスのインターフェース
/// </summary>
public interface IPdfConverterService
{
    /// <summary>
    /// Markdown を PDF に変換します
    /// </summary>
    /// <param name="markdown">Markdown テキスト</param>
    /// <param name="options">PDF オプション（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>PDF のバイナリデータ</returns>
    Task<byte[]> ConvertMarkdownToPdfAsync(
        string markdown,
        Models.PdfOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Markdown を HTML に変換します
    /// </summary>
    /// <param name="markdown">Markdown テキスト</param>
    /// <returns>HTML テキスト</returns>
    string ConvertMarkdownToHtml(string markdown);

    /// <summary>
    /// Chromium ブラウザがダウンロード済みかどうかを確認します
    /// </summary>
    /// <returns>ダウンロード済みの場合は true</returns>
    Task<bool> EnsureBrowserDownloadedAsync(CancellationToken cancellationToken = default);
}
