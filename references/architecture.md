# アプリケーションアーキテクチャ

## プロジェクト概要

アップロードされた画像から印刷または手書きのテキストを抽出するWebアプリケーション

### 技術スタック
- **フロントエンド**: ASP.NET Core Web App (Razor Pages) + JavaScript
- **バックエンド**: .NET 10 (C#)
- **OCRサービス**: Azure Document Intelligence
- **デプロイ**: Docker コンテナ

---

## プロジェクト構造

```
Read-Text-in-Images-with-Azure-AI/
├── src/
│   └── WebApp/                    # ASP.NET Core Web App (Razor Pages)
│       ├── Pages/                 # Razor Pages
│       │   ├── Index.cshtml       # ホーム画面
│       │   ├── Index.cshtml.cs
│       │   ├── Error.cshtml       # エラーページ
│       │   ├── Error.cshtml.cs
│       │   ├── OCR/               # Document Intelligence 画面
│       │   │   ├── Index.cshtml
│       │   │   └── Index.cshtml.cs
│       │   ├── GPT/               # GPT-4o Vision 画面
│       │   │   ├── Index.cshtml
│       │   │   └── Index.cshtml.cs
│       │   ├── Shared/            # 共有レイアウト
│       │   │   ├── _Layout.cshtml
│       │   │   ├── _Layout.cshtml.css
│       │   │   └── _ValidationScriptsPartial.cshtml
│       │   ├── _ViewImports.cshtml
│       │   └── _ViewStart.cshtml
│       ├── Services/              # ビジネスロジック層
│       │   ├── IOcrService.cs
│       │   ├── DocumentIntelligenceService.cs
│       │   ├── IGptVisionService.cs
│       │   ├── OpenAIVisionService.cs
│       │   └── HealthChecks/      # ヘルスチェック実装
│       │       ├── DocumentIntelligenceHealthCheck.cs
│       │       └── AzureOpenAIHealthCheck.cs
│       ├── Models/                # データモデル、DTOs
│       │   ├── OcrResult.cs       # Document Intelligence 結果
│       │   ├── VisionOcrResult.cs # GPT-4o Vision 結果
│       │   ├── OcrError.cs
│       │   └── FileUploadOptions.cs
│       ├── wwwroot/               # 静的ファイル
│       │   ├── js/
│       │   │   ├── ocr-app.js     # Document Intelligence UI
│       │   │   ├── gpt-vision.js  # GPT-4o Vision UI
│       │   │   └── site.js
│       │   ├── css/
│       │   │   └── site.css
│       │   └── lib/               # クライアントライブラリ
│       │       ├── bootstrap/
│       │       ├── jquery/
│       │       └── jquery-validation/
│       ├── Properties/
│       │   └── launchSettings.json
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── WebApp.csproj
├── AzureAISample.sln
├── README.md
└── references/
    ├── architecture.md            # このファイル
    ├── features.md                # 機能一覧
    ├── implementation-plan.md     # 実装計画
    ├── plan.md                    # 段階的実装計画
    ├── plan_add-on-1.md           # 追加機能1 (GPT-4o)
    ├── plan_add-on-2.md           # 追加機能2 (OpenTelemetry)
    ├── image1.png                 # UIモック (アップロード前)
    └── image2.png                 # UIモック (アップロード後)
```

---

## アーキテクチャ層

### 1. プレゼンテーション層 (Razor Pages)

#### Pages/Index.cshtml.cs (PageModel)
```csharp
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
            
            var result = await _ocrService.ExtractTextAsync(imageFile);
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

#### Pages/Index.cshtml
- 画像アップロードUI
- 抽出結果の表示エリア
- JavaScript連携

### 2. サービス層

#### IOcrService インターフェース
```csharp
public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(IFormFile imageFile);
    Task<bool> ValidateImageAsync(IFormFile imageFile);
}
```

#### DocumentIntelligenceService 実装
```csharp
public class DocumentIntelligenceService : IOcrService
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    
    public async Task<OcrResult> ExtractTextAsync(IFormFile imageFile)
    {
        // 1. 画像検証
        // 2. Azure Document Intelligence API呼び出し
        // 3. Read モデルでテキスト抽出
        // 4. 結果の整形と返却
    }
    
    public async Task<bool> ValidateImageAsync(IFormFile imageFile)
    {
        // ファイルサイズチェック
        // ファイル形式チェック (JPEG, PNG, PDF, TIFF)
        // その他検証ロジック
    }
}
```

### 3. モデル層

#### OcrResult.cs
```csharp
public class OcrResult
{
    public bool Success { get; set; }
    public string ExtractedText { get; set; }
    public List<TextLine> Lines { get; set; }
    public int PageCount { get; set; }
    public string Language { get; set; }
    public double ConfidenceScore { get; set; }
}

