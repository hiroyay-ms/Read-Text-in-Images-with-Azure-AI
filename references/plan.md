# 段階的実装計画

画像からテキストを抽出するOCRアプリケーションの詳細な実装計画

**最終更新日**: 2025年12月22日  
**実装状況**: Phase 1-8 完了、Phase 9 一部実装済み

---

## 📊 全体進捗状況

| Phase | タイトル | 状態 | 完了日 |
|-------|---------|------|--------|
| Phase 1 | 環境構築と最小構成 | ✅ 完了 | 2025-12-22 |
| Phase 2 | Azure リソース準備 | ✅ 完了 | 2025-12-22 |
| Phase 3 | モデルクラスの作成 | ✅ 完了 | 2025-12-22 |
| Phase 4 | サービス層の実装 | ✅ 完了 | 2025-12-22 |
| Phase 5 | Program.cs の設定 | ✅ 完了 | 2025-12-22 |
| Phase 6 | 基本UIの実装 | ✅ 完了 | 2025-12-22 |
| Phase 7 | JavaScript の実装 | ✅ 完了 | 2025-12-22 (Phase 6で実装) |
| Phase 8 | 動作テストとデバッグ | ✅ 完了 | 2025-12-22 |
| Phase 9 | UI/UXの改善 | 🔄 一部実装 | - |
| Phase 10 | エラーハンドリングの強化 | 🔜 未実装 | - |
| Phase 11+ | その他の拡張機能 | 🔜 未実装 | - |

---

## 🎯 実装完了済み機能

### コア機能
- ✅ ファイルアップロード（10MB制限、7つの拡張子対応）
- ✅ Azure Document Intelligence による OCR 処理
- ✅ Entra ID 認証（DefaultAzureCredential）
- ✅ 画像プレビュー（即時表示）
- ✅ テキスト抽出結果の表示
- ✅ コピー機能（クリップボード API）
- ✅ エラーハンドリング（CSRF 保護含む）

### UI/UX
- ✅ Bootstrap 5 ベースのレスポンシブデザイン
- ✅ 3つの UI 状態管理（初期、ローディング、結果）
- ✅ ローディングスピナーアニメーション
- ✅ 視覚的フィードバック
- ✅ ナビゲーション（Home, OCR, Privacy）

### アーキテクチャ
- ✅ ディレクトリ構造の整理（Pages/OCR）
- ✅ 依存性注入（DI）
- ✅ サービス層の分離
- ✅ モデルクラスによる型安全性
- ✅ セキュリティ（CSRF、入力検証）

---

## 実装の基本方針

### 粒度の考え方
- **1ステップ = 1-2日で完了可能な作業単位**
- **各ステップで動作確認が可能**
- **依存関係を考慮した順序**
- **常に動作するアプリケーションを維持**

### 開発アプローチ
- **垂直スライス**: 機能を縦に切って、UI → API → サービス → Azure連携まで一気通貫
- **反復的開発**: 基本機能 → 改善 → 拡張の繰り返し
- **継続的テスト**: 各ステップで動作確認とテスト

---

## Phase 1: 環境構築と最小構成 (1-2日) ✅ **完了**

### ゴール
ASP.NET Core Web App プロジェクトを作成し、基本的なプロジェクト構造を整える

### 実装完了日
2025年12月22日

### Step 1.1: プロジェクト作成 (2-3時間) ✅

#### タスク
- [x] ASP.NET Core Web App (Razor Pages) プロジェクトの作成
- [x] ソリューションファイルの作成
- [x] プロジェクト構造の確認

#### コマンド
```bash
# src ディレクトリ作成
mkdir src
cd src

# Razor Pages プロジェクト作成
dotnet new webapp -n WebApp -f net10.0

# ソリューション作成
cd ..
dotnet new sln -n AzureAISample
dotnet sln add src/WebApp/WebApp.csproj

# 動作確認
cd src/WebApp
dotnet run
```

#### 検証
- [x] http://localhost:5269 でアプリが起動する
- [x] デフォルトのページが表示される

---

### Step 1.2: プロジェクト構造の整理 (1-2時間)

#### タスク
- [x] Services ディレクトリの作成
- [x] Models ディレクトリの作成
- [x] 不要なファイルの削除（Privacy.cshtml など）
- [x] appsettings.Development.json の確認（既に存在）
- [x] .gitignore の作成（ルートディレクトリ）

#### ファイル構造
```
src/WebApp/
├── Pages/
│   ├── Index.cshtml
│   ├── Index.cshtml.cs
│   └── Shared/
│       ├── _Layout.cshtml
│       └── _ViewImports.cshtml
├── Services/          # 新規作成
├── Models/            # 新規作成
├── wwwroot/
│   ├── css/
│   ├── js/
│   └── lib/
├── Program.cs
└── appsettings.json
```

#### 検証
- [x] プロジェクトがビルドできる
- [x] dotnet run で起動する
- [x] .gitignore がルートディレクトリに存在する
- [x] appsettings.Development.json が .gitignore に含まれている

---

### Step 1.3: NuGet パッケージのインストール (30分)

#### タスク
- [x] Azure.AI.FormRecognizer のインストール
- [x] Azure.Identity のインストール

#### コマンド
```bash
cd src/WebApp
dotnet add package Azure.AI.FormRecognizer --version 4.1.0
dotnet add package Azure.Identity --version 1.17.1
```

#### 検証
- [x] パッケージが正しくインストールされている
- [x] プロジェクトがビルドできる

---

## Phase 2: Azure リソース準備 (1時間)

### ゴール
Azure Document Intelligence リソースを作成し、接続情報を取得

### Step 2.1: Azure リソース作成

#### タスク
- [x] Azure Portal で Document Intelligence リソースを作成
- [x] エンドポイントを取得
- [x] appsettings.Development.json に設定を保存（Entra ID 認証使用）

#### Azure Portal での作業
1. Azure Portal にログイン
2. 「Document Intelligence」で検索
3. 「作成」をクリック
4. リソースグループ、リージョン、価格レベル（Free F0）を選択
5. 作成完了後、「キーとエンドポイント」からコピー

#### appsettings.Development.json 設定
```json
{
  "DocumentIntelligence_Endpoint": "YOUR_ENDPOINT"
}
```

**認証方式**: Entra ID (Azure AD) 認証を使用。キーは不要。
**注意**: appsettings.Development.json は .gitignore に追加済みで、Git にコミットされません。

#### 検証
- [x] appsettings.Development.json に設定が追加されている
- [x] リソースがAzure Portal で確認できる
- [x] エンドポイントの値が登録されている
- [x] Entra ID 認証を使用（キー不要）

---

## Phase 3: モデルクラスの作成 (2-3時間)

### ゴール
データモデルを定義し、型安全なコードを準備

