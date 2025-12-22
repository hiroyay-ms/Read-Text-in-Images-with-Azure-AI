# GPT-4o による OCR 機能 - 段階的実装計画

**作成日**: 2025年12月22日  
**対象**: Azure OpenAI GPT-4o を使用した画像からのテキスト抽出機能の追加

---

## 📋 背景と目的

現在、Azure Document Intelligence を使用した OCR 機能が実装されていますが、GPT-4o を使用することで以下のメリットが得られます:

### メリット
- **コンテキスト理解**: 画像内のテキストを文脈を含めて理解
- **構造化抽出**: 特定のフォーマットでのテキスト抽出（JSON, Markdown等）
- **自然言語処理**: 抽出したテキストの要約や翻訳も同時に実行可能
- **柔軟性**: ユーザーが抽出方法を自然言語で指示可能

### 実装アプローチ
既存の Document Intelligence サービスと**併用**する形で、GPT-4o を使ったサービスを追加実装します。

---

## Phase 12: GPT-4o 基盤構築 (推定: 3-4時間)

### ゴール
Azure OpenAI Service を使用して GPT-4o によるテキスト抽出機能の基盤を構築

---

### Step 12.1: Azure OpenAI リソースの準備 (1時間)

#### タスク
- [ ] Azure Portal で Azure OpenAI Service リソースを確認（デプロイ済み）
- [ ] GPT-4o モデルのデプロイ名を確認
- [ ] エンドポイント URL を確認
- [ ] `appsettings.Development.json` に設定追加

#### Azure Portal での作業手順

1. **既存リソースの確認**
   ```
   Azure Portal → Azure OpenAI Service → 既存のリソースを選択
   ```

2. **必要な情報の取得**
   - エンドポイント URL: リソースの「キーとエンドポイント」から取得
   - デプロイ名: 「モデルのデプロイ」から GPT-4o のデプロイ名を確認
   - 認証方式: Entra ID (DefaultAzureCredential) を使用

3. **デプロイの確認**
   ```
   リソース → モデルのデプロイ → GPT-4o のデプロイを確認
   デプロイ名をメモ（例: gpt-4o）
   ```

#### 設定ファイルの更新

**ファイル: `appsettings.Development.json`**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Azure": {
    "DocumentIntelligence": {
      "Endpoint": "https://your-doc-intelligence.cognitiveservices.azure.com/"
    },
    "OpenAI": {
      "Endpoint": "https://your-openai.openai.azure.com/",
      "DeploymentName": "gpt-4o",
      "ApiVersion": "2024-02-15-preview"
    }
  },
  "FileUpload": {
    "MaxFileSizeMB": 10,
    "AllowedExtensions": [ ".jpg", ".jpeg", ".png", ".pdf", ".tiff", ".tif", ".bmp" ]
  }
}
```

#### 検証
- [ ] Azure Portal で既存リソースが確認できている
- [ ] GPT-4o のデプロイが存在している
- [ ] エンドポイント URL が取得できている
- [ ] デプロイ名が確認できている
- [ ] Entra ID 認証が有効になっている

---

### Step 12.2: NuGet パッケージのインストール (30分)

#### タスク
- [ ] `Azure.AI.OpenAI` パッケージをインストール
- [ ] 必要に応じて `Azure.Identity` を最新版に更新（既にインストール済み）

#### コマンド

```bash
cd src/WebApp
dotnet add package Azure.AI.OpenAI
dotnet restore
dotnet build
```

#### パッケージ情報
- **Azure.AI.OpenAI**: Azure OpenAI Service の公式 SDK
- **推奨バージョン**: 最新の安定版（1.0.0-beta.17 以降）

#### 検証
- [ ] パッケージが `WebApp.csproj` に追加されている
- [ ] ビルドが成功する
- [ ] 依存関係の競合がない

---

### Step 12.3: サービスインターフェースの作成 (30分)

#### タスク
- [ ] `IGptVisionService.cs` インターフェースの作成
- [ ] メソッドシグネチャの定義

#### ファイル: `Services/IGptVisionService.cs`

```csharp
namespace WebApp.Services;

/// <summary>
/// GPT-4o を使用した画像からのテキスト抽出サービス
/// </summary>
public interface IGptVisionService
{
    /// <summary>
    /// 画像からテキストを抽出します
    /// </summary>
    /// <param name="imageFile">アップロードされた画像ファイル</param>
    /// <param name="customPrompt">カスタムプロンプト（オプション）</param>
    /// <returns>抽出されたテキスト</returns>
    Task<string> ExtractTextFromImageAsync(IFormFile imageFile, string? customPrompt = null);

    /// <summary>
    /// 画像ファイルの検証を行います
    /// </summary>
    /// <param name="imageFile">検証する画像ファイル</param>
    /// <returns>検証結果（true: 有効、false: 無効）</returns>
    Task<bool> ValidateImageAsync(IFormFile imageFile);
}
```

#### 検証
- [ ] ファイルが作成されている
- [ ] 名前空間が正しい
- [ ] ビルドが成功する

---

### Step 12.4: OpenAI Vision サービスの実装 (1.5時間)

#### タスク
- [ ] `OpenAIVisionService.cs` の実装
- [ ] OpenAI クライアントの初期化
- [ ] 画像検証ロジックの実装
- [ ] テキスト抽出ロジックの実装
- [ ] エラーハンドリングの実装

#### ファイル: `Services/OpenAIVisionService.cs`

```csharp
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using WebApp.Models;