public class TextLine
{
    public string Text { get; set; }
    public BoundingBox BoundingBox { get; set; }
    public double Confidence { get; set; }
}
```

#### ImageUploadRequest.cs
```csharp
public class ImageUploadRequest
{
    public IFormFile File { get; set; }
    public ImageProcessingOptions Options { get; set; }
}

public class ImageProcessingOptions
{
    public bool DetectHandwriting { get; set; } = true;
    public string Language { get; set; } = "ja";
}
```

#### OcrError.cs
```csharp
public class OcrError
{
    public string ErrorCode { get; set; }
    public string Message { get; set; }
    public string Details { get; set; }
}
```

---

## 技術詳細

### フロントエンド (JavaScript)

#### wwwroot/js/ocr-app.js
```javascript
// 主要機能
class OcrApp {
    constructor() {
        this.initializeEventListeners();
    }
    
    initializeEventListeners() {
        // ドラッグ&ドロップ
        // ファイル選択
        // Submit ボタン
    }
    
    async submitImage(formData) {
        const response = await fetch('/Index?handler=Upload', {
            method: 'POST',
            body: formData,
            headers: {
                'RequestVerificationToken': this.getAntiForgeryToken()
            }
        });
        
        return await response.json();
    }
    
    displayResults(result) {
        // 抽出テキストの表示
        // コピー機能の有効化
    }
    