### Step 3.1: OcrResult モデルの作成 (1時間)

#### ファイル: `Models/OcrResult.cs`

```csharp
namespace WebApp.Models;

public class OcrResult
{
    public bool Success { get; set; }
    public string? ExtractedText { get; set; }
    public List<TextLine> Lines { get; set; } = new();
    public int PageCount { get; set; }
    public string? Language { get; set; }
    public double ConfidenceScore { get; set; }
}

public class TextLine
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
```

#### 検証
- [x] ファイルが正しく作成されている
- [x] プロジェクトがビルドできる

---

### Step 3.2: その他のモデルクラス作成 (1-2時間)

#### ファイル: `Models/OcrError.cs`

```csharp
namespace WebApp.Models;

public class OcrError
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
```

#### ファイル: `Models/FileUploadOptions.cs`

```csharp
namespace WebApp.Models;

public class FileUploadOptions
{
    public int MaxFileSizeMB { get; set; } = 10;
    public List<string> AllowedExtensions { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png", ".pdf", ".tiff", ".tif", ".bmp"
    };
}
```

#### 検証
- [x] すべてのモデルクラスが作成されている
- [x] 名前空間が正しい
- [x] プロジェクトがビルドできる

---

## Phase 4: サービス層の実装 (4-5時間)

### ゴール
Azure Document Intelligence と連携するサービス層を実装

### Step 4.1: インターフェース定義 (30分)

#### ファイル: `Services/IOcrService.cs`

```csharp
using Microsoft.AspNetCore.Http;
using WebApp.Models;

namespace WebApp.Services;

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(IFormFile imageFile);
    Task<bool> ValidateImageAsync(IFormFile imageFile);
}
```

#### 検証
- [x] インターフェースが作成されている
- [x] プロジェクトがビルドできる

---

### Step 4.2: DocumentIntelligenceService 実装 - 基本構造 (2時間)

#### ファイル: `Services/DocumentIntelligenceService.cs`

```csharp
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.AspNetCore.Http;
using WebApp.Models;

namespace WebApp.Services;

public class DocumentIntelligenceService : IOcrService
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly FileUploadOptions _options;

    public DocumentIntelligenceService(
        DocumentAnalysisClient client,
        ILogger<DocumentIntelligenceService> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _options = configuration.GetSection("FileUpload").Get<FileUploadOptions>() 
            ?? new FileUploadOptions();
    }

    public async Task<OcrResult> ExtractTextAsync(IFormFile imageFile)
    {
        // 次のステップで実装
        throw new NotImplementedException();
    }

    public async Task<bool> ValidateImageAsync(IFormFile imageFile)
    {
        // 次のステップで実装
        throw new NotImplementedException();
    }
}
```

#### 検証
- [x] クラスが作成されている
- [x] 依存性注入の準備ができている
- [x] プロジェクトがビルドできる

---

### Step 4.3: ファイル検証の実装 (1時間)

#### タスク
`ValidateImageAsync` メソッドを実装

```csharp
public async Task<bool> ValidateImageAsync(IFormFile imageFile)
{
    if (imageFile == null || imageFile.Length == 0)
    {
        _logger.LogWarning("画像ファイルが選択されていません");
        return false;
    }

    // ファイルサイズチェック
    var maxSizeBytes = _options.MaxFileSizeMB * 1024 * 1024;
    if (imageFile.Length > maxSizeBytes)
    {
        _logger.LogWarning("ファイルサイズが {MaxSize}MB を超えています", _options.MaxFileSizeMB);
        return false;
    }

    // 拡張子チェック
    var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
    if (!_options.AllowedExtensions.Contains(extension))
    {
        _logger.LogWarning("サポートされていないファイル形式です: {Extension}", extension);
        return false;
    }

    return true;
}
```

#### 検証
- [x] メソッドが実装されている
- [x] プロジェクトがビルドできる

---

### Step 4.4: テキスト抽出の実装 (1-2時間) ✅

#### タスク
`ExtractTextAsync` メソッドを実装

**実装内容（2025年12月22日更新）:**

信頼度スコアの計算方法を改善しました。DocumentLine には Confidence プロパティがありませんが、DocumentWord には存在します。

```csharp
public async Task<OcrResult> ExtractTextAsync(IFormFile imageFile)
{
    try
    {
        // ファイル検証
        if (!await ValidateImageAsync(imageFile))
        {
            return new OcrResult { Success = false };
        }

        _logger.LogInformation("OCR処理を開始: {FileName}", imageFile.FileName);

        using var stream = imageFile.OpenReadStream();
        
        // Document Intelligence API 呼び出し
        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            stream
        );

        var result = operation.Value;

        // テキスト行を抽出し、単語の信頼度から行の信頼度を計算
        var lines = new List<TextLine>();
        var allConfidences = new List<double>();

        foreach (var page in result.Pages)
        {
            foreach (var line in page.Lines)
            {
                // 行に含まれる単語の信頼度を取得
                var lineConfidences = new List<double>();
                
                // page.Words から、この行に含まれる単語を特定して信頼度を取得
                if (page.Words != null)
                {
                    foreach (var word in page.Words)
                    {
                        // 単語が行の範囲内にあるかチェック（Spanを使用）
                        if (line.Spans.Any(lineSpan => 
                            word.Span.Index >= lineSpan.Index && 
                            word.Span.Index < lineSpan.Index + lineSpan.Length))
                        {
                            if (word.Confidence > 0)
                            {
                                lineConfidences.Add(word.Confidence);
                            }
                        }
                    }
                }

                // 行の平均信頼度を計算（単語がない場合は0）
                var lineConfidence = lineConfidences.Any() ? lineConfidences.Average() : 0.0;
                
                lines.Add(new TextLine
                {
                    Text = line.Content,
                    Confidence = lineConfidence
                });

                if (lineConfidence > 0)
                {
                    allConfidences.Add(lineConfidence);
                }
            }
        }

        // 全体の平均信頼度を計算
        var overallConfidence = allConfidences.Any() ? allConfidences.Average() : 0.0;

        _logger.LogInformation("OCR処理完了: {LineCount} 行抽出、平均信頼度: {Confidence:P1}", 
            lines.Count, overallConfidence);

        return new OcrResult
        {
            Success = true,
            ExtractedText = result.Content,
            Lines = lines,
            PageCount = result.Pages.Count,
            ConfidenceScore = overallConfidence
        };
    }
    catch (RequestFailedException ex)
    {
        _logger.LogError(ex, "Azure API エラー: {StatusCode}", ex.Status);
        return new OcrResult { Success = false };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "OCR処理中にエラーが発生しました");
        return new OcrResult { Success = false };
    }
}
```

#### 検証
- [x] メソッドが実装されている
- [x] エラーハンドリングが適切
- [x] プロジェクトがビルドできる
- [x] 実際の信頼度スコアが計算される（DocumentWord.Confidence を使用）

