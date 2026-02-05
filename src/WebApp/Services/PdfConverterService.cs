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

    /// <summary>
    /// スタイルを定義します
    /// </summary>
    private void DefineStyles(Document document)
    {
        // 基本スタイル
        var style = document.Styles["Normal"];
        style!.Font.Name = "Yu Gothic";
        style.Font.Size = 11;
        style.ParagraphFormat.LineSpacing = 14;
        style.ParagraphFormat.LineSpacingRule = LineSpacingRule.AtLeast;
        style.ParagraphFormat.SpaceAfter = 6;

        // 見出し1
        style = document.Styles["Heading1"];
        style!.Font.Name = "Yu Gothic";
        style.Font.Size = 22;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 12;
        style.ParagraphFormat.SpaceAfter = 6;
        style.ParagraphFormat.Borders.Bottom.Width = 1;
        style.ParagraphFormat.Borders.Bottom.Color = Colors.DarkGray;

        // 見出し2
        style = document.Styles["Heading2"];
        style!.Font.Name = "Yu Gothic";
        style.Font.Size = 18;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 12;
        style.ParagraphFormat.SpaceAfter = 6;
        style.ParagraphFormat.Borders.Bottom.Width = 0.5;
        style.ParagraphFormat.Borders.Bottom.Color = Colors.Gray;

        // 見出し3
        style = document.Styles["Heading3"];
        style!.Font.Name = "Yu Gothic";
        style.Font.Size = 14;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 10;
        style.ParagraphFormat.SpaceAfter = 4;

        // 見出し4
        style = document.Styles["Heading4"];
        style!.Font.Name = "Yu Gothic";
        style.Font.Size = 12;
        style.Font.Bold = true;
        style.ParagraphFormat.SpaceBefore = 8;
        style.ParagraphFormat.SpaceAfter = 4;

        // コードスタイル
        style = document.Styles.AddStyle("Code", "Normal");
        style.Font.Name = "Consolas";
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
/// 日本語フォントリゾルバー
/// </summary>
public class JapaneseFontResolver : IFontResolver
{
    public string DefaultFontName => "Yu Gothic";

    public byte[]? GetFont(string faceName)
    {
        // Windows の游ゴシックフォントを使用
        var fontPaths = new[]
        {
            @"C:\Windows\Fonts\YuGothM.ttc",  // 游ゴシック Medium
            @"C:\Windows\Fonts\YuGothB.ttc",  // 游ゴシック Bold
            @"C:\Windows\Fonts\meiryo.ttc",   // メイリオ
            @"C:\Windows\Fonts\msgothic.ttc", // MS Gothic
            @"C:\Windows\Fonts\arial.ttf",    // Arial（フォールバック）
        };

        foreach (var path in fontPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // フォントファミリー名を正規化
        var normalizedName = familyName.ToLowerInvariant();
        
        // 日本語フォントのマッピング
        if (normalizedName.Contains("gothic") || 
            normalizedName.Contains("ゴシック") ||
            normalizedName.Contains("yu gothic"))
        {
            return new FontResolverInfo("YuGothic", isBold, isItalic);
        }
        
        if (normalizedName.Contains("meiryo") || normalizedName.Contains("メイリオ"))
        {
            return new FontResolverInfo("Meiryo", isBold, isItalic);
        }

        if (normalizedName.Contains("consolas") || normalizedName.Contains("mono"))
        {
            return new FontResolverInfo("Consolas", isBold, isItalic);
        }

        // デフォルトは游ゴシック
        return new FontResolverInfo("YuGothic", isBold, isItalic);
    }
}