namespace WebApp.Services;

public class OpenAIVisionService : IGptVisionService
{
    private readonly OpenAIClient _client;
    private readonly string _deploymentName;
    private readonly ILogger<OpenAIVisionService> _logger;
    private readonly FileUploadOptions _options;

    public OpenAIVisionService(
        IConfiguration configuration,
        ILogger<OpenAIVisionService> logger)
    {
        // Azure OpenAI の設定を取得
        var endpoint = configuration["Azure:OpenAI:Endpoint"] 
            ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");
        _deploymentName = configuration["Azure:OpenAI:DeploymentName"] 
            ?? throw new InvalidOperationException("Deployment name not configured");
        
        // DefaultAzureCredential を使用してクライアントを初期化
        _client = new OpenAIClient(
            new Uri(endpoint),
            new DefaultAzureCredential());
        
        _logger = logger;
        _options = configuration.GetSection("FileUpload").Get<FileUploadOptions>() 
            ?? new FileUploadOptions();

        _logger.LogInformation("OpenAIVisionService が初期化されました。デプロイ名: {DeploymentName}", _deploymentName);
    }

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
            _logger.LogWarning("ファイルサイズが {MaxSize}MB を超えています。実際のサイズ: {ActualSize}MB", 
                _options.MaxFileSizeMB, 
                imageFile.Length / 1024.0 / 1024.0);
            return false;
        }

        // 拡張子チェック（GPT-4o サポート形式）
        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        
        if (!supportedExtensions.Contains(extension))
        {
            _logger.LogWarning("サポートされていないファイル形式です: {Extension}", extension);
            return false;
        }

        _logger.LogInformation("ファイル検証成功: {FileName} ({Size}KB)", 
            imageFile.FileName, 
            imageFile.Length / 1024);
        return true;
    }

    public async Task<string> ExtractTextFromImageAsync(
        IFormFile imageFile, 
        string? customPrompt = null)
    {
        try
        {
            _logger.LogInformation("テキスト抽出を開始します: {FileName}", imageFile.FileName);

            // 画像をBase64エンコード
            using var memoryStream = new MemoryStream();
            await imageFile.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();
            var base64Image = Convert.ToBase64String(imageBytes);
            var contentType = imageFile.ContentType;

            _logger.LogInformation("画像のエンコードが完了しました。サイズ: {Size}KB", imageBytes.Length / 1024);

            // プロンプトの設定
            var prompt = customPrompt ?? 
                "この画像に含まれるすべてのテキストを抽出してください。" +
                "テキストは元のレイアウトを保持したまま、読みやすい形式で返してください。" +
                "テキストが見つからない場合は、「テキストが検出されませんでした」と返してください。";

            // チャット完了リクエストの作成
            var chatCompletionsOptions = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage("あなたは画像からテキストを抽出する専門家です。画像内のすべてのテキストを正確に読み取り、元の構造を保持したまま返してください。"),
                    new ChatRequestUserMessage(
                        new ChatMessageTextContentItem(prompt),
                        new ChatMessageImageContentItem(
                            new Uri($"data:{contentType};base64,{base64Image}")))
                },
                MaxTokens = 4000,
                Temperature = 0.0f  // 一貫性のある結果のため
            };

            _logger.LogInformation("GPT-4o API を呼び出しています...");
            var response = await _client.GetChatCompletionsAsync(chatCompletionsOptions);

            var extractedText = response.Value.Choices[0].Message.Content;
            
            _logger.LogInformation("テキスト抽出が完了しました。文字数: {Length}, トークン使用量: {Tokens}", 
                extractedText.Length,
                response.Value.Usage.TotalTokens);

            return extractedText;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI API エラー: StatusCode={StatusCode}, Message={Message}", 
                ex.Status, 
                ex.Message);

            // ユーザーフレンドリーなエラーメッセージに変換
            var errorMessage = ex.Status switch
            {
                401 => "認証エラーが発生しました。Azure OpenAI の設定を確認してください。",
                403 => "アクセスが拒否されました。権限を確認してください。",
                429 => "リクエストが多すぎます。しばらく待ってから再試行してください。",
                500 => "Azure OpenAI サービスでエラーが発生しました。",
                _ => $"OCR処理中にエラーが発生しました: {ex.Message}"
            };

            throw new InvalidOperationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラーが発生しました");
            throw new InvalidOperationException("OCR処理中に予期しないエラーが発生しました。", ex);
        }
    }
}
```

#### 実装のポイント

1. **認証**: DefaultAzureCredential を使用（Entra ID 認証）
2. **画像エンコード**: Base64 エンコードして Data URI として送信
3. **プロンプト**: デフォルトプロンプトとカスタムプロンプトに対応
4. **エラーハンドリング**: HTTP ステータスコードに応じた詳細なエラーメッセージ
5. **ロギング**: 各ステップでの詳細なログ出力

#### 検証
- [ ] ファイルが作成されている
- [ ] ビルドが成功する
- [ ] ロジックが正しい

---

### Step 12.5: Program.cs への依存性注入の追加 (15分)

#### タスク
- [ ] Program.cs に IGptVisionService の DI 登録を追加

#### ファイル: `Program.cs` の更新

既存の Document Intelligence サービスの登録の後に追加:

```csharp
// Azure Document Intelligence サービスの登録（既存）
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["Azure:DocumentIntelligence:Endpoint"]
        ?? throw new InvalidOperationException("Azure Document Intelligence endpoint not configured");
    return new DocumentAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
});
builder.Services.AddScoped<IOcrService, DocumentIntelligenceService>();