**信頼度スコアの計算方法（2025年12月22日更新）:**

Azure.AI.FormRecognizer 4.1.0 では以下のように信頼度を取得します：

1. **DocumentLine**: Confidence プロパティは存在しない
2. **DocumentWord**: Confidence プロパティが存在（float 型）
3. **実装方法**:
   - 各行に含まれる単語（DocumentWord）の Confidence を取得
   - Span.Index と Span.Length を使用して行に含まれる単語を特定
   - 行の信頼度 = その行に含まれる単語の Confidence の平均値
   - 全体の信頼度 = すべての行の信頼度の平均値

この実装により、UI に表示される信頼度スコアが実際の OCR 処理結果を反映するようになりました。

---

## Phase 5: Program.cs の設定 (1時間) ✅ **完了**

### ゴール
依存性注入とミドルウェアを設定

### Step 5.1: Program.cs の更新

#### ファイル: `Program.cs`

**実装済み（Entra ID 認証版）:**

```csharp
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using WebApp.Models;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// FileUploadOptions の登録
builder.Services.Configure<FileUploadOptions>(
    builder.Configuration.GetSection("FileUpload"));

// Azure Document Intelligence クライアントの登録（Entra ID 認証）
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["DocumentIntelligence_Endpoint"];

    if (string.IsNullOrEmpty(endpoint))
    {
        throw new InvalidOperationException(
            "DocumentIntelligence_Endpoint が appsettings.Development.json に設定されていません。");
    }

    // Entra ID (Azure AD) 認証を使用
    var credential = new DefaultAzureCredential();
    return new DocumentAnalysisClient(new Uri(endpoint), credential);
});

// OCR サービスの登録
builder.Services.AddScoped<IOcrService, DocumentIntelligenceService>();

var app = builder.Build();
// ... (以降は既存のミドルウェア設定)
```

#### 検証
- [x] DocumentAnalysisClient が Entra ID 認証で設定されている
- [x] IOcrService が DI コンテナに登録されている
- [x] FileUploadOptions が設定から読み込まれる
- [x] プロジェクトがビルドできる

---

## Phase 4 完了まとめ

### 作成したファイル
1. [Services/IOcrService.cs](src/WebApp/Services/IOcrService.cs) - OCR サービスのインターフェース
2. [Services/DocumentIntelligenceService.cs](src/WebApp/Services/DocumentIntelligenceService.cs) - Azure Document Intelligence 連携サービス実装

### 変更したファイル
1. [Program.cs](src/WebApp/Program.cs) - DI 設定（Entra ID 認証使用）

### 主な実装内容
- **ValidateImageAsync**: ファイルサイズと拡張子のバリデーション
- **ExtractTextAsync**: Azure Document Intelligence を使用したテキスト抽出
- **Entra ID 認証**: DefaultAzureCredential による認証（キー不要）
- **エラーハンドリング**: RequestFailedException と一般的な Exception のキャッチ

---

## 元の Phase 5 の内容（参考用）

### Step 5.1: Program.cs の更新（元の API キー認証版）

#### ファイル: `Program.cs`

```csharp
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages の追加
builder.Services.AddRazorPages();

// FileUploadOptions の登録
builder.Services.Configure<WebApp.Models.FileUploadOptions>(
    builder.Configuration.GetSection("FileUpload"));

// Azure Document Intelligence クライアントの登録
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Azure:DocumentIntelligence:Endpoint"];
    var key = config["Azure:DocumentIntelligence:Key"];

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
    {
        throw new InvalidOperationException(
            "Azure Document Intelligence の設定が見つかりません。User Secrets を設定してください。");
    }

    return new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
});

// OCRサービスの登録
builder.Services.AddScoped<IOcrService, DocumentIntelligenceService>();

var app = builder.Build();

// ミドルウェア設定
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
```

#### 検証
- [ ] Program.cs が更新されている
- [ ] プロジェクトが起動する
- [ ] 依存性注入が正しく設定されている

---

### Step 5.2: appsettings.json の更新

#### ファイル: `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "FileUpload": {
    "MaxFileSizeMB": 10,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".pdf", ".tiff", ".tif", ".bmp"]
  }
}
```

#### 検証
- [x] 設定ファイルが更新されている
- [x] JSON形式が正しい
- [x] プロジェクトがビルドできる

---

## Phase 6: 基本UIの実装 (3-4時間) ✅ **完了**

### ゴール
UIモックに基づいた基本的な画面を作成

### 実装完了日
2025年12月22日

### Step 6.1: Index.cshtml.cs (PageModel) の実装 (1-2時間) ✅

#### ファイル: `Pages/Index.cshtml.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Services;

namespace WebApp.Pages;