    showError(error) {
        // エラーメッセージの表示
    }
}
```

#### 機能一覧
- 画像プレビュー表示
- ドラッグ&ドロップ対応
- 進行状況の表示
- 結果のリアルタイム表示
- テキストのコピー機能
- エラーハンドリング

### バックエンド (.NET 10)

#### Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

// サービス登録
builder.Services.AddRazorPages();

// OCRサービスの登録
builder.Services.AddScoped<IOcrService, DocumentIntelligenceService>();

// Azure Document Intelligence クライアント
builder.Services.AddSingleton(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Azure:DocumentIntelligence:Endpoint"];
    var key = config["Azure:DocumentIntelligence:Key"];
    
    return new DocumentAnalysisClient(
        new Uri(endpoint), 
        new AzureKeyCredential(key)
    );
});

// ロギング設定
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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

#### appsettings.json
```json
{
  "Azure": {
    "DocumentIntelligence": {
      "Endpoint": "https://your-resource.cognitiveservices.azure.com/",
      "Key": "your-key-here"
    }
  },
  "FileUpload": {
    "MaxFileSizeMB": 10,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".pdf", ".tiff", ".tif"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## 処理フロー

### 画像アップロードからテキスト抽出まで

```
1. ユーザーアクション
   ├── 画像をドラッグ&ドロップ
   └── または ファイル選択ダイアログから選択
   
2. クライアントサイド処理 (JavaScript)
   ├── 画像プレビュー表示
   ├── ファイル形式検証
   └── FormData オブジェクト作成
   
3. サーバーへ送信
   └── POST /Index?handler=Upload
   
4. サーバーサイド処理 (PageModel)
   ├── リクエスト検証
   ├── ファイルサイズチェック
   └── IOcrService.ExtractTextAsync() 呼び出し
   
5. OCRサービス処理
   ├── 画像をストリームで読み込み
   ├── Azure Document Intelligence API 呼び出し
   │   └── Read モデルを使用
   ├── 結果の解析
   └── OcrResult オブジェクト生成
   
6. レスポンス返却
   └── JSON 形式で結果を返却
   
7. クライアントサイド表示
   ├── 抽出テキストを表示
   ├── 行ごとの詳細情報表示
   └── コピーボタンの有効化
```

---

## Azure Document Intelligence 連携

### 使用するモデル
- **Read Model**: 印刷および手書きテキストの抽出

### API 呼び出し例
```csharp
public async Task<OcrResult> ExtractTextAsync(IFormFile imageFile)
{
    using var stream = imageFile.OpenReadStream();
    
    // Document Intelligence の Read 操作を開始
    var operation = await _client.AnalyzeDocumentAsync(
        WaitUntil.Completed,
        "prebuilt-read",
        stream
    );
    
    var result = operation.Value;
    
    // テキスト行を抽出
    var lines = new List<TextLine>();
    foreach (var page in result.Pages)
    {
        foreach (var line in page.Lines)
        {
            lines.Add(new TextLine
            {
                Text = line.Content,
                Confidence = line.Confidence ?? 0.0,
                BoundingBox = ConvertBoundingBox(line.BoundingBox)
            });
        }
    }
    
    return new OcrResult
    {
        Success = true,
        ExtractedText = result.Content,
        Lines = lines,
        PageCount = result.Pages.Count
    };
}
```

### 対応フォーマット
- JPEG/JPG
- PNG
- PDF
- TIFF/TIF
- BMP

---

## セキュリティ

### 実装する対策

#### 1. ファイルアップロード
- ファイルサイズ制限 (デフォルト: 10MB)
- ファイル拡張子のホワイトリスト検証
- MIMEタイプの検証
- ファイル名のサニタイゼーション

#### 2. CSRF 対策
- ASP.NET Core の AntiForgery トークン使用
- すべてのPOSTリクエストで検証

#### 3. エラーハンドリング
- 詳細なエラー情報をログに記録
- ユーザーには一般的なエラーメッセージのみ表示
- 例外情報の隠蔽

#### 4. Azure シークレット管理
- 本番環境: Azure Key Vault 使用
- 開発環境: User Secrets 使用
- 環境変数での設定もサポート

---

## パフォーマンス最適化

### 実装する最適化

#### 1. 非同期処理
- すべてのI/O操作で `async/await` 使用
- Azure API呼び出しの非同期化

#### 2. リソース管理
- `using` ステートメントでストリームを適切に破棄
- メモリリークの防止

#### 3. キャッシング
- 静的ファイルのブラウザキャッシング
- レスポンスキャッシング (必要に応じて)

#### 4. ロギング
- 構造化ログ (Serilog推奨)
- Application Insights 統合

---

## エラーハンドリング

### クライアントサイド
```javascript
try {
    const result = await this.submitImage(formData);
    if (result.success) {
        this.displayResults(result);
    } else {
        this.showError('テキストの抽出に失敗しました');
    }
} catch (error) {
    this.showError('サーバーとの通信に失敗しました');
    console.error(error);
}
```

### サーバーサイド
```csharp
try
{
    var result = await _ocrService.ExtractTextAsync(imageFile);
    return new JsonResult(result);
}
catch (RequestFailedException ex) when (ex.Status == 429)
{
    _logger.LogWarning("Azure API rate limit exceeded");
    return StatusCode(429, new { error = "リクエストが多すぎます。しばらく待ってから再試行してください" });
}
catch (RequestFailedException ex)
{
    _logger.LogError(ex, "Azure Document Intelligence API error");
    return StatusCode(500, new { error = "OCR処理中にエラーが発生しました" });
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error during OCR processing");
    return StatusCode(500, new { error = "予期しないエラーが発生しました" });
}
```

---

## コンテナ化

### Dockerfile
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
EXPOSE 8081

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WebApp.dll"]
```

### docker-compose.yml
```yaml
version: '3.8'

services:
  webapp:
    build:
      context: .
      dockerfile: docker/Dockerfile
    ports:
      - "8080:8080"
      - "8081:8081"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
      - Azure__DocumentIntelligence__Endpoint=${AZURE_DI_ENDPOINT}
      - Azure__DocumentIntelligence__Key=${AZURE_DI_KEY}
    volumes:
      - ./logs:/app/logs
```

### 環境変数
```bash
# .env ファイル (開発用)
AZURE_DI_ENDPOINT=https://your-resource.cognitiveservices.azure.com/
AZURE_DI_KEY=your-key-here
ASPNETCORE_ENVIRONMENT=Development
```

---

## テスト戦略

### 単体テスト (WebApp.Tests)

#### サービス層のテスト
```csharp
public class DocumentIntelligenceServiceTests
{
    [Fact]
    public async Task ExtractTextAsync_ValidImage_ReturnsOcrResult()
    {
        // Arrange
        var mockClient = new Mock<DocumentAnalysisClient>();
        var service = new DocumentIntelligenceService(mockClient.Object, logger);
        
        // Act
        var result = await service.ExtractTextAsync(mockFile);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.ExtractedText);
    }
    
    [Fact]
    public async Task ValidateImageAsync_InvalidFormat_ReturnsFalse()
    {
        // テスト実装
    }
}
```

#### PageModel のテスト
```csharp
public class IndexModelTests
{
    [Fact]
    public async Task OnPostUploadAsync_ValidImage_ReturnsJsonResult()
    {
        // テスト実装
    }
}
```

### 統合テスト
- Azure Document Intelligence API との実際の連携テスト
- エンドツーエンドのフローテスト

---

## 開発ロードマップ

### フェーズ1: 基盤構築
- [x] アーキテクチャ設計
- [ ] プロジェクト作成
- [ ] 依存関係のセットアップ
- [ ] Azure Document Intelligence リソースの作成

### フェーズ2: コア機能実装
- [ ] IOcrService インターフェース定義
- [ ] DocumentIntelligenceService 実装
- [ ] モデルクラスの作成
- [ ] PageModel の実装

### フェーズ3: UI実装
- [ ] Index.cshtml の作成
- [ ] JavaScript (ocr-app.js) の実装
- [ ] CSS スタイリング
- [ ] レスポンシブデザイン対応

### フェーズ4: 品質向上
- [ ] エラーハンドリングの実装
- [ ] ロギングの設定
- [ ] 単体テストの作成
- [ ] セキュリティ対策の実装

### フェーズ5: コンテナ化
- [ ] Dockerfile の作成
- [ ] docker-compose.yml の設定
- [ ] コンテナでの動作確認
- [ ] 環境変数の整理

### フェーズ6: ドキュメント整備
- [ ] README.md の更新
- [ ] API ドキュメント作成
- [ ] デプロイ手順書作成

---

## 依存パッケージ

### NuGet パッケージ
```xml
<!-- Azure AI サービス -->
<PackageReference Include="Azure.AI.FormRecognizer" Version="4.1.0" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.1.0" />
<PackageReference Include="Azure.Identity" Version="1.13.1" />

<!-- OpenTelemetry -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.9.0" />
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.2.0" />

<!-- ヘルスチェック -->
<PackageReference Include="AspNetCore.HealthChecks.UI.Client" Version="8.0.1" />
```

### クライアントサイドライブラリ
- Bootstrap 5.3.x (レスポンシブUI)
- Font Awesome (アイコン)

---

## Razor Pages を選択した理由

### MVC との比較

| 項目 | MVC | Razor Pages |
|------|-----|-------------|
| 構造 | Controller + View | Page + PageModel |
| ルーティング | 属性/規約ベース | ファイルベース |
| コードの配置 | 分離 (C/V分離) | 統合 (コロケーション) |
| 学習曲線 | やや高い | 低い |
| 適用場面 | 複雑なアプリ | ページ中心のアプリ |
| 保守性 | 規模による | シンプルな構造で高い |

### 今回のアプリに最適な理由
1. **単一ページアプリケーション**: 主要機能が1つのページに集約
2. **シンプルな構造**: 複雑なナビゲーションが不要
3. **保守性**: 関連コードが1箇所にまとまり理解しやすい
4. **開発速度**: ファイルベースルーティングで迅速な実装が可能

---

## 可観測性とヘルスチェック

### OpenTelemetry 統合

#### 設定 (Program.cs)
```csharp
// OpenTelemetry の設定
var connectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "WebApp", serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                // ヘルスチェックエンドポイントは除外
                options.Filter = context => 
                    !context.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("WebApp.HealthChecks")
            .AddMeter("WebApp.OCR")
            .AddMeter("WebApp.GPTVision");
    });