// Azure OpenAI GPT-4o サービスの登録（新規追加）
builder.Services.AddScoped<IGptVisionService, OpenAIVisionService>();
```

#### 検証
- [ ] Program.cs が正しく更新されている
- [ ] ビルドが成功する
- [ ] アプリケーションが起動する

---

### Phase 12 完了チェックリスト

- [ ] 既存の Azure OpenAI リソースが確認されている
- [ ] GPT-4o モデルのデプロイが確認されている
- [ ] appsettings.Development.json に設定が追加されている
- [ ] Azure.AI.OpenAI パッケージがインストールされている
- [ ] IGptVisionService インターフェースが作成されている
- [ ] OpenAIVisionService が実装されている
- [ ] Program.cs に DI が登録されている
- [ ] ビルドが成功する
- [ ] エラーがない

---

## Phase 13: UI と API エンドポイントの拡張 (推定: 3-4時間)

### ゴール
GPT-4o を使用した OCR 機能の UI とバックエンド API を実装

---

### Step 13.1: モデルクラスの作成 (30分)

#### タスク
- [ ] `VisionOcrResult.cs` の作成
- [ ] GPT-4o の結果を格納するモデル

#### ファイル: `Models/VisionOcrResult.cs`

```csharp
namespace WebApp.Models;

/// <summary>
/// GPT-4o によるテキスト抽出結果
/// </summary>
public class VisionOcrResult
{
    /// <summary>
    /// 抽出されたテキスト
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// 使用されたメソッド
    /// </summary>
    public string Method { get; set; } = "GPT-4o";

    /// <summary>
    /// 文字数
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// 処理日時（UTC）
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// 使用されたカスタムプロンプト（あれば）
    /// </summary>
    public string? CustomPrompt { get; set; }
}
```

#### 検証
- [ ] ファイルが作成されている
- [ ] ビルドが成功する

---

### Step 13.2: PageModel の実装 (1時間)

#### タスク
- [ ] `Pages/OCR/Vision.cshtml.cs` の作成
- [ ] GPT-4o を使用した OCR 処理の実装
- [ ] エラーハンドリングの実装

#### ファイル: `Pages/OCR/Vision.cshtml.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Pages.OCR;

public class VisionModel : PageModel
{
    private readonly IGptVisionService _visionService;
    private readonly ILogger<VisionModel> _logger;

    [BindProperty]
    public string? CustomPrompt { get; set; }

    public VisionModel(
        IGptVisionService visionService,
        ILogger<VisionModel> logger)
    {
        _visionService = visionService;
        _logger = logger;
    }

    public void OnGet()
    {
        _logger.LogInformation("GPT-4o OCR ページが読み込まれました");
    }

    public async Task<IActionResult> OnPostExtractAsync(IFormFile imageFile)
    {
        try
        {
            _logger.LogInformation("テキスト抽出リクエストを受信しました");

            // ファイルの存在チェック
            if (imageFile == null || imageFile.Length == 0)
            {
                _logger.LogWarning("画像ファイルが選択されていません");
                return BadRequest(new OcrError 
                { 
                    Message = "画像ファイルが選択されていません",
                    Code = "NO_FILE"
                });
            }

            // ファイルの検証
            if (!await _visionService.ValidateImageAsync(imageFile))
            {
                _logger.LogWarning("無効なファイルです: {FileName}", imageFile.FileName);
                return BadRequest(new OcrError 
                { 
                    Message = "無効なファイルです。サポートされている形式: JPG, JPEG, PNG, GIF, WebP (最大10MB)",
                    Code = "INVALID_FILE"
                });
            }

            // テキスト抽出
            var extractedText = await _visionService.ExtractTextFromImageAsync(
                imageFile, 
                CustomPrompt);

            var result = new VisionOcrResult
            {
                ExtractedText = extractedText,
                CharacterCount = extractedText.Length,
                ProcessedAt = DateTime.UtcNow,
                CustomPrompt = CustomPrompt
            };

            _logger.LogInformation("テキスト抽出が完了しました。文字数: {Count}", result.CharacterCount);
            return new JsonResult(result);
        }
        catch (InvalidOperationException ex)
        {
            // サービス層からの詳細なエラーメッセージ
            _logger.LogError(ex, "OCR処理中にエラーが発生しました");
            return StatusCode(500, new OcrError 
            { 
                Message = ex.Message,
                Code = "SERVICE_ERROR",
                Details = ex.InnerException?.Message
            });
        }
        catch (Exception ex)
        {
            // 予期しないエラー
            _logger.LogError(ex, "予期しないエラーが発生しました");
            return StatusCode(500, new OcrError 
            { 
                Message = "処理中に予期しないエラーが発生しました",
                Code = "INTERNAL_ERROR",
                Details = ex.Message
            });
        }
    }
}
```

#### 実装のポイント

1. **ファイル検証**: サービス層の検証メソッドを使用
2. **カスタムプロンプト**: ユーザーが指定したプロンプトを渡す
3. **エラーハンドリング**: 詳細なエラーメッセージをクライアントに返す
4. **ロギング**: 各ステップでのログ出力

#### 検証
- [ ] ファイルが作成されている
- [ ] ビルドが成功する

---

### Step 13.3: Razor Page の実装 (1.5時間)

#### タスク
- [ ] `Pages/OCR/Vision.cshtml` の作成
- [ ] UI の実装（既存の OCR ページと同様の構造）
- [ ] カスタムプロンプト入力フィールドの追加

#### ファイル: `Pages/OCR/Vision.cshtml`

```cshtml
@page
@model WebApp.Pages.OCR.VisionModel
@{
    ViewData["Title"] = "GPT-4o OCR";
}