public class IndexModel : PageModel
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IOcrService ocrService, ILogger<IndexModel> logger)
    {
        _ocrService = ocrService;
        _logger = logger;
    }

    public void OnGet()
    {
        // ページ初期表示
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile imageFile)
    {
        try
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return BadRequest(new { error = "画像ファイルが選択されていません" });
            }

            _logger.LogInformation("画像アップロード: {FileName} ({Length} bytes)",
                imageFile.FileName, imageFile.Length);

            var result = await _ocrService.ExtractTextAsync(imageFile);

            if (!result.Success)
            {
                return BadRequest(new { error = "テキストの抽出に失敗しました" });
            }

            return new JsonResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR処理中にエラーが発生しました");
            return StatusCode(500, new { error = "処理中にエラーが発生しました" });
        }
    }
}
```

#### 検証
- [x] PageModel が実装されている
- [x] エラーハンドリングが適切
- [x] プロジェクトがビルドできる
- [x] ファイルは Pages/OCR/Index.cshtml.cs に配置されている
- [x] 名前空間は WebApp.Pages.OCR

**実装時の追加変更:**
- ディレクトリ構造の整理: OCR 機能を Pages/OCR サブディレクトリに移動
- 新しいホームページ (Pages/Index.cshtml) を作成し、OCR へのナビゲーションを提供
- _Layout.cshtml にナビゲーションリンクを追加 (Home, OCR, Privacy)

---

### Step 6.2: Index.cshtml の基本UI実装 (2時間) ✅

**実装済みファイル:** [Pages/OCR/Index.cshtml](../src/WebApp/Pages/OCR/Index.cshtml)

#### 実装内容
UIモック (references/image1.png, image2.png) に基づいた完全な UI 実装:

**主要機能:**
1. **ファイルアップロード**
   - File input with accept 属性による拡張子制限
   - Run ボタン (ファイル選択までは無効化)
   - CSRF 保護 (@Html.AntiForgeryToken())

2. **レイアウト**
   - 2カラムレイアウト (左: 画像プレビュー、右: テキスト表示)
   - Bootstrap 5 ベースのレスポンシブデザイン
   - Card コンポーネントによる統一感のあるデザイン

3. **画像プレビュー**
   - ファイル選択時に即座にメインエリアに表示
   - 初期状態: "画像が選択されていません" メッセージ（中央配置）
   - サムネイル機能は削除（メインプレビューエリアのみ）

4. **テキスト表示エリア**
   - 3つの状態を管理:
     * 初期状態: "テキストがまだ抽出されていません" （中央配置）
     * ローディング: スピナーアニメーション（中央配置）
     * 結果表示: 抽出されたテキスト（左上寄せ）
   - flexbox による動的なレイアウト切替
   - コピーボタン（結果表示時）

5. **エラー表示**
   - Bootstrap Alert による視認性の高いエラーメッセージ

#### JavaScript 実装

**実装済みファイル:** [wwwroot/js/ocr-app.js](../src/WebApp/wwwroot/js/ocr-app.js)

**主要機能:**
1. **ファイル選択ハンドリング**
   - 画像プレビューの即時表示
   - Run ボタンの有効/無効切替
   - テキストエリアのリセット

2. **OCR 処理**
   - FormData による multipart/form-data 送信
   - CSRF トークンの自動付与
   - `/OCR/Index?handler=Upload` エンドポイント呼び出し

3. **状態管理**
   - `showLoading()`: テキストエリア内にローディング表示（中央配置）
   - `hideLoading()`: ローディング非表示
   - `resetTextArea()`: 初期状態にリセット（中央配置メッセージ）
   - `displayResult()`: 結果表示（左上寄せ、コンテナのレイアウト変更）

4. **結果表示**
   - 画像プレビューの更新
   - 抽出テキストの表示
   - メタデータ表示（ページ数、行数、信頼度）
   - コピー機能（クリップボード API）

#### 検証
- [x] HTML が正しくレンダリングされる
- [x] Bootstrap のスタイルが適用される
- [x] フォームが表示される
- [x] ファイル選択後に画像がメインプレビューエリアに即座に表示される
- [x] Run ボタンが実装され、ファイル未選択時は無効化される
- [x] テキストエリアが3つの状態を正しく表示する
- [x] OCR 処理が正常に動作する
- [x] CSRF トークンエラーが解決されている
- [x] コピーボタンが動作する

**実装時の UI 改善:**
1. サムネイル削除: 別途サムネイル表示を廃止し、メインプレビューエリアのみに統一
2. テキストエリアの初期メッセージを中央配置（画像エリアと統一感）
3. ローディング表示をテキストエリア内に移動（別エリアを削除）
4. 結果表示時は左上寄せに切り替え（可読性向上）
5. Bootstrap flexbox クラスによる動的なレイアウト制御

---

### Phase 6 完了まとめ

#### 作成・変更したファイル
1. **OCR 機能**
   - [Pages/OCR/Index.cshtml](../src/WebApp/Pages/OCR/Index.cshtml) - OCR UI（元は Pages/Index.cshtml）
   - [Pages/OCR/Index.cshtml.cs](../src/WebApp/Pages/OCR/Index.cshtml.cs) - OCR PageModel（名前空間: WebApp.Pages.OCR）
   - [wwwroot/js/ocr-app.js](../src/WebApp/wwwroot/js/ocr-app.js) - クライアントサイドロジック

2. **ホームページ**
   - [Pages/Index.cshtml](../src/WebApp/Pages/Index.cshtml) - 新規作成（ウェルカムページ）
   - [Pages/Index.cshtml.cs](../src/WebApp/Pages/Index.cshtml.cs) - 新規作成（ホームページ PageModel）

3. **共通レイアウト**
   - [Pages/Shared/_Layout.cshtml](../src/WebApp/Pages/Shared/_Layout.cshtml) - ナビゲーション追加（Home, OCR, Privacy）

#### ディレクトリ構造
```
src/WebApp/
├── Pages/
│   ├── Index.cshtml (新規: ホームページ)
│   ├── Index.cshtml.cs (新規: ホームページ PageModel)
│   ├── OCR/
│   │   ├── Index.cshtml (OCR UI)
│   │   └── Index.cshtml.cs (OCR PageModel, namespace: WebApp.Pages.OCR)
│   ├── Privacy.cshtml
│   ├── Privacy.cshtml.cs
│   └── Shared/
│       ├── _Layout.cshtml (ナビゲーション更新)
│       └── _ViewImports.cshtml
└── wwwroot/
    └── js/
        └── ocr-app.js
```

#### URL 構造
- ホームページ: `http://localhost:5269/`
- OCR 機能: `http://localhost:5269/OCR`
- Privacy: `http://localhost:5269/Privacy`

#### 主な実装内容
- **完全な UI 実装**: ファイル選択、画像プレビュー、OCR 実行、結果表示、コピー機能
- **CSRF 保護**: AntiForgeryToken による安全な POST リクエスト
- **エラーハンドリング**: ユーザーフレンドリーなエラーメッセージ
- **ローディング表示**: テキストエリア内でのスピナーアニメーション
- **レスポンシブデザイン**: Bootstrap 5 による様々なデバイス対応
- **動的な状態管理**: JavaScript による UI 状態の制御
- **ディレクトリ構造の整理**: 将来の機能拡張に備えた階層構造

---

## Phase 7: JavaScript の実装 (3-4時間)

### ゴール
クライアントサイドのインタラクティブ機能を実装

**⚠️ このフェーズは Phase 6 で完全に実装済みです。**

Phase 6 の実装時に、以下の JavaScript 機能をすべて実装済み:
- イベントリスナーの設定
- ファイル選択ハンドリング
- 画像プレビュー表示
- フォーム送信処理
- AntiForgeryToken の自動付与
- OCR 結果の表示
- コピー機能
- エラーハンドリング
- ローディング表示/非表示
- 状態管理ヘルパー関数

**実装済みファイル:** [wwwroot/js/ocr-app.js](../src/WebApp/wwwroot/js/ocr-app.js)

---

## Phase 8: 動作テストとデバッグ (2-3時間) ✅ **完了**

#### ファイル: `Pages/Index.cshtml`

