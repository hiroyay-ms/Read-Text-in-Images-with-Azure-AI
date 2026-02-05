using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Markdown から PDF への変換サービスの実装（PdfSharpCore + MigraDoc 使用）
/// </summary>
public class PdfConverterService : IPdfConverterService
{
    private readonly ILogger<PdfConverterService> _logger;
    private readonly MarkdownPipeline _markdownPipeline;
    private static bool _fontResolverInitialized = false;
    private static readonly object _fontResolverLock = new();

    public PdfConverterService(ILogger<PdfConverterService> logger)
    {
        _logger = logger;

        // Markdig パイプラインを構築（拡張機能を有効化）
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UsePipeTables()
            .UseGridTables()
            .UseAutoLinks()
            .UseTaskLists()
            .Build();

        // フォントリゾルバーの初期化（1回のみ）
        InitializeFontResolver();

        _logger.LogInformation("PdfConverterService が初期化されました（PdfSharpCore + MigraDoc）");
    }

    /// <summary>
    /// フォントリゾルバーを初期化します
    /// </summary>
    private void InitializeFontResolver()
    {
        lock (_fontResolverLock)
        {
            if (_fontResolverInitialized)
            {
                return;
            }

            try
            {
                // カスタムフォントリゾルバーを設定
                if (GlobalFontSettings.FontResolver == null)
                {
                    GlobalFontSettings.FontResolver = new JapaneseFontResolver();
                }
                _fontResolverInitialized = true;
                _logger.LogInformation("フォントリゾルバーが初期化されました");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "フォントリゾルバーの初期化中にエラーが発生しました");
            }
        }
    }

    /// <summary>
    /// Markdown を PDF に変換します
    /// </summary>
    public Task<byte[]> ConvertMarkdownToPdfAsync(
        string markdown,
        PdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PdfOptions.DefaultA4;

        try
        {
            _logger.LogInformation("PDF 変換を開始します（MigraDoc）");

            // Markdown をパース
            var document = Markdown.Parse(markdown, _markdownPipeline);

            // MigraDoc ドキュメントを作成
            var pdfDocument = CreateMigraDocument(document, options);

            // PDF をレンダリング
            var renderer = new PdfDocumentRenderer(true);
            renderer.Document = pdfDocument;
            renderer.RenderDocument();

            // メモリストリームに出力
            using var stream = new MemoryStream();
            renderer.PdfDocument.Save(stream, false);
            var pdfData = stream.ToArray();

            _logger.LogInformation("PDF 変換が完了しました。サイズ: {Size} bytes", pdfData.Length);

            return Task.FromResult(pdfData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF 変換中にエラーが発生しました");
            throw new InvalidOperationException("PDF 変換に失敗しました。", ex);
        }
    }

    /// <summary>
    /// Markdown を HTML に変換します
    /// </summary>
    public string ConvertMarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, _markdownPipeline);
    }

    /// <summary>
    /// ブラウザダウンロード確認（MigraDoc では不要だが、インターフェース互換性のため）
    /// </summary>
    public Task<bool> EnsureBrowserDownloadedAsync(CancellationToken cancellationToken = default)
    {
        // MigraDoc は外部ブラウザ不要
        return Task.FromResult(true);
    }

    /// <summary>
    /// MigraDoc ドキュメントを作成します
    /// </summary>
    private Document CreateMigraDocument(MarkdownDocument markdownDoc, PdfOptions options)
    {
        var document = new Document();

        // ドキュメント情報
        document.Info.Title = options.Title ?? "Document";
        document.Info.Author = options.Author ?? "Azure AI Sample";

        // スタイルの定義
        DefineStyles(document);

        // セクションの作成
        var section = document.AddSection();

        // ページ設定
        ConfigurePageSetup(section, options);

        // Markdown 要素を MigraDoc 要素に変換
        foreach (var block in markdownDoc)
        {
            ProcessBlock(block, section);
        }

        return document;
    }

    // フォントリゾルバーで使用するフォント名（一貫性を保つため定数化）
    private const string MainFontName = "MultiLangFont";
    private const string CodeFontName = "Consolas";

    /// <summary>
    /// スタイルを定義します
    /// </summary>
    private void DefineStyles(Document document)
    {
        // 基本スタイル（フォントリゾルバーと一致するフォント名を使用）
        var style = document.Styles["Normal"];
        style!.Font.Name = MainFontName;
        style.Font.Size = 11;
        style.ParagraphFormat.LineSpacing = 14;
        style.ParagraphFormat.LineSpacingRule = LineSpacingRule.AtLeast;
        style.ParagraphFormat.SpaceAfter = 6;

        // 見出し1
        style = document.Styles["Heading1"];
        style!.Font.Name = MainFontName;
        style.Font.Size = 22;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 12;
        style.ParagraphFormat.SpaceAfter = 6;
        style.ParagraphFormat.Borders.Bottom.Width = 1;
        style.ParagraphFormat.Borders.Bottom.Color = Colors.DarkGray;

        // 見出し2
        style = document.Styles["Heading2"];
        style!.Font.Name = MainFontName;
        style.Font.Size = 18;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 12;
        style.ParagraphFormat.SpaceAfter = 6;
        style.ParagraphFormat.Borders.Bottom.Width = 0.5;
        style.ParagraphFormat.Borders.Bottom.Color = Colors.Gray;

        // 見出し3
        style = document.Styles["Heading3"];
        style!.Font.Name = MainFontName;
        style.Font.Size = 14;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 10;
        style.ParagraphFormat.SpaceAfter = 4;

        // 見出し4
        style = document.Styles["Heading4"];
        style!.Font.Name = MainFontName;
        style.Font.Size = 12;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 8;
        style.ParagraphFormat.SpaceAfter = 4;

        // コードスタイル
        style = document.Styles.AddStyle("Code", "Normal");
        style.Font.Name = CodeFontName;
        style.Font.Size = 9;
        style.ParagraphFormat.Shading.Color = new Color(245, 245, 245);
        style.ParagraphFormat.LeftIndent = 10;
        style.ParagraphFormat.RightIndent = 10;
        style.ParagraphFormat.SpaceBefore = 6;
        style.ParagraphFormat.SpaceAfter = 6;

        // 引用スタイル
        style = document.Styles.AddStyle("Quote", "Normal");
        style.Font.Italic = true;
        style.Font.Color = new Color(102, 102, 102);
        style.ParagraphFormat.LeftIndent = 20;
        style.ParagraphFormat.Borders.Left.Width = 3;
        style.ParagraphFormat.Borders.Left.Color = new Color(200, 200, 200);
    }

    /// <summary>
    /// ページ設定を構成します
    /// </summary>
    private void ConfigurePageSetup(Section section, PdfOptions options)
    {
        var pageSetup = section.PageSetup;

        // ページサイズ
        switch (options.PageSize.ToUpperInvariant())
        {
            case "A3":
                pageSetup.PageFormat = PageFormat.A3;
                break;
            case "A5":
                pageSetup.PageFormat = PageFormat.A5;
                break;
            case "LETTER":
                pageSetup.PageFormat = PageFormat.Letter;
                break;
            case "LEGAL":
                pageSetup.PageFormat = PageFormat.Legal;
                break;
            default:
                pageSetup.PageFormat = PageFormat.A4;
                break;
        }

        // 向き
        pageSetup.Orientation = options.LandscapeMode ? Orientation.Landscape : Orientation.Portrait;

        // マージン
        pageSetup.TopMargin = ParseMargin(options.MarginTop);
        pageSetup.BottomMargin = ParseMargin(options.MarginBottom);
        pageSetup.LeftMargin = ParseMargin(options.MarginLeft);
        pageSetup.RightMargin = ParseMargin(options.MarginRight);
    }

    /// <summary>
    /// マージン文字列をパースします（例: "20mm" → Unit）
    /// </summary>
    private Unit ParseMargin(string margin)
    {
        if (string.IsNullOrWhiteSpace(margin))
        {
            return Unit.FromMillimeter(20);
        }

        margin = margin.Trim().ToLowerInvariant();

        if (margin.EndsWith("mm"))
        {
            if (double.TryParse(margin[..^2], out var mm))
            {
                return Unit.FromMillimeter(mm);
            }
        }
        else if (margin.EndsWith("cm"))
        {
            if (double.TryParse(margin[..^2], out var cm))
            {
                return Unit.FromCentimeter(cm);
            }
        }
        else if (margin.EndsWith("in"))
        {
            if (double.TryParse(margin[..^2], out var inch))
            {
                return Unit.FromInch(inch);
            }
        }
        else if (margin.EndsWith("pt"))
        {
            if (double.TryParse(margin[..^2], out var pt))
            {
                return Unit.FromPoint(pt);
            }
        }
        else if (double.TryParse(margin, out var value))
        {
            return Unit.FromPoint(value);
        }

        return Unit.FromMillimeter(20);
    }

    /// <summary>
    /// Markdown ブロック要素を処理します
    /// </summary>
    private void ProcessBlock(Block block, Section section)
    {
        switch (block)
        {
            case HeadingBlock heading:
                ProcessHeading(heading, section);
                break;

            case ParagraphBlock paragraph:
                ProcessParagraph(paragraph, section);
                break;

            case ListBlock list:
                ProcessList(list, section);
                break;

            case FencedCodeBlock codeBlock:
                ProcessCodeBlock(codeBlock, section);
                break;

            case CodeBlock codeBlock:
                ProcessCodeBlock(codeBlock, section);
                break;

            case QuoteBlock quote:
                ProcessQuote(quote, section);
                break;

            case ThematicBreakBlock:
                ProcessThematicBreak(section);
                break;

            case Markdig.Extensions.Tables.Table table:
                ProcessTable(table, section);
                break;

            default:
                // その他のブロックは段落として処理
                var text = block.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    section.AddParagraph(text);
                }
                break;
        }
    }

    /// <summary>
    /// 見出しを処理します
    /// </summary>
    private void ProcessHeading(HeadingBlock heading, Section section)
    {
        var styleName = heading.Level switch
        {
            1 => "Heading1",
            2 => "Heading2",
            3 => "Heading3",
            4 => "Heading4",
            _ => "Heading4"
        };

        var paragraph = section.AddParagraph();
        paragraph.Style = styleName;
        ProcessInlines(heading.Inline, paragraph);
    }

    /// <summary>
    /// 段落を処理します
    /// </summary>
    private void ProcessParagraph(ParagraphBlock paragraphBlock, Section section)
    {
        var paragraph = section.AddParagraph();
        paragraph.Style = "Normal";
        ProcessInlines(paragraphBlock.Inline, paragraph);
    }

    /// <summary>
    /// リストを処理します
    /// </summary>
    private void ProcessList(ListBlock list, Section section, int level = 0)
    {
        var isOrdered = list.IsOrdered;
        var counter = 1;

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var indent = 15 * (level + 1);
                
                foreach (var block in listItem)
                {
                    if (block is ParagraphBlock paragraphBlock)
                    {
                        var paragraph = section.AddParagraph();
                        paragraph.Style = "Normal";
                        paragraph.Format.LeftIndent = Unit.FromPoint(indent);

                        // リストマーカー
                        var marker = isOrdered ? $"{counter}. " : "• ";
                        paragraph.AddText(marker);
                        
                        ProcessInlines(paragraphBlock.Inline, paragraph);
                        counter++;
                    }
                    else if (block is ListBlock nestedList)
                    {
                        ProcessList(nestedList, section, level + 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// コードブロックを処理します
    /// </summary>
    private void ProcessCodeBlock(LeafBlock codeBlock, Section section)
    {
        var paragraph = section.AddParagraph();
        paragraph.Style = "Code";

        var lines = codeBlock.Lines.ToString();
        paragraph.AddText(lines);
    }

    /// <summary>
    /// 引用を処理します
    /// </summary>
    private void ProcessQuote(QuoteBlock quote, Section section)
    {
        foreach (var block in quote)
        {
            if (block is ParagraphBlock paragraphBlock)
            {
                var paragraph = section.AddParagraph();
                paragraph.Style = "Quote";
                ProcessInlines(paragraphBlock.Inline, paragraph);
            }
        }
    }

    /// <summary>
    /// 水平線を処理します
    /// </summary>
    private void ProcessThematicBreak(Section section)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.Borders.Bottom.Width = 1;
        paragraph.Format.Borders.Bottom.Color = Colors.Gray;
        paragraph.Format.SpaceBefore = 12;
        paragraph.Format.SpaceAfter = 12;
    }

    /// <summary>
    /// テーブルを処理します
    /// </summary>
    private void ProcessTable(Markdig.Extensions.Tables.Table markdownTable, Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = new Color(200, 200, 200);

        // 列数を取得
        var columnCount = 0;
        foreach (var row in markdownTable)
        {
            if (row is Markdig.Extensions.Tables.TableRow tableRow)
            {
                columnCount = Math.Max(columnCount, tableRow.Count);
            }
        }

        if (columnCount == 0) return;

        // 列を追加（均等幅）
        var columnWidth = Unit.FromCentimeter(16.0 / columnCount);
        for (int i = 0; i < columnCount; i++)
        {
            var column = table.AddColumn(columnWidth);
        }

        // 行を処理
        var isFirstRow = true;
        foreach (var row in markdownTable)
        {
            if (row is Markdig.Extensions.Tables.TableRow tableRow)
            {
                var pdfRow = table.AddRow();
                
                var cellIndex = 0;
                foreach (var cell in tableRow)
                {
                    if (cell is Markdig.Extensions.Tables.TableCell tableCell && cellIndex < columnCount)
                    {
                        var pdfCell = pdfRow.Cells[cellIndex];
                        
                        // ヘッダー行のスタイル（最初の行をヘッダーとして扱う）
                        if (isFirstRow)
                        {
                            pdfCell.Shading.Color = new Color(240, 240, 240);
                            pdfCell.Format.Font.Bold = true;
                        }

                        pdfCell.VerticalAlignment = VerticalAlignment.Center;
                        var paragraph = pdfCell.AddParagraph();
                        
                        foreach (var block in tableCell)
                        {
                            if (block is ParagraphBlock paragraphBlock)
                            {
                                ProcessInlines(paragraphBlock.Inline, paragraph);
                            }
                        }
                        
                        cellIndex++;
                    }
                }
                
                isFirstRow = false;
            }
        }
    }

    /// <summary>
    /// インライン要素を処理します
    /// </summary>
    private void ProcessInlines(ContainerInline? container, Paragraph paragraph)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    paragraph.AddText(literal.Content.ToString());
                    break;

                case EmphasisInline emphasis:
                    var emphasisText = GetInlineText(emphasis);
                    var formattedText = paragraph.AddFormattedText(emphasisText);
                    if (emphasis.DelimiterCount == 2)
                    {
                        formattedText.Bold = true;
                    }
                    else
                    {
                        formattedText.Italic = true;
                    }
                    break;

                case CodeInline code:
                    var codeText = paragraph.AddFormattedText(code.Content);
                    codeText.Font.Name = "Consolas";
                    codeText.Font.Size = 9;
                    // MigraDocCore では FormattedText に Shading プロパティがないため、背景色なし
                    break;

                case LinkInline link:
                    var linkText = GetInlineText(link);
                    var hyperlink = paragraph.AddHyperlink(link.Url ?? "", HyperlinkType.Web);
                    var linkFormattedText = hyperlink.AddFormattedText(linkText);
                    linkFormattedText.Color = new Color(0, 102, 204);
                    linkFormattedText.Underline = Underline.Single;
                    break;

                case LineBreakInline:
                    paragraph.AddLineBreak();
                    break;

                case HtmlInline:
                    // HTML インラインは無視
                    break;

                default:
                    var text = inline.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        paragraph.AddText(text);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// インライン要素からテキストを取得します
    /// </summary>
    private string GetInlineText(ContainerInline container)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
            {
                sb.Append(literal.Content.ToString());
            }
            else if (inline is ContainerInline nestedContainer)
            {
                sb.Append(GetInlineText(nestedContainer));
            }
            else
            {
                sb.Append(inline.ToString());
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// 多言語対応フォントリゾルバー
/// Windows に標準でインストールされているフォントを使用して、
/// 日本語、中国語、韓国語などの多言語に対応します。
/// PdfSharpCore が TTC ファイルを直接扱えないため、TTF を抽出して使用します。
/// </summary>
public class JapaneseFontResolver : IFontResolver
{
    private static readonly Dictionary<string, byte[]?> _fontCache = new();
    private static readonly object _cacheLock = new();
    private static string? _loadedFontPath = null;

    public string DefaultFontName => "MultiLangFont";

    public byte[]? GetFont(string faceName)
    {
        lock (_cacheLock)
        {
            if (_fontCache.TryGetValue(faceName, out var cachedFont))
            {
                return cachedFont;
            }
        }

        byte[]? fontData = null;

        // faceName に基づいてフォントを選択
        var normalizedName = faceName.ToLowerInvariant();

        if (normalizedName.Contains("consolas"))
        {
            fontData = LoadFontFromFile(@"C:\Windows\Fonts\consola.ttf");
            if (fontData == null)
            {
                fontData = LoadFontFromFile(@"C:\Windows\Fonts\cour.ttf"); // Courier New
            }
            if (fontData == null)
            {
                fontData = LoadFontFromFile(@"C:\Windows\Fonts\arial.ttf"); // フォールバック
            }
        }
        else
        {
            // 日本語対応フォント - TTF ファイルを優先（TTC より確実）
            var fontPaths = new (string path, bool isTtc, int ttcIndex)[]
            {
                // 1. TTF ファイル（最も確実）
                (@"C:\Windows\Fonts\msgothic.ttf", false, 0),   // MS Gothic TTF版
                (@"C:\Windows\Fonts\meiryo.ttf", false, 0),     // メイリオ TTF版
                (@"C:\Windows\Fonts\yugothic.ttf", false, 0),   // 游ゴシック TTF版
                (@"C:\Windows\Fonts\arial.ttf", false, 0),      // Arial（フォールバック）
                
                // 2. TTC ファイル（TTF がない場合）
                (@"C:\Windows\Fonts\msgothic.ttc", true, 0),    // MS Gothic
                (@"C:\Windows\Fonts\meiryo.ttc", true, 0),      // メイリオ
                (@"C:\Windows\Fonts\YuGothR.ttc", true, 0),     // 游ゴシック Regular
                (@"C:\Windows\Fonts\YuGothM.ttc", true, 0),     // 游ゴシック Medium
                
                // 3. その他の多言語フォント
                (@"C:\Windows\Fonts\seguisym.ttf", false, 0),   // Segoe UI Symbol
                (@"C:\Windows\Fonts\arialuni.ttf", false, 0),   // Arial Unicode MS
            };

            foreach (var (path, isTtc, ttcIndex) in fontPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    if (isTtc)
                    {
                        fontData = LoadFontFromTtc(path, ttcIndex);
                    }
                    else
                    {
                        fontData = LoadFontFromFile(path);
                    }
                    
                    if (fontData != null && fontData.Length > 0)
                    {
                        _loadedFontPath = path;
                        System.Diagnostics.Debug.WriteLine($"[FontResolver] フォント読み込み成功: {path} ({fontData.Length} bytes)");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FontResolver] フォント読み込み失敗: {path} - {ex.Message}");
                }
            }
            
            // 最終フォールバック: 何も見つからない場合は Arial を使用
            if (fontData == null)
            {
                fontData = LoadFontFromFile(@"C:\Windows\Fonts\arial.ttf");
                if (fontData != null)
                {
                    _loadedFontPath = @"C:\Windows\Fonts\arial.ttf";
                    System.Diagnostics.Debug.WriteLine($"[FontResolver] フォールバック: arial.ttf を使用");
                }
            }
        }

        lock (_cacheLock)
        {
            _fontCache[faceName] = fontData;
        }

        return fontData;
    }

    /// <summary>
    /// TTF ファイルを読み込みます
    /// </summary>
    private byte[]? LoadFontFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var data = File.ReadAllBytes(path);
            // 有効な TTF かどうか簡易チェック（最初の4バイト）
            if (data.Length >= 4)
            {
                var tag = System.Text.Encoding.ASCII.GetString(data, 0, 4);
                // TTF: 0x00010000 または "OTTO" (OpenType) または "true" (TrueType)
                if (data[0] == 0x00 && data[1] == 0x01 && data[2] == 0x00 && data[3] == 0x00)
                {
                    return data;
                }
                if (tag == "OTTO" || tag == "true")
                {
                    return data;
                }
                // TTCでないことを確認
                if (tag != "ttcf")
                {
                    return data; // それでも使用を試みる
                }
            }
            return data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// TTC ファイルから指定されたインデックスのフォントを抽出します
    /// </summary>
    private byte[]? LoadFontFromTtc(string path, int fontIndex)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var ttcData = File.ReadAllBytes(path);
            var ttfData = ExtractTtfFromTtc(ttcData, fontIndex);
            
            if (ttfData != null && ttfData.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FontResolver] TTC から TTF 抽出成功: {path} index={fontIndex} ({ttfData.Length} bytes)");
                return ttfData;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FontResolver] TTC 読み込みエラー: {path} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// TTC バイナリから TTF を抽出します
    /// </summary>
    private byte[]? ExtractTtfFromTtc(byte[] ttcData, int fontIndex)
    {
        try
        {
            // TTC ヘッダーを読み込み
            if (ttcData.Length < 12)
            {
                return null;
            }

            // TTC タグを確認 ("ttcf")
            var tag = System.Text.Encoding.ASCII.GetString(ttcData, 0, 4);
            if (tag != "ttcf")
            {
                // TTC ではなく TTF の可能性 - そのまま返す
                return ttcData;
            }

            // フォント数を取得（オフセット 8、4バイト、ビッグエンディアン）
            var numFonts = ReadBigEndianUInt32(ttcData, 8);
            if (fontIndex >= numFonts)
            {
                fontIndex = 0;
            }

            // 指定されたフォントのオフセットを取得
            var offsetTableOffset = ReadBigEndianUInt32(ttcData, 12 + (fontIndex * 4));

            // TTF データを構築
            return BuildTtfFromOffset(ttcData, (int)offsetTableOffset);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FontResolver] TTF 抽出エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// TTC 内のオフセットから TTF を構築します
    /// </summary>
    private byte[]? BuildTtfFromOffset(byte[] ttcData, int offsetTableOffset)
    {
        try
        {
            // オフセットテーブルを読み込み
            if (ttcData.Length < offsetTableOffset + 12)
            {
                return null;
            }

            // テーブル数を取得
            var numTables = ReadBigEndianUInt16(ttcData, offsetTableOffset + 4);

            // 各テーブルの情報を収集
            var tables = new List<(string tag, uint checksum, uint offset, uint length)>();
            var tableRecordOffset = offsetTableOffset + 12;

            for (int i = 0; i < numTables; i++)
            {
                var recordOffset = tableRecordOffset + (i * 16);
                if (ttcData.Length < recordOffset + 16)
                {
                    return null;
                }

                var tableTag = System.Text.Encoding.ASCII.GetString(ttcData, recordOffset, 4);
                var checksum = ReadBigEndianUInt32(ttcData, recordOffset + 4);
                var offset = ReadBigEndianUInt32(ttcData, recordOffset + 8);
                var length = ReadBigEndianUInt32(ttcData, recordOffset + 12);

                tables.Add((tableTag, checksum, offset, length));
            }

            // 新しい TTF を構築
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // オフセットテーブルを書き込み
            writer.Write(ttcData, offsetTableOffset, 12);

            // 新しいテーブルレコードのオフセットを計算
            var newDataOffset = (uint)(12 + (numTables * 16));
            newDataOffset = (newDataOffset + 3) & ~3u; // 4バイト境界に揃える

            // テーブルレコードを書き込み
            var currentOffset = newDataOffset;
            var tableData = new List<byte[]>();

            foreach (var (tag, checksum, offset, length) in tables)
            {
                // タグを書き込み
                writer.Write(System.Text.Encoding.ASCII.GetBytes(tag));
                // チェックサムを書き込み
                WriteBigEndianUInt32(writer, checksum);
                // 新しいオフセットを書き込み
                WriteBigEndianUInt32(writer, currentOffset);
                // 長さを書き込み
                WriteBigEndianUInt32(writer, length);

                // テーブルデータを抽出
                var data = new byte[length];
                Array.Copy(ttcData, offset, data, 0, length);
                tableData.Add(data);

                // 次のオフセットを計算（4バイト境界）
                currentOffset += length;
                currentOffset = (currentOffset + 3) & ~3u;
            }

            // パディングを追加
            var padding = newDataOffset - (12 + (numTables * 16));
            for (int i = 0; i < padding; i++)
            {
                writer.Write((byte)0);
            }

            // テーブルデータを書き込み
            foreach (var data in tableData)
            {
                writer.Write(data);
                // 4バイト境界にパディング
                var tablePadding = (4 - (data.Length % 4)) % 4;
                for (int i = 0; i < tablePadding; i++)
                {
                    writer.Write((byte)0);
                }
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static uint ReadBigEndianUInt32(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    private static ushort ReadBigEndianUInt16(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static void WriteBigEndianUInt32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // フォントファミリー名を正規化
        var normalizedName = familyName.ToLowerInvariant();

        // Consolas（コード用）
        if (normalizedName.Contains("consolas") || normalizedName.Contains("mono") || normalizedName.Contains("courier"))
        {
            return new FontResolverInfo("Consolas", isBold, isItalic);
        }

        // すべてのフォントを多言語対応フォントにマッピング
        return new FontResolverInfo("MultiLangFont", isBold, isItalic);
    }
}