<div class="container mt-5">
    <h2 class="mb-4">
        <i class="bi bi-robot"></i> GPT-4o で画像からテキスト抽出
    </h2>

    <div class="alert alert-info" role="alert">
        <i class="bi bi-info-circle"></i>
        <strong>GPT-4o の特徴:</strong>
        画像内のテキストを文脈を理解しながら抽出できます。カスタム指示を使って、特定の形式での抽出も可能です。
    </div>

    <div class="row">
        <!-- 左側: アップロードエリア -->
        <div class="col-md-6">
            <div class="card shadow-sm">
                <div class="card-body">
                    <h5 class="card-title">
                        <i class="bi bi-cloud-upload"></i> 画像をアップロード
                    </h5>
                    
                    <form id="ocrForm" enctype="multipart/form-data">
                        @Html.AntiForgeryToken()
                        
                        <!-- ドラッグ&ドロップエリア -->
                        <div id="dropArea" class="border border-2 border-dashed rounded p-4 text-center mb-3">
                            <p class="mb-2">
                                <i class="bi bi-cloud-upload" style="font-size: 2rem; color: #0d6efd;"></i>
                            </p>
                            <p class="mb-2">ここに画像をドラッグ&ドロップ</p>
                            <p class="text-muted mb-3">または</p>
                            <div class="input-group">
                                <input type="file" class="form-control" id="imageFile" 
                                       accept=".jpg,.jpeg,.png,.gif,.webp">
                            </div>
                        </div>

                        <div class="alert alert-secondary small" role="alert">
                            <strong>対応形式:</strong> JPG, PNG, GIF, WebP<br>
                            <strong>最大サイズ:</strong> 10MB
                        </div>

                        <!-- カスタムプロンプト -->
                        <div class="mb-3">
                            <label for="customPrompt" class="form-label">
                                <i class="bi bi-chat-left-text"></i> カスタム指示（オプション）
                            </label>
                            <textarea class="form-control" id="customPrompt" rows="3"
                                      placeholder="例: テキストをMarkdown形式で抽出して、箇条書きにしてください"></textarea>
                            <div class="form-text">
                                空欄の場合は、画像内のすべてのテキストを抽出します。
                                特定の形式や処理方法を指示することができます。
                            </div>
                        </div>

                        <!-- 画像プレビュー -->
                        <div id="imagePreview" class="mb-3" style="display: none;">
                            <label class="form-label">プレビュー:</label>
                            <div class="text-center">
                                <img id="previewImage" class="img-fluid rounded shadow-sm" 
                                     style="max-height: 300px;" alt="プレビュー">
                            </div>
                        </div>

                        <!-- 実行ボタン -->
                        <button type="submit" id="runButton" class="btn btn-primary w-100" disabled>
                            <i class="bi bi-robot"></i> テキストを抽出
                        </button>
                    </form>
                </div>
            </div>
        </div>

        <!-- 右側: 結果表示エリア -->
        <div class="col-md-6">
            <div class="card shadow-sm">
                <div class="card-body">
                    <h5 class="card-title">
                        <i class="bi bi-file-text"></i> 抽出結果
                    </h5>
                    
                    <div id="resultArea">
                        <!-- 初期状態 -->
                        <div id="initialState" class="text-center text-muted py-5">
                            <i class="bi bi-file-text" style="font-size: 3rem;"></i>
                            <p class="mt-3">画像をアップロードしてテキストを抽出</p>
                            <p class="small">GPT-4o が画像を分析します</p>
                        </div>

                        <!-- ローディング状態 -->
                        <div id="loadingState" class="text-center py-5" style="display: none;">
                            <div class="spinner-border text-primary" role="status">
                                <span class="visually-hidden">処理中...</span>
                            </div>
                            <p class="mt-3"><strong>GPT-4o で分析中...</strong></p>
                            <p class="small text-muted">画像の内容を理解してテキストを抽出しています</p>
                        </div>

                        <!-- 結果表示 -->
                        <div id="resultContent" style="display: none;">
                            <div class="mb-3">
                                <div class="d-flex justify-content-between align-items-center mb-2">
                                    <span class="badge bg-success">
                                        <i class="bi bi-check-circle"></i> 完了
                                    </span>
                                    <button id="copyButton" class="btn btn-sm btn-outline-primary">
                                        <i class="bi bi-clipboard"></i> コピー
                                    </button>
                                </div>
                                <textarea id="extractedText" class="form-control font-monospace" 
                                          rows="15" readonly style="white-space: pre-wrap;"></textarea>
                            </div>
                            <div class="text-muted small">
                                <i class="bi bi-info-circle"></i>
                                <span id="charCount"></span> 文字 | 
                                処理時刻: <span id="processedTime"></span>
                            </div>
                            <div id="promptInfo" class="mt-2 small text-muted" style="display: none;">
                                <strong>使用した指示:</strong> <span id="usedPrompt"></span>
                            </div>
                        </div>

                        <!-- エラー表示 -->
                        <div id="errorState" class="alert alert-danger" style="display: none;">
                            <i class="bi bi-exclamation-triangle"></i>
                            <strong>エラー:</strong> <span id="errorMessage"></span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <!-- 使い方のヒント -->
    <div class="row mt-4">
        <div class="col-12">
            <div class="card border-info">
                <div class="card-body">
                    <h6 class="card-title">
                        <i class="bi bi-lightbulb"></i> カスタム指示の例
                    </h6>
                    <ul class="mb-0 small">
                        <li>「テキストを Markdown 形式で抽出してください」</li>
                        <li>「箇条書きの項目のみを抽出してください」</li>
                        <li>「日本語のテキストのみを抽出し、英語は無視してください」</li>
                        <li>「テキストを抽出して、簡単に要約してください」</li>
                        <li>「テーブルの内容を CSV 形式で抽出してください」</li>
                    </ul>
                </div>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <script src="~/js/vision-ocr.js"></script>
}
```

#### UI の特徴

1. **情報表示**: GPT-4o の特徴を説明
2. **カスタムプロンプト**: ユーザーが自由に指示を入力
3. **プレビュー**: 画像のプレビュー表示
4. **状態管理**: 初期、ローディング、結果、エラーの4つの状態
5. **使い方のヒント**: カスタムプロンプトの例を表示

#### 検証
- [ ] ファイルが作成されている
- [ ] HTML構造が正しい

---

### Step 13.4: JavaScript の実装 (1.5時間)

#### タスク
- [ ] `wwwroot/js/vision-ocr.js` の作成
- [ ] ファイルアップロード処理
- [ ] ドラッグ&ドロップ処理
- [ ] フォーム送信処理
- [ ] 結果表示処理

#### ファイル: `wwwroot/js/vision-ocr.js`

```javascript
/**
 * GPT-4o OCR アプリケーション
 */