```html
@page
@model IndexModel
@{
    ViewData["Title"] = "OCR Text Extractor";
}

<div class="container mt-5">
    <div class="row justify-content-center">
        <div class="col-md-10">
            <div class="card shadow">
                <div class="card-body p-4">
                    <h1 class="card-title text-center mb-4">画像からテキストを抽出</h1>

                    <!-- ファイルアップロードエリア -->
                    <div class="mb-4">
                        <form id="uploadForm" enctype="multipart/form-data">
                            <div class="input-group">
                                <input type="file" 
                                       class="form-control" 
                                       id="imageFile" 
                                       name="imageFile" 
                                       accept=".jpg,.jpeg,.png,.pdf,.tiff,.tif,.bmp"
                                       required>
                                <button class="btn btn-primary" type="submit" id="submitBtn">
                                    Submit
                                </button>
                            </div>
                        </form>
                    </div>

                    <!-- ローディング表示 -->
                    <div id="loadingArea" class="text-center mb-4" style="display: none;">
                        <div class="spinner-border text-primary" role="status">
                            <span class="visually-hidden">処理中...</span>
                        </div>
                        <p class="mt-2">画像を解析しています...</p>
                    </div>

                    <!-- エラー表示 -->
                    <div id="errorArea" class="alert alert-danger" style="display: none;" role="alert"></div>

                    <!-- 結果表示エリア -->
                    <div id="resultArea" style="display: none;">
                        <div class="row">
                            <!-- 左側: 画像プレビュー -->
                            <div class="col-md-5">
                                <h5>アップロードされた画像</h5>
                                <img id="imagePreview" class="img-fluid border rounded" alt="プレビュー">
                            </div>

                            <!-- 右側: 抽出されたテキスト -->
                            <div class="col-md-7">
                                <h5>抽出されたテキスト</h5>
                                <div class="border rounded p-3" style="min-height: 300px; background-color: #f8f9fa;">
                                    <pre id="extractedText" style="white-space: pre-wrap; word-wrap: break-word;"></pre>
                                </div>
                                <button class="btn btn-secondary mt-2" id="copyBtn">
                                    <i class="bi bi-clipboard"></i> コピー
                                </button>
                            </div>
                        </div>

                        <!-- 詳細情報 -->
                        <div class="mt-3">
                            <small class="text-muted">
                                <span id="pageCount"></span> | 
                                <span id="lineCount"></span> | 
                                平均信頼度: <span id="confidence"></span>
                            </small>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <script src="~/js/ocr-app.js"></script>
}
```

#### 検証
- [x] HTML が正しくレンダリングされる
- [x] Bootstrap のスタイルが適用される
- [x] フォームが表示される
- [x] ファイル選択後にサムネイルが表示される
- [x] Run ボタンが実装されている

---

## Phase 7: JavaScript の実装 (3-4時間)

### ゴール
クライアントサイドのインタラクティブ機能を実装

### Step 7.1: 基本的なJavaScript実装 (2-3時間)

#### ファイル: `wwwroot/js/ocr-app.js`

```javascript
class OcrApp {
    constructor() {
        this.form = document.getElementById('uploadForm');
        this.fileInput = document.getElementById('imageFile');
        this.submitBtn = document.getElementById('submitBtn');
        this.loadingArea = document.getElementById('loadingArea');
        this.errorArea = document.getElementById('errorArea');
        this.resultArea = document.getElementById('resultArea');
        this.imagePreview = document.getElementById('imagePreview');
        this.extractedText = document.getElementById('extractedText');
        this.copyBtn = document.getElementById('copyBtn');
        this.pageCount = document.getElementById('pageCount');
        this.lineCount = document.getElementById('lineCount');
        this.confidence = document.getElementById('confidence');

        this.initializeEventListeners();
    }

    initializeEventListeners() {
        this.form.addEventListener('submit', (e) => this.handleSubmit(e));
        this.fileInput.addEventListener('change', (e) => this.handleFileSelect(e));
        this.copyBtn.addEventListener('click', () => this.copyToClipboard());
    }

    handleFileSelect(event) {
        const file = event.target.files[0];
        if (file) {
            // 画像プレビュー（後のステップで実装）
            console.log('ファイル選択:', file.name);
        }
    }

    async handleSubmit(event) {
        event.preventDefault();

        const file = this.fileInput.files[0];
        if (!file) {
            this.showError('画像ファイルを選択してください');
            return;
        }

        this.showLoading();
        this.hideError();
        this.hideResult();

        try {
            const formData = new FormData();
            formData.append('imageFile', file);

            const response = await fetch('/Index?handler=Upload', {
                method: 'POST',
                body: formData,
                headers: {
                    'RequestVerificationToken': this.getAntiForgeryToken()
                }
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'サーバーエラーが発生しました');
            }

            const result = await response.json();
            this.displayResult(result, file);

        } catch (error) {
            console.error('エラー:', error);
            this.showError(error.message || 'サーバーとの通信に失敗しました');
        } finally {
            this.hideLoading();
        }
    }

    displayResult(result, file) {
        // 画像プレビュー
        const reader = new FileReader();
        reader.onload = (e) => {
            this.imagePreview.src = e.target.result;
        };
        reader.readAsDataURL(file);

        // テキスト表示
        this.extractedText.textContent = result.extractedText || '(テキストが見つかりませんでした)';

        // 詳細情報
        this.pageCount.textContent = `${result.pageCount} ページ`;
        this.lineCount.textContent = `${result.lines.length} 行`;
        this.confidence.textContent = `${(result.confidenceScore * 100).toFixed(1)}%`;

        this.showResult();
    }

    copyToClipboard() {
        const text = this.extractedText.textContent;
        navigator.clipboard.writeText(text).then(() => {
            // コピー成功のフィードバック
            const originalText = this.copyBtn.textContent;
            this.copyBtn.textContent = 'コピーしました!';
            setTimeout(() => {
                this.copyBtn.textContent = originalText;
            }, 2000);
        }).catch(err => {
            console.error('コピーに失敗しました:', err);
            this.showError('クリップボードへのコピーに失敗しました');
        });
    }

    getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    showLoading() {
        this.loadingArea.style.display = 'block';
        this.submitBtn.disabled = true;
    }

    hideLoading() {
        this.loadingArea.style.display = 'none';
        this.submitBtn.disabled = false;
    }

    showError(message) {
        this.errorArea.textContent = message;
        this.errorArea.style.display = 'block';
    }

    hideError() {
        this.errorArea.style.display = 'none';
    }

    showResult() {
        this.resultArea.style.display = 'block';
    }

    hideResult() {
        this.resultArea.style.display = 'none';
    }
}

// ページ読み込み時に初期化
document.addEventListener('DOMContentLoaded', () => {
    new OcrApp();
});
```

#### 検証
- [x] JavaScriptが読み込まれる
- [x] イベントリスナーが動作する
- [x] コンソールエラーがない

---

### Step 7.2: AntiForgeryトークンの追加 (30分) ✅

#### タスク
`Index.cshtml` にAntiForgeryトークンを追加

```html
<!-- フォームの直前に追加 -->
<form id="uploadForm" enctype="multipart/form-data">
    @Html.AntiForgeryToken()
    <!-- 既存のフォーム内容 -->
</form>
```

#### 検証
- [x] トークンがHTMLに出力される
- [x] POSTリクエストが成功する

