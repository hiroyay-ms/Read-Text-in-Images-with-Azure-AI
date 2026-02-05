namespace WebApp.Models;

/// <summary>
/// Document Intelligence から抽出された画像情報
/// </summary>
public class ExtractedImage
{
    /// <summary>
    /// 画像 ID（例: image_001, image_002, ...）
    /// </summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>
    /// 画像のバイナリデータ
    /// </summary>
    public byte[] ImageData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// MIME タイプ（例: image/png, image/jpeg）
    /// </summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>
    /// 画像があるページ番号（1-indexed）
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// 画像の幅（ピクセル）
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 画像の高さ（ピクセル）
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 画像の位置情報（バウンディングボックス）
    /// </summary>
    public BoundingBox? Position { get; set; }

    /// <summary>
    /// Blob Storage に保存後の URL
    /// </summary>
    public string? BlobUrl { get; set; }

    /// <summary>
    /// Blob Storage に保存後の Blob 名
    /// </summary>
    public string? BlobName { get; set; }

    /// <summary>
    /// Base64 エンコードされた画像データを取得します
    /// </summary>
    public string GetBase64Data()
    {
        return Convert.ToBase64String(ImageData);
    }

    /// <summary>
    /// データ URI 形式で画像を取得します（Markdown 埋め込み用）
    /// </summary>
    public string GetDataUri()
    {
        return $"data:{ContentType};base64,{GetBase64Data()}";
    }
}

/// <summary>
/// 画像の位置情報（バウンディングボックス）
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// 左上の X 座標
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// 左上の Y 座標
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// 幅
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// 高さ
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// ポリゴン座標（オプション、より詳細な位置情報）
    /// </summary>
    public List<PointF>? Polygon { get; set; }
}

/// <summary>
/// 2D 座標点
/// </summary>
public class PointF
{
    /// <summary>
    /// X 座標
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Y 座標
    /// </summary>
    public float Y { get; set; }

    public PointF() { }

    public PointF(float x, float y)
    {
        X = x;
        Y = y;
    }
}
