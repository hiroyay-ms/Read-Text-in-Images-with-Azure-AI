namespace WebApp.Models;

/// <summary>
/// PDF 変換オプション
/// </summary>
public class PdfOptions
{
    /// <summary>
    /// PDF タイトル
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 作成者
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// 横向きモード
    /// </summary>
    public bool LandscapeMode { get; set; } = false;

    /// <summary>
    /// ページサイズ（A4, Letter など）
    /// </summary>
    public string PageSize { get; set; } = "A4";

    /// <summary>
    /// ヘッダー HTML
    /// </summary>
    public string? HeaderHtml { get; set; }

    /// <summary>
    /// フッター HTML（ページ番号など）
    /// </summary>
    public string? FooterHtml { get; set; }

    /// <summary>
    /// 上マージン（例: "20mm"）
    /// </summary>
    public string MarginTop { get; set; } = "20mm";

    /// <summary>
    /// 下マージン（例: "20mm"）
    /// </summary>
    public string MarginBottom { get; set; } = "20mm";

    /// <summary>
    /// 左マージン（例: "20mm"）
    /// </summary>
    public string MarginLeft { get; set; } = "20mm";

    /// <summary>
    /// 右マージン（例: "20mm"）
    /// </summary>
    public string MarginRight { get; set; } = "20mm";

    /// <summary>
    /// 背景のグラフィックスを印刷するかどうか
    /// </summary>
    public bool PrintBackground { get; set; } = true;

    /// <summary>
    /// デフォルトのフッター（ページ番号付き）を取得します
    /// </summary>
    public static string DefaultFooter => @"
        <div style='font-size: 10px; text-align: center; width: 100%;'>
            <span class='pageNumber'></span> / <span class='totalPages'></span>
        </div>";

    /// <summary>
    /// A4 サイズの標準オプションを取得します
    /// </summary>
    public static PdfOptions DefaultA4 => new()
    {
        PageSize = "A4",
        MarginTop = "20mm",
        MarginBottom = "20mm",
        MarginLeft = "20mm",
        MarginRight = "20mm",
        PrintBackground = true
    };

    /// <summary>
    /// ページ番号付きの A4 オプションを取得します
    /// </summary>
    public static PdfOptions A4WithPageNumbers => new()
    {
        PageSize = "A4",
        MarginTop = "20mm",
        MarginBottom = "25mm",
        MarginLeft = "20mm",
        MarginRight = "20mm",
        PrintBackground = true,
        FooterHtml = DefaultFooter
    };
}