---

## Phase 8: 動作テストとデバッグ (2-3時間) ✅ **完了**

### ゴール
エンドツーエンドで動作確認し、問題を修正

### 実装完了日
2025年12月22日

### Step 8.1: 統合テスト (1-2時間) ✅

#### テストシナリオ

1. **正常系テスト**
   - [x] JPEG画像をアップロードしてテキストが抽出される
   - [x] PNG画像をアップロードしてテキストが抽出される
   - [x] 抽出結果が正しく表示される
   - [x] コピーボタンが動作する
   - [x] ファイル選択時に画像プレビューが即座に表示される
   - [x] Run ボタンが適切に有効/無効化される

2. **異常系テスト**
   - [x] ファイルを選択せずにSubmitした場合のエラー表示
   - [x] サイズ超過ファイルのエラー表示（10MB制限）
   - [x] 非対応形式ファイルのエラー表示（7つの拡張子のみ許可）
   - [x] ネットワークエラーのハンドリング

#### デバッグのポイント
- [x] ブラウザのコンソールでエラーを確認
- [x] サーバーのログでエラーを確認
- [x] Azure Document Intelligence の呼び出しが成功しているか確認

#### 修正した問題
1. **CSRF トークンエラー**
   - 問題: "Unexpected end of JSON input" エラーが発生
   - 原因: FormData に AntiForgeryToken が含まれていなかった
   - 解決: @Html.AntiForgeryToken() を form に追加し、JavaScript で FormData に含めるように修正

2. **DocumentLine.Confidence プロパティ問題**
   - 問題: Azure.AI.FormRecognizer 4.1.0 では DocumentLine に Confidence プロパティが存在しない
   - 解決: 固定値 1.0 を使用するように DocumentIntelligenceService.cs を修正

3. **Azure.Identity セキュリティ脆弱性**
   - 問題: Azure.Identity 1.10.0 に高・中程度の CVE 存在
   - 解決: Azure.Identity 1.17.1 にアップグレード、すべての脆弱性を解消

---

### Step 8.2: パフォーマンステスト (1時間) ✅

#### 確認事項
- [x] 小さい画像（～1MB）の処理時間 - 正常に動作確認
- [x] 大きい画像（～10MB）の処理時間 - ファイルサイズ制限による検証実施
- [ ] 複数ページPDFの処理時間 - 将来のテスト項目
- [x] メモリ使用量 - 問題なし

#### 改善事項
- ローディング表示をテキストエリア内に配置し、ユーザーエクスペリエンスを向上
- 初期状態とローディング状態で中央配置、結果表示時は左上寄せに動的に変更
- Bootstrap flexbox クラスを活用した効率的な状態管理

---

### Phase 8 完了まとめ

#### 実施したテスト
1. **機能テスト**: すべての基本機能が正常に動作することを確認
2. **エラーハンドリング**: 異常系でのエラー表示が適切に機能することを確認
3. **CSRF 保護**: トークンによる安全な POST リクエストを確認
4. **UI/UX**: ローディング表示、結果表示、エラー表示の動作確認

#### 解決した主要な問題
1. CSRF トークンエラー（Unexpected end of JSON input）
2. DocumentLine.Confidence プロパティの不在
3. Azure.Identity パッケージのセキュリティ脆弱性

#### 現在の動作環境
- URL: http://localhost:5269
- ホームページ: http://localhost:5269/
- OCR 機能: http://localhost:5269/OCR
- 認証方式: Entra ID (DefaultAzureCredential)
- Azure エンドポイント: appsettings.Development.json に設定

---

## Phase 9: UI/UXの改善 (3-4時間) ✅ **完了**

### ゴール
ユーザー体験を向上させる

### 実装完了日
2025年12月22日

### 実装済みの改善項目
1. **レスポンシブデザイン**: Bootstrap 5 による様々なデバイス対応
2. **動的な状態管理**: 3つの UI 状態（初期、ローディング、結果）の適切な表示
3. **視覚的フィードバック**: ローディングスピナー、コピー完了通知
4. **ユーザビリティ**: 画像の即時プレビュー、Run ボタンの状態管理
5. **ドラッグ&ドロップ**: ファイルのドラッグ&ドロップアップロード機能
6. **カスタムCSS**: ドロップエリアのスタイル、レスポンシブ調整、その他の視覚的改善

### Step 9.1: ドラッグ&ドロップ機能 (2時間) ✅

#### 実装内容

1. **HTML**: `Pages/OCR/Index.cshtml` にドロップエリアを追加
```html
<div id="dropArea" class="border border-2 border-dashed rounded p-4 text-center mb-3">
    <p class="mb-2">
        <i class="bi bi-cloud-upload" style="font-size: 2rem;"></i>
    </p>
    <p class="mb-2">ここに画像をドラッグ&ドロップ</p>
    <p class="text-muted mb-3">または</p>
    <div class="input-group">
        <input type="file" class="form-control" id="imageFile" 
               accept=".jpg,.jpeg,.png,.pdf,.tiff,.tif,.bmp">
    </div>
</div>
```

2. **JavaScript**: `ocr-app.js` に `initializeDragAndDrop()` メソッドを実装
   - デフォルトの動作を防止（dragenter, dragover, dragleave, drop）
   - ドラッグ中のスタイル変更（`.drag-over` クラスの追加/削除）
   - ファイルドロップ時の処理（`imageFileInput.files` への設定、`handleFileSelect()` 呼び出し）
   - クリック可能なドロップエリア（`imageFileInput.click()` を呼び出し）

3. **共通化**: ファイル選択処理を `handleFileSelect()` 関数として共通化

#### 検証
- [x] ドラッグ&ドロップが動作する
- [x] ドラッグ中の視覚的フィードバックがある（青いハイライト）
- [x] ホバー時の視覚的フィードバック（背景色変更）
- [x] ドロップエリアをクリックしてもファイル選択可能

---

### Step 9.2: カスタムCSSとレスポンシブデザインの調整 (1-2時間) ✅

#### 実装内容

`wwwroot/css/site.css` に以下のカスタムスタイルを追加:

1. **ドラッグ&ドロップエリア**
   - 背景色とトランジション効果
   - ホバー時の背景色変更
   - ドラッグオーバー時のハイライト（青い背景、太いボーダー）
   - カーソルをポインターに設定

2. **レスポンシブ対応**（モバイル：768px以下）
   - カラムを縦方向に配置
   - 画像プレビューの高さ制限（300px）
   - カードボディのパディング調整

3. **ローディングアニメーション**
   - スピナーサイズを 3rem × 3rem に設定

4. **結果エリア**
   - `#extractedText` に等幅フォント適用（Consolas, Monaco, Courier New）
   - フォントサイズ 14px、行間 1.6