class VisionOcrApp {
    constructor() {
        this.form = document.getElementById('ocrForm');
        this.imageFileInput = document.getElementById('imageFile');
        this.customPromptInput = document.getElementById('customPrompt');
        this.runButton = document.getElementById('runButton');
        this.dropArea = document.getElementById('dropArea');
        
        this.imagePreview = document.getElementById('imagePreview');
        this.previewImage = document.getElementById('previewImage');
        
        this.initialState = document.getElementById('initialState');
        this.loadingState = document.getElementById('loadingState');
        this.resultContent = document.getElementById('resultContent');
        this.errorState = document.getElementById('errorState');
        
        this.extractedText = document.getElementById('extractedText');
        this.copyButton = document.getElementById('copyButton');
        this.charCount = document.getElementById('charCount');
        this.processedTime = document.getElementById('processedTime');
        this.errorMessage = document.getElementById('errorMessage');
        this.promptInfo = document.getElementById('promptInfo');
        this.usedPrompt = document.getElementById('usedPrompt');
        
        this.initialize();
    }
    
    /**
     * イベントリスナーの初期化
     */
    initialize() {
        this.imageFileInput.addEventListener('change', () => this.handleFileSelect());
        this.form.addEventListener('submit', (e) => this.handleSubmit(e));
        this.copyButton.addEventListener('click', () => this.copyToClipboard());
        this.initializeDragAndDrop();
        
        console.log('VisionOcrApp が初期化されました');
    }
    
