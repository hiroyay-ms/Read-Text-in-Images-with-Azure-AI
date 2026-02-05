using Markdig;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using WebApp.Models;

// 型エイリアスで名前衝突を解決
using PuppeteerPdfOptions = PuppeteerSharp.PdfOptions;
using AppPdfOptions = WebApp.Models.PdfOptions;

namespace WebApp.Services;

/// <summary>
/// Markdown から PDF への変換サービスの実装
/// </summary>
public class PdfConverterService : IPdfConverterService
{
    private readonly ILogger<PdfConverterService> _logger;
    private readonly MarkdownPipeline _markdownPipeline;
    private static bool _browserDownloaded = false;
    private static readonly SemaphoreSlim _downloadLock = new(1, 1);

    /// <summary>
    /// PDF 変換用の CSS スタイル
    /// </summary>
    private const string PdfStyles = @"
        @import url('https://fonts.googleapis.com/css2?family=Noto+Sans+JP:wght@400;500;700&display=swap');

        * {
            box-sizing: border-box;
        }

        body {
            font-family: 'Noto Sans JP', 'Segoe UI', 'Yu Gothic', 'Meiryo', sans-serif;
            font-size: 11pt;
            line-height: 1.8;
            color: #333;
            max-width: 100%;
            margin: 0;
            padding: 0;
        }

        h1, h2, h3, h4, h5, h6 {
            font-weight: 700;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
            page-break-after: avoid;
        }

        h1 {
            font-size: 24pt;
            border-bottom: 2px solid #333;
            padding-bottom: 0.3em;
        }

        h2 {
            font-size: 18pt;
            border-bottom: 1px solid #666;
            padding-bottom: 0.2em;
        }

        h3 {
            font-size: 14pt;
        }

        h4 {
            font-size: 12pt;
        }

        p {
            margin: 0.8em 0;
            text-align: justify;
        }

        ul, ol {
            margin: 0.8em 0;
            padding-left: 2em;
        }

        li {
            margin: 0.3em 0;
        }

        table {
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
            page-break-inside: avoid;
        }

        th, td {
            border: 1px solid #ddd;
            padding: 8px 12px;
            text-align: left;
        }

        th {
            background-color: #f5f5f5;
            font-weight: 700;
        }

        tr:nth-child(even) {
            background-color: #fafafa;
        }

        code {
            font-family: 'Consolas', 'Monaco', monospace;
            background-color: #f4f4f4;
            padding: 2px 6px;
            border-radius: 3px;
            font-size: 10pt;
        }

        pre {
            background-color: #f4f4f4;
            padding: 1em;
            border-radius: 5px;
            overflow-x: auto;
            page-break-inside: avoid;
        }

        pre code {
            background-color: transparent;
            padding: 0;
        }

        blockquote {
            border-left: 4px solid #ddd;
            margin: 1em 0;
            padding-left: 1em;
            color: #666;
        }

        img {
            max-width: 100%;
            height: auto;
            display: block;
            margin: 1em auto;
        }

        a {
            color: #0066cc;
            text-decoration: none;
        }

        hr {
            border: none;
            border-top: 1px solid #ddd;
            margin: 2em 0;
        }

        /* ページ区切り */
        .page-break {
            page-break-before: always;
        }
    ";

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

        _logger.LogInformation("PdfConverterService が初期化されました");
    }

    /// <summary>
    /// Markdown を PDF に変換します
    /// </summary>
    public async Task<byte[]> ConvertMarkdownToPdfAsync(
        string markdown,
        AppPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppPdfOptions.DefaultA4;

        try
        {
            _logger.LogInformation("PDF 変換を開始します");

            // ブラウザがダウンロードされていることを確認
            await EnsureBrowserDownloadedAsync(cancellationToken);

            // Markdown → HTML 変換
            var html = ConvertMarkdownToHtml(markdown);

            // 完全な HTML ドキュメントを構築
            var fullHtml = BuildFullHtmlDocument(html, options);

            // Puppeteer でブラウザを起動
            await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu"
                }
            });

            await using var page = await browser.NewPageAsync();

            // HTML をセット
            await page.SetContentAsync(fullHtml, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
            });

            // PDF を生成
            var pdfGenOptions = new PuppeteerPdfOptions
            {
                Format = GetPaperFormat(options.PageSize),
                Landscape = options.LandscapeMode,
                PrintBackground = options.PrintBackground,
                MarginOptions = new MarginOptions
                {
                    Top = options.MarginTop,
                    Bottom = options.MarginBottom,
                    Left = options.MarginLeft,
                    Right = options.MarginRight
                },
                DisplayHeaderFooter = !string.IsNullOrEmpty(options.HeaderHtml) || !string.IsNullOrEmpty(options.FooterHtml),
                HeaderTemplate = options.HeaderHtml ?? "<span></span>",
                FooterTemplate = options.FooterHtml ?? "<span></span>"
            };

            var pdfData = await page.PdfDataAsync(pdfGenOptions);

            _logger.LogInformation("PDF 変換が完了しました。サイズ: {Size} bytes", pdfData.Length);

            return pdfData;
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
    /// Chromium ブラウザがダウンロード済みかどうかを確認し、必要に応じてダウンロードします
    /// </summary>
    public async Task<bool> EnsureBrowserDownloadedAsync(CancellationToken cancellationToken = default)
    {
        if (_browserDownloaded)
        {
            return true;
        }

        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (_browserDownloaded)
            {
                return true;
            }

            _logger.LogInformation("Chromium ブラウザのダウンロードを確認しています...");

            var browserFetcher = new BrowserFetcher();
            var installedBrowser = browserFetcher.GetInstalledBrowsers().FirstOrDefault();

            if (installedBrowser == null)
            {
                _logger.LogInformation("Chromium ブラウザをダウンロードしています...");
                await browserFetcher.DownloadAsync();
                _logger.LogInformation("Chromium ブラウザのダウンロードが完了しました");
            }
            else
            {
                _logger.LogInformation("Chromium ブラウザは既にインストールされています");
            }

            _browserDownloaded = true;
            return true;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    /// 完全な HTML ドキュメントを構築します
    /// </summary>
    private string BuildFullHtmlDocument(string bodyHtml, Models.PdfOptions options)
    {
        var title = options.Title ?? "Document";

        return $@"
<!DOCTYPE html>
<html lang=""ja"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Web.HttpUtility.HtmlEncode(title)}</title>
    <style>
        {PdfStyles}
    </style>
</head>
<body>
    {bodyHtml}
</body>
</html>";
    }

    /// <summary>
    /// ページサイズ名から PaperFormat を取得します
    /// </summary>
    private static PaperFormat GetPaperFormat(string pageSize)
    {
        return pageSize.ToUpperInvariant() switch
        {
            "A4" => PaperFormat.A4,
            "A3" => PaperFormat.A3,
            "A5" => PaperFormat.A5,
            "LETTER" => PaperFormat.Letter,
            "LEGAL" => PaperFormat.Legal,
            "TABLOID" => PaperFormat.Tabloid,
            _ => PaperFormat.A4
        };
    }
}