5. **画像プレビュー**
   - コンテナの最小/最大高さ設定
   - 画像のオブジェクトフィット設定

#### 検証
- [x] デスクトップで正しく表示される
- [x] タブレットで正しく表示される（Bootstrap + カスタムCSS）
- [x] スマートフォンで正しく表示される（Bootstrap + カスタムCSS）
- [x] ドラッグ&ドロップエリアのスタイルが適用される
- [x] ホバーエフェクトが動作する
- [x] ドラッグオーバーエフェクトが動作する

---

## Phase 10: エラーハンドリングの強化 (2時間) 🔜 **未実装**

### ゴール
ユーザーフレンドリーなエラーメッセージと回復機能

### Step 10.1: より詳細なエラーハンドリング (1時間)

#### `DocumentIntelligenceService.cs` の更新

```csharp
catch (RequestFailedException ex) when (ex.Status == 429)
{
    _logger.LogWarning("Azure API rate limit exceeded");
    throw new InvalidOperationException("リクエストが多すぎます。しばらく待ってから再試行してください。");
}
catch (RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
{
    _logger.LogError(ex, "Azure API 認証エラー");
    throw new InvalidOperationException("Azure Document Intelligence の認証に失敗しました。設定を確認してください。");
}
catch (RequestFailedException ex)
{
    _logger.LogError(ex, "Azure API エラー: {StatusCode}", ex.Status);
    throw new InvalidOperationException($"OCR処理中にエラーが発生しました（エラーコード: {ex.Status}）");
}
```

#### `Index.cshtml.cs` の更新

```csharp
public async Task<IActionResult> OnPostUploadAsync(IFormFile imageFile)
{
    try
    {
        // 既存のコード
    }
    catch (InvalidOperationException ex)
    {
        _logger.LogWarning(ex, "バリデーションエラー");
        return BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "予期しないエラー");
        return StatusCode(500, new { error = "予期しないエラーが発生しました。しばらく待ってから再試行してください。" });
    }
}
```

#### 検証
- [ ] 適切なエラーメッセージが表示される
- [ ] ログに詳細情報が記録される

---

### Step 10.2: リトライ機能の追加（オプション） (1時間)

#### タスク
簡単なリトライロジックを追加（必要に応じて）

```javascript
// ocr-app.js に追加
async handleSubmitWithRetry(event, retryCount = 0) {
    const maxRetries = 2;
    
    try {
        await this.handleSubmit(event);
    } catch (error) {
        if (retryCount < maxRetries && this.isRetryableError(error)) {
            console.log(`リトライ ${retryCount + 1}/${maxRetries}`);
            await new Promise(resolve => setTimeout(resolve, 1000 * (retryCount + 1)));
            return this.handleSubmitWithRetry(event, retryCount + 1);
        }
        throw error;
    }
}

isRetryableError(error) {
    // ネットワークエラーなどリトライ可能なエラーを判定
    return error.message.includes('network') || error.message.includes('timeout');
}
```

#### 検証
- [ ] 一時的なエラーでリトライされる
- [ ] 最大リトライ回数が守られる

---

## Phase 11: ロギングの設定 (1-2時間)

### ゴール
Serilogを導入して構造化ログを実装

### Step 11.1: Serilogのインストール (30分)

#### コマンド
```bash
cd src/WebApp
dotnet add package Serilog.AspNetCore --version 8.0.0
dotnet add package Serilog.Sinks.Console --version 5.0.0
dotnet add package Serilog.Sinks.File --version 5.0.0
```

#### 検証
- [ ] パッケージがインストールされている

---

### Step 11.2: Serilogの設定 (1時間)

#### `Program.cs` の更新

```csharp
using Serilog;

// プログラムの最初に追加
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ocr-app-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("アプリケーションを起動しています");

    var builder = WebApplication.CreateBuilder(args);

    // Serilogを使用
    builder.Host.UseSerilog();

    // 既存のコード...

    var app = builder.Build();

    // 既存のコード...

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "アプリケーションの起動に失敗しました");
}
finally
{
    Log.CloseAndFlush();
}
```

#### 検証
- [ ] ログがコンソールに出力される
- [ ] ログがファイルに出力される（logs/フォルダ）
- [ ] 構造化ログが記録される

---

## Phase 12: ドキュメント整備 (2時間)

### ゴール
README.mdを更新し、使い方を明確化

### Step 12.1: README.md の作成

#### ファイル: `README.md`

```markdown
# OCR Text Extractor

画像からテキストを抽出するWebアプリケーション

## 機能

- 画像ファイル（JPEG, PNG, PDF, TIFF, BMP）からテキスト抽出
- ドラッグ&ドロップ対応
- リアルタイムプレビュー
- テキストのコピー機能

## 必要な環境

- .NET 10 SDK
- Azure Document Intelligence リソース

## セットアップ

### 1. リポジトリのクローン

\`\`\`bash
git clone <repository-url>
cd Read-Text-in-Images-with-Azure-AI
\`\`\`

### 2. Azure Document Intelligence リソースの作成

1. Azure Portal で Document Intelligence リソースを作成
2. エンドポイントとキーをコピー

### 3. User Secrets の設定

\`\`\`bash
cd src/WebApp
dotnet user-secrets init
dotnet user-secrets set "Azure:DocumentIntelligence:Endpoint" "YOUR_ENDPOINT"
dotnet user-secrets set "Azure:DocumentIntelligence:Key" "YOUR_KEY"
\`\`\`

### 4. 実行

\`\`\`bash
dotnet run
\`\`\`

ブラウザで https://localhost:5001 にアクセス

## 使い方

1. 画像ファイルを選択またはドラッグ&ドロップ
2. "Submit" ボタンをクリック
3. 抽出されたテキストを確認
4. 必要に応じてコピー

## サポートされているファイル形式

- JPEG (.jpg, .jpeg)
- PNG (.png)
- PDF (.pdf)
- TIFF (.tiff, .tif)
- BMP (.bmp)

最大ファイルサイズ: 10MB

## ライセンス

MIT
```

#### 検証
- [ ] README.md が作成されている
- [ ] 手順が明確
- [ ] 実際に手順通りに実行できる

---

## Phase 13: テストプロジェクトの作成（オプション） (3-4時間)

### ゴール
単体テストプロジェクトを作成

### Step 13.1: テストプロジェクトの作成 (1時間)

#### コマンド
```bash
cd src
dotnet new xunit -n WebApp.Tests
cd ..
dotnet sln add src/WebApp.Tests/WebApp.Tests.csproj

cd src/WebApp.Tests
dotnet add reference ../WebApp/WebApp.csproj
dotnet add package Moq --version 4.20.70
dotnet add package Microsoft.AspNetCore.Mvc.Testing
```

#### 検証
- [ ] テストプロジェクトが作成されている
- [ ] 参照が正しく設定されている