    /**
     * ドラッグ&ドロップの初期化
     */
    initializeDragAndDrop() {
        // デフォルトの動作を防止
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, (e) => {
                e.preventDefault();
                e.stopPropagation();
            });
        });
        
        // ドラッグオーバー時のスタイル変更
        ['dragenter', 'dragover'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, () => {
                this.dropArea.classList.add('drag-over');
            });
        });
        
        ['dragleave', 'drop'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, () => {
                this.dropArea.classList.remove('drag-over');
            });
        });
        
        // ドロップ時の処理
        this.dropArea.addEventListener('drop', (e) => {
            const files = e.dataTransfer.files;
            if (files.length > 0) {
                this.imageFileInput.files = files;
                this.handleFileSelect();
            }
        });
        
        // クリック時にファイル選択ダイアログを開く
        this.dropArea.addEventListener('click', () => {
            this.imageFileInput.click();
        });
    }
    
    /**
     * ファイル選択時の処理
     */
    handleFileSelect() {
        const file = this.imageFileInput.files[0];
        if (file) {
            console.log('ファイルが選択されました:', file.name);
            
            // 画像プレビューの表示
            const reader = new FileReader();
            reader.onload = (e) => {
                this.previewImage.src = e.target.result;
                this.imagePreview.style.display = 'block';
                this.runButton.disabled = false;
                console.log('プレビューを表示しました');
            };
            reader.readAsDataURL(file);
        }
    }
    
    /**
     * フォーム送信時の処理
     */
    async handleSubmit(e) {
        e.preventDefault();
        
        const file = this.imageFileInput.files[0];
        if (!file) {
            this.showError('画像ファイルを選択してください');
            return;
        }
        
        console.log('テキスト抽出を開始します...');
        this.showLoading();
        
        // FormData の作成
        const formData = new FormData();
        formData.append('imageFile', file);
        
        const customPrompt = this.customPromptInput.value.trim();
        if (customPrompt) {
            formData.append('customPrompt', customPrompt);
            console.log('カスタムプロンプト:', customPrompt);
        }
        
        // CSRF トークンの追加
        const token = this.form.querySelector('input[name="__RequestVerificationToken"]').value;
        formData.append('__RequestVerificationToken', token);
        
        try {
            const response = await fetch('/OCR/Vision?handler=Extract', {
                method: 'POST',
                body: formData
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || 'エラーが発生しました');
            }
            
            const result = await response.json();
            console.log('テキスト抽出が完了しました:', result);
            this.showResult(result);
        } catch (error) {
            console.error('エラーが発生しました:', error);
            this.showError(error.message);
        }
    }
    
    /**
     * ローディング状態の表示
     */
    showLoading() {
        this.initialState.style.display = 'none';
        this.loadingState.style.display = 'block';
        this.resultContent.style.display = 'none';
        this.errorState.style.display = 'none';
        this.runButton.disabled = true;
    }
    
    /**
     * 結果の表示
     */
    showResult(result) {
        this.initialState.style.display = 'none';
        this.loadingState.style.display = 'none';
        this.resultContent.style.display = 'block';
        this.errorState.style.display = 'none';
        
        this.extractedText.value = result.extractedText;
        this.charCount.textContent = result.characterCount;
        this.processedTime.textContent = new Date(result.processedAt).toLocaleString('ja-JP');
        
        // カスタムプロンプトが使用された場合に表示
        if (result.customPrompt) {
            this.usedPrompt.textContent = result.customPrompt;
            this.promptInfo.style.display = 'block';
        } else {
            this.promptInfo.style.display = 'none';
        }
        
        this.runButton.disabled = false;
        console.log('結果を表示しました');
    }
    
    /**
     * エラーの表示
     */
    showError(message) {
        this.initialState.style.display = 'none';
        this.loadingState.style.display = 'none';
        this.resultContent.style.display = 'none';
        this.errorState.style.display = 'block';
        
        this.errorMessage.textContent = message;
        this.runButton.disabled = false;
        console.error('エラーを表示しました:', message);
    }
    
    /**
     * クリップボードへのコピー
     */
    async copyToClipboard() {
        try {
            await navigator.clipboard.writeText(this.extractedText.value);
            
            // ボタンのテキストを一時的に変更
            const originalText = this.copyButton.innerHTML;
            this.copyButton.innerHTML = '<i class="bi bi-check"></i> コピーしました';
            this.copyButton.classList.remove('btn-outline-primary');
            this.copyButton.classList.add('btn-success');
            
            setTimeout(() => {
                this.copyButton.innerHTML = originalText;
                this.copyButton.classList.remove('btn-success');
                this.copyButton.classList.add('btn-outline-primary');
            }, 2000);
            
            console.log('クリップボードにコピーしました');
        } catch (err) {
            console.error('コピーに失敗しました:', err);
            alert('コピーに失敗しました');
        }
    }
}

// ページ読み込み時に初期化
document.addEventListener('DOMContentLoaded', () => {
    new VisionOcrApp();
});
```

#### JavaScript の特徴

1. **クラスベース**: 保守性の高いコード構造
2. **ドラッグ&ドロップ**: 直感的なファイルアップロード
3. **状態管理**: 4つの UI 状態を管理
4. **エラーハンドリング**: 詳細なエラーメッセージ表示
5. **ログ出力**: デバッグ用のコンソールログ

#### 検証
- [ ] ファイルが作成されている
- [ ] JavaScript 構文が正しい

---

### Step 13.5: ナビゲーションの更新 (15分)

#### タスク
- [ ] `_Layout.cshtml` にナビゲーションリンクを追加

#### ファイル: `Pages/Shared/_Layout.cshtml` の更新

既存のナビゲーションメニューに GPT-4o のリンクを追加:

```html
<ul class="navbar-nav flex-grow-1">
    <li class="nav-item">
        <a class="nav-link text-dark" asp-area="" asp-page="/Index">Home</a>
    </li>
    <li class="nav-item">
        <a class="nav-link text-dark" asp-area="" asp-page="/OCR/Index">
            <i class="bi bi-file-text"></i> Document Intelligence OCR
        </a>
    </li>
    <li class="nav-item">
        <a class="nav-link text-dark" asp-area="" asp-page="/OCR/Vision">
            <i class="bi bi-robot"></i> GPT-4o OCR
        </a>
    </li>