// Application Insights への送信
if (!string.IsNullOrEmpty(connectionString))
{
    otelBuilder.UseAzureMonitor(options =>
    {
        options.ConnectionString = connectionString;
    });
}
```

#### カスタムメトリクス
- **ヘルスチェック**: `health_check.executions`, `health_check.duration`
- **OCR**: `ocr.requests`, `ocr.errors`, `ocr.duration`, `ocr.text_lines` (Phase 4 で実装予定)
- **GPT Vision**: `gpt_vision.requests`, `gpt_vision.errors`, `gpt_vision.duration`, `gpt_vision.tokens` (Phase 4 で実装予定)

### ヘルスチェックエンドポイント

#### エンドポイント一覧
- **`/health`**: すべてのヘルスチェックの詳細情報 (JSON)
- **`/health/ready`**: Readiness プローブ (Kubernetes/Container Apps)
- **`/health/live`**: Liveness プローブ (外部依存関係なし)
- **`/warmup`**: Warmup エンドポイント (アプリケーション起動時のサービス初期化確認)
#### ヘルスチェック実装

##### DocumentIntelligenceHealthCheck
```csharp
public class DocumentIntelligenceHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentIntelligenceHealthCheck> _logger;
    private static readonly HttpClient _httpClient = new();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["DocumentIntelligence_Endpoint"];
            // HTTP HEAD リクエストでエンドポイントへの接続確認 (2秒タイムアウト)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, endpoint), cts.Token);
            
            return HealthCheckResult.Healthy("Document Intelligence endpoint is reachable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document Intelligence health check failed");
            return HealthCheckResult.Unhealthy("Cannot reach Document Intelligence endpoint", ex);
        }
    }
}
```

##### AzureOpenAIHealthCheck
```csharp
public class AzureOpenAIHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureOpenAIHealthCheck> _logger;
    private static readonly HttpClient _httpClient = new();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["AzureOpenAI:Endpoint"];
            // HTTP HEAD リクエストでエンドポイントへの接続確認 (2秒タイムアウト)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, endpoint), cts.Token);
            
            return HealthCheckResult.Healthy("Azure OpenAI endpoint is reachable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI health check failed");
            return HealthCheckResult.Unhealthy("Cannot reach Azure OpenAI endpoint", ex);
        }
    }
}
```

#### ヘルスチェックの特徴
- **軽量**: HTTP HEAD リクエストのみ (認証なし、API 呼び出しなし)
- **高速**: sub-second レスポンス (2秒タイムアウト)
- **メトリクス**: エンドポイント呼び出し時にカスタムメトリクスを記録
- **Azure 対応**: Container Apps/AKS の Readiness/Liveness プローブに最適化

### Warmup エンドポイント

#### `/warmup` エンドポイント
アプリケーション起動時にすべての依存サービスの初期化と接続確認を行います。

```csharp
app.MapGet("/warmup", async (IServiceProvider sp, IConfiguration config) =>
{
    var warmupLogger = sp.GetRequiredService<ILogger<Program>>();
    warmupLogger.LogInformation("Warmup エンドポイントが呼び出されました");
    
    try
    {
        // Document Intelligence の接続確認
        var docClient = sp.GetRequiredService<DocumentAnalysisClient>();
        var docEndpoint = config["DocumentIntelligence_Endpoint"];
        
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        var docRequest = new HttpRequestMessage(HttpMethod.Head, docEndpoint);
        var docResponse = await httpClient.SendAsync(docRequest);
        warmupLogger.LogInformation("Document Intelligence 接続確認成功");
        
        // OCR サービスの初期化確認
        var ocrService = sp.GetRequiredService<IOcrService>();
        
        // Azure OpenAI の接続確認
        var openAIEndpoint = config["AzureOpenAI:Endpoint"];
        var deploymentName = config["AzureOpenAI:DeploymentName"];
        
        if (!string.IsNullOrEmpty(openAIEndpoint) && !string.IsNullOrEmpty(deploymentName))
        {
            var gptService = sp.GetRequiredService<IGptVisionService>();
            var openAIRequest = new HttpRequestMessage(HttpMethod.Head, openAIEndpoint);
            var openAIResponse = await httpClient.SendAsync(openAIRequest);
            warmupLogger.LogInformation("Azure OpenAI 接続確認成功");
        }
        
        return Results.Ok(new { status = "ready", message = "Application warmed up successfully" });
    }
    catch (Exception ex)
    {
        warmupLogger.LogError(ex, "Warmup エラー");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
```

#### Warmup エンドポイントの特徴
- **HTTP アクセス可能**: HTTPSリダイレクトをスキップ（App Service の warmup トリガーに対応）
- **サービス初期化**: すべての依存サービス（DI コンテナ）の初期化を確認
- **接続確認**: Azure サービスへの実際の接続を確認（HTTP HEAD リクエスト、5秒タイムアウト）
- **エラーハンドリング**: 接続エラー時は HTTP 503 (Service Unavailable) を返却
- **詳細ログ**: 各サービスの初期化状況とエラーをログに記録

#### HTTPSリダイレクトの制御
```csharp
// Warmup エンドポイント以外のリクエストに対してのみ HTTPS リダイレクトを適用
// App Service の Warmup 機能は HTTP でリクエストするため、リダイレクトを除外
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/warmup"),
    mainApp => mainApp.UseHttpsRedirection()
);
```

`/warmup` パスのみHTTPアクセスを許可し、他のすべてのエンドポイントはHTTPSにリダイレクトされます。

---

## 今後の拡張性

### 実装可能な追加機能

#### 1. バッチ処理
- 複数画像の一括アップロード
- 処理結果の一括ダウンロード

#### 2. 結果の保存
- データベースへの保存
- ユーザーアカウント管理

#### 3. 高度な機能
- テーブル認識
- フォーム認識
- レイアウト解析

#### 4. エクスポート機能
- テキストファイルとしてダウンロード
- JSON/XML 形式でのエクスポート
- PDFへの注釈追加

---

## 参考資料

### 公式ドキュメント
- [ASP.NET Core Razor Pages](https://learn.microsoft.com/aspnet/core/razor-pages/)
- [Azure AI Document Intelligence](https://learn.microsoft.com/azure/ai-services/document-intelligence/)
- [Docker for .NET](https://learn.microsoft.com/dotnet/core/docker/introduction)

### ベストプラクティス
- [ASP.NET Core セキュリティ](https://learn.microsoft.com/aspnet/core/security/)
- [.NET アプリケーションのパフォーマンス](https://learn.microsoft.com/dotnet/core/diagnostics/performance-profiling)

---

## まとめ

このアーキテクチャは以下の特徴を持ちます:

✅ **シンプルさ**: Razor Pages による直感的な構造  
✅ **拡張性**: サービス層の抽象化により将来の変更に対応  
✅ **セキュリティ**: ベストプラクティスに基づいた実装  
✅ **パフォーマンス**: 非同期処理とリソース管理の最適化  
✅ **コンテナ対応**: Docker によるポータブルなデプロイ  
✅ **保守性**: 明確な責務分離と構造化されたコード

このアーキテクチャに基づいて開発を進めることで、堅牢で保守性の高いOCRアプリケーションを構築できます。