---

### Step 13.2: サービスのテスト作成 (2-3時間)

#### ファイル: `src/WebApp.Tests/Services/DocumentIntelligenceServiceTests.cs`

```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WebApp.Services;
using WebApp.Models;

namespace WebApp.Tests.Services;

public class DocumentIntelligenceServiceTests
{
    [Fact]
    public async Task ValidateImageAsync_NullFile_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ValidateImageAsync(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateImageAsync_ValidFile_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var mockFile = CreateMockFormFile("test.jpg", 1024);

        // Act
        var result = await service.ValidateImageAsync(mockFile);

        // Assert
        Assert.True(result);
    }

    // ヘルパーメソッド
    private DocumentIntelligenceService CreateService()
    {
        var mockClient = new Mock<Azure.AI.FormRecognizer.DocumentAnalysis.DocumentAnalysisClient>();
        var mockLogger = new Mock<ILogger<DocumentIntelligenceService>>();
        var configuration = CreateConfiguration();

        return new DocumentIntelligenceService(
            mockClient.Object,
            mockLogger.Object,
            configuration);
    }

    private IConfiguration CreateConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string>
        {
            {"FileUpload:MaxFileSizeMB", "10"},
            {"FileUpload:AllowedExtensions:0", ".jpg"},
            {"FileUpload:AllowedExtensions:1", ".jpeg"},
            {"FileUpload:AllowedExtensions:2", ".png"}
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private IFormFile CreateMockFormFile(string fileName, long length)
    {
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(length);
        return mockFile.Object;
    }
}
```

#### 検証
- [ ] テストが実行できる
- [ ] すべてのテストが成功する

---

## Phase 14: コンテナ化（オプション） (2-3時間)

### ゴール
Dockerコンテナでアプリケーションを実行できるようにする

### Step 14.1: Dockerfile の作成 (1時間)

#### ファイル: `docker/Dockerfile`

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# プロジェクトファイルをコピーして復元
COPY ["src/WebApp/WebApp.csproj", "src/WebApp/"]
RUN dotnet restore "src/WebApp/WebApp.csproj"

# ソースコードをコピーしてビルド
COPY . .
WORKDIR "/src/src/WebApp"
RUN dotnet build "WebApp.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "WebApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WebApp.dll"]
```

#### 検証
- [ ] Dockerfile が作成されている
- [ ] docker build が成功する

---

### Step 14.2: docker-compose.yml の作成 (1時間)

#### ファイル: `docker-compose.yml`

```yaml
version: '3.8'

services:
  webapp:
    build:
      context: .
      dockerfile: docker/Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
      - Azure__DocumentIntelligence__Endpoint=${AZURE_DI_ENDPOINT}
      - Azure__DocumentIntelligence__Key=${AZURE_DI_KEY}
    volumes:
      - ./logs:/app/logs
```

#### ファイル: `.env.example`

```bash
AZURE_DI_ENDPOINT=https://your-resource.cognitiveservices.azure.com/
AZURE_DI_KEY=your-key-here
```

#### ファイル: `.gitignore` に追加

```
.env
logs/
```

#### 検証
- [ ] docker-compose up でアプリが起動する
- [ ] http://localhost:8080 でアクセスできる
- [ ] 環境変数が正しく読み込まれる

---

## 実装チェックリスト

### Phase 1: 環境構築 ✅
- [x] プロジェクト作成
- [x] プロジェクト構造の整理
- [x] NuGet パッケージのインストール

### Phase 2: Azure準備 ✅
- [x] Document Intelligence リソース作成
- [x] appsettings.Development.json にエンドポイントを設定（Entra ID 認証）

### Phase 3: モデル作成 ✅
- [ ] OcrResult クラス
- [ ] その他のモデルクラス

### Phase 4: サービス層 ✅
- [ ] IOcrService インターフェース
- [ ] DocumentIntelligenceService 基本構造
- [ ] ファイル検証の実装
- [ ] テキスト抽出の実装

### Phase 5: Program.cs ✅
- [ ] 依存性注入の設定
- [ ] appsettings.json の更新

### Phase 6: 基本UI ✅
- [ ] PageModel の実装
- [ ] Index.cshtml の実装

### Phase 7: JavaScript ✅
- [ ] 基本的なJavaScript実装
- [ ] AntiForgeryトークンの追加

### Phase 8: テストとデバッグ ✅
- [ ] 統合テスト
- [ ] パフォーマンステスト

### Phase 9: UI/UX改善 🔄
- [ ] ドラッグ&ドロップ機能
- [ ] レスポンシブデザイン

### Phase 10: エラーハンドリング 🔄
- [ ] 詳細なエラーハンドリング
- [ ] リトライ機能（オプション）

### Phase 11: ロギング 🔄
- [ ] Serilog のインストール
- [ ] Serilog の設定

### Phase 12: ドキュメント 🔄
- [ ] README.md の作成

### Phase 13: テスト（オプション） ⭕
- [ ] テストプロジェクトの作成
- [ ] サービスのテスト作成

### Phase 14: コンテナ化（オプション） ⭕
- [ ] Dockerfile の作成
- [ ] docker-compose.yml の作成

---

## 実装のヒント

### デバッグのコツ

1. **ブラウザの開発者ツールを活用**
   - Console でJavaScriptのエラーを確認
   - Network でAPIリクエスト/レスポンスを確認

2. **サーバーログを確認**
   - コンソール出力を確認
   - logs/ フォルダのログファイルを確認

3. **ステップバイステップでテスト**
   - 各Phaseの完了時に動作確認
   - 問題が発生したらすぐに修正

### よくある問題と解決策

| 問題 | 原因 | 解決策 |
|------|------|--------|
| User Secrets が読み込まれない | 設定が正しくない | dotnet user-secrets list で確認 |
| Azure API エラー 401 | キーが間違っている | Azure Portal で再確認 |
| CORS エラー | SPA モードの設定 | 不要（Razor Pagesなので） |
| ファイルアップロードできない | サイズ制限 | appsettings.json で調整 |
| 画像が表示されない | パスが間違っている | ブラウザのコンソールで確認 |

---

## 次のステップ

MVP完成後、以下の拡張機能を検討できます：

### 短期的な改善（1-2週間）
- 複数画像の一括処理
- 処理履歴の表示
- より詳細な結果表示（バウンディングボックス）

### 中期的な改善（1-2ヶ月）
- データベース統合
- ユーザーアカウント機能
- API の公開

### 長期的な改善（3-6ヶ月）
- テーブル認識機能
- フォーム認識機能
- 多言語UI対応

---

**最終更新日**: 2025年12月19日

**推定合計時間**: 
- MVP (Phase 1-8): 約 20-25 時間
- 完全版 (Phase 1-14): 約 35-45 時間