</ul>
```

#### 検証
- [ ] ナビゲーションが更新されている
- [ ] リンクが正しく動作する

---

### Phase 13 完了チェックリスト

- [ ] VisionOcrResult モデルが作成されている
- [ ] Vision.cshtml.cs PageModel が実装されている
- [ ] Vision.cshtml UI が作成されている
- [ ] vision-ocr.js JavaScript が実装されている
- [ ] ナビゲーションが更新されている
- [ ] ビルドが成功する
- [ ] アプリケーションが起動する

---

## Phase 14: テストとデバッグ (推定: 2-3時間)

### ゴール
実装した GPT-4o OCR 機能をテストし、問題を修正

---

### Step 14.1: 統合テスト (1.5時間)

#### テストシナリオ

##### 1. 正常系テスト

**テストケース 1.1: 基本的なテキスト抽出**
- [ ] JPEG 画像をアップロード
- [ ] カスタムプロンプトなしでテキスト抽出
- [ ] 結果が正しく表示される
- [ ] コピーボタンが動作する

**テストケース 1.2: PNG 画像の処理**
- [ ] PNG 画像をアップロード
- [ ] テキストが正しく抽出される

**テストケース 1.3: カスタムプロンプトの使用**
- [ ] カスタムプロンプトを入力（例: "Markdown形式で抽出"）
- [ ] 指定した形式で結果が返される
- [ ] 使用したプロンプトが結果に表示される

**テストケース 1.4: ドラッグ&ドロップ**
- [ ] 画像をドラッグ&ドロップ
- [ ] プレビューが表示される
- [ ] テキスト抽出が成功する

##### 2. 異常系テスト

**テストケース 2.1: ファイル未選択**
- [ ] ファイルを選択せずに送信
- [ ] エラーメッセージが表示される

**テストケース 2.2: サイズ超過**
- [ ] 10MB を超えるファイルをアップロード
- [ ] エラーメッセージが表示される

**テストケース 2.3: 非対応形式**
- [ ] PDF や TIFF などの非対応形式をアップロード
- [ ] エラーメッセージが表示される

**テストケース 2.4: Azure API エラー**
- [ ] エンドポイントを無効にして実行
- [ ] 適切なエラーメッセージが表示される

#### デバッグのポイント

1. **ブラウザの開発者ツール**
   - Console: JavaScript エラーの確認
   - Network: API リクエスト/レスポンスの確認
   - Application: Local Storage, Cookies の確認

2. **サーバーログ**
   - コンソール出力を確認
   - エラーログを確認
   - Azure OpenAI API の呼び出し状況を確認

3. **Azure Portal**
   - Azure OpenAI の使用状況を確認
   - レート制限の確認
   - エラーログの確認

#### よくある問題と解決策

| 問題 | 原因 | 解決策 |
|------|------|--------|
| 401 Unauthorized | 認証エラー | DefaultAzureCredential の設定を確認 |
| 429 Too Many Requests | レート制限 | リトライロジックの実装またはクォータの増加 |
| モデルが見つからない | デプロイ名の誤り | appsettings.json のデプロイ名を確認 |
| 画像が送信されない | Base64 エンコードエラー | ファイルサイズと形式を確認 |

---

### Step 14.2: パフォーマンステスト (1時間)

#### テスト項目

1. **処理時間の測定**
   - [ ] 小さい画像（～1MB）の処理時間を計測
   - [ ] 大きい画像（～10MB）の処理時間を計測
   - [ ] 複雑な画像（テキストが多い）の処理時間を計測

2. **トークン使用量の確認**
   - [ ] 各画像での トークン使用量をログで確認
   - [ ] コスト見積もりを計算

3. **同時リクエストのテスト**
   - [ ] 複数のブラウザタブで同時にリクエスト
   - [ ] レート制限に達しないか確認

#### 期待される結果

- **処理時間**: 5-15秒（画像サイズとテキスト量による）
- **トークン使用量**: 500-2000 トークン/リクエスト
- **エラーレート**: 0%（正常な環境下）

---

### Step 14.3: Document Intelligence との比較テスト (30分)

#### 比較項目

| 項目 | Document Intelligence | GPT-4o |
|------|----------------------|--------------|
| 処理速度 | 速い（3-5秒） | やや遅い（5-15秒） |
| 精度 | 非常に高い | 高い |
| コスト | 低い | 高い |
| 柔軟性 | 低い | 非常に高い |
| 座標情報 | あり | なし |
| 文脈理解 | なし | あり |

#### 推奨される使い分け

- **Document Intelligence を使用すべき場合**:
  - 高精度な座標情報が必要
  - 大量の画像を処理
  - コストを抑えたい
  - 定型フォーマットの文書

- **GPT-4o を使用すべき場合**:
  - 文脈理解が必要
  - 柔軟な抽出ルール
  - 特定の形式での出力が必要
  - 要約や翻訳も同時に実行したい

---

### Phase 14 完了チェックリスト

- [ ] すべての正常系テストが成功
- [ ] すべての異常系テストが成功
- [ ] エラーハンドリングが適切
- [ ] パフォーマンスが許容範囲内
- [ ] ログが適切に出力されている
- [ ] Document Intelligence との使い分けが明確

---

## Phase 15: ドキュメント作成（オプション） (推定: 1-2時間)

### Step 15.1: README.md の更新

GPT-4o 機能の追加を README.md に記載:

```markdown
## 機能

### 1. Document Intelligence OCR
Azure Document Intelligence を使用した高精度なテキスト抽出

- 印刷・手書きテキストの認識
- 座標情報の取得
- 複数ページの PDF サポート

### 2. GPT-4o OCR
Azure OpenAI GPT-4o を使用した柔軟なテキスト抽出

- 文脈を理解したテキスト抽出
- カスタムプロンプトによる柔軟な出力形式
- 要約・翻訳などの後処理
```

---

### Step 15.2: 使い方ガイドの作成

ユーザー向けの使い方ガイドを作成（オプション）

---

## 📊 実装完了後の全体構成

```
src/WebApp/
├── Services/
│   ├── IOcrService.cs                    # Document Intelligence インターフェース
│   ├── DocumentIntelligenceService.cs    # Document Intelligence 実装
│   ├── IGptVisionService.cs              # GPT-4o インターフェース
│   └── OpenAIVisionService.cs            # GPT-4o 実装
├── Models/
│   ├── OcrResult.cs                      # Document Intelligence 結果
│   ├── VisionOcrResult.cs                # GPT-4o 結果
│   ├── OcrError.cs
│   └── FileUploadOptions.cs
├── Pages/
│   ├── OCR/
│   │   ├── Index.cshtml                  # Document Intelligence UI
│   │   ├── Index.cshtml.cs
│   │   ├── Vision.cshtml                 # GPT-4o UI
│   │   └── Vision.cshtml.cs
│   └── Shared/
│       └── _Layout.cshtml
├── wwwroot/
│   └── js/
│       ├── ocr-app.js                    # Document Intelligence JS
│       └── vision-ocr.js                 # GPT-4o JS
└── Program.cs
```

---

## 🔧 実装チェックリスト（全体）

### Phase 12: GPT-4o 基盤構築
- [ ] 既存 Azure OpenAI リソースの確認
- [ ] GPT-4o モデルのデプロイ確認
- [ ] appsettings.json の設定
- [ ] Azure.AI.OpenAI パッケージのインストール
- [ ] IGptVisionService インターフェースの作成
- [ ] OpenAIVisionService の実装
- [ ] Program.cs への DI 登録

### Phase 13: UI と API 拡張
- [ ] VisionOcrResult モデルの作成
- [ ] Vision.cshtml.cs PageModel の実装
- [ ] Vision.cshtml UI の作成
- [ ] vision-ocr.js JavaScript の実装
- [ ] ナビゲーションの更新

### Phase 14: テストとデバッグ
- [ ] 統合テストの実施
- [ ] パフォーマンステスト
- [ ] Document Intelligence との比較

### Phase 15: ドキュメント（オプション）
- [ ] README.md の更新
- [ ] 使い方ガイドの作成

---

## 💡 実装のヒント

### GPT-4o 使用時の注意点

#### 1. コスト管理
- **入力トークン**: 画像サイズにより変動（500-2000トークン）
- **出力トークン**: 抽出されたテキスト量による
- **推定コスト**: $0.01-0.05/リクエスト（画像により変動）

#### 2. レート制限
- **TPM (Tokens Per Minute)**: デプロイメントごとに設定
- **RPM (Requests Per Minute)**: 同時リクエスト数の制限
- **推奨**: リトライロジックの実装

#### 3. 画像形式と制限
- **対応形式**: JPEG, PNG, GIF, WebP
- **最大サイズ**: 20MB（推奨は10MB以下）
- **解像度**: 高解像度ほど精度向上（ただしコスト増）

#### 4. プロンプトエンジニアリング
- **明確な指示**: 具体的な出力形式を指定
- **例示**: 期待する出力例を示す
- **制約**: 不要な情報を除外する指示

### おすすめのカスタムプロンプト例

```
# Markdown 形式での抽出
"この画像のテキストを Markdown 形式で抽出し、見出しは # を使用してください"

# 箇条書きのみ
"この画像から箇条書きの項目のみを抽出してください"

# 特定言語のみ
"日本語のテキストのみを抽出し、英語やその他の言語は無視してください"

# 要約付き
"この画像のテキストを抽出し、その後 3行で要約してください"

# テーブル抽出
"この画像のテーブルを CSV 形式で抽出してください"
```

---

## 📈 推定作業時間まとめ

| Phase | 内容 | 時間 |
|-------|------|------|
| Phase 12 | GPT-4o 基盤構築 | 3-4時間 |
| Phase 13 | UI と API 拡張 | 3-4時間 |
| Phase 14 | テストとデバッグ | 2-3時間 |
| Phase 15 | ドキュメント（オプション） | 1-2時間 |
| **合計** | | **9-13時間** |

---

## 🎯 次のステップ

### 短期的な拡張（1-2週間）
1. **比較機能**: Document Intelligence と GPT-4o の並列実行と比較
2. **バッチ処理**: 複数画像の一括処理
3. **結果の保存**: データベースへの保存機能

### 中期的な拡張（1-2ヶ月）
1. **プロンプトテンプレート**: よく使うプロンプトの保存
2. **履歴機能**: 過去の処理結果の表示
3. **API 公開**: REST API エンドポイントの提供

### 長期的な拡張（3-6ヶ月）
1. **カスタムモデル**: Fine-tuning による精度向上
2. **マルチモーダル**: 画像と音声の同時処理
3. **リアルタイム処理**: WebSocket を使用したストリーミング

---

**最終更新日**: 2025年12月22日  
**推定合計時間**: 9-13 時間  
**難易度**: 中級～上級
