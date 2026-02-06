# Read-Text-in-Images-with-Azure-AI

Azure AI サービスを活用した画像からのテキスト抽出・ドキュメント翻訳アプリケーション

## 概要

このアプリケーションは、Azure Document Intelligence、Azure OpenAI GPT-4o、および Azure Translator の3つの異なる AI サービスを使用して、画像からのテキスト抽出とドキュメント翻訳を行う Web アプリケーションです。用途に応じて最適な AI サービスを選択できます。

## 主な機能

### 1. Document Intelligence OCR
- Azure AI Document Intelligence の prebuilt-read モデルを使用
- 高精度な文字認識と信頼度スコア表示
- 構造化されたドキュメントの読み取りに最適
- ドラッグ&ドロップによる直感的な操作

### 2. GPT-4o Vision OCR
- Azure OpenAI GPT-4o の Vision 機能を使用
- カスタムプロンプトによる柔軟な文字抽出
- 文脈理解と AI による解釈が可能
- 特定のルールに基づいた抽出に対応

### 3. ドキュメント翻訳 (Azure Translator)
- Azure Translator Document Translation API を使用
- 48言語間での翻訳に対応
- ドキュメントの形式を保持したまま翻訳
- PDF, DOCX, XLSX, PPTX, HTML, TXT など多様な形式に対応
- Managed Identity によるセキュアな認証

### 4. AI 翻訳 (GPT-4o)
- Azure OpenAI GPT-4o を使用した高品質翻訳
- カスタムプロンプトによる柔軟な翻訳スタイル
- Document Intelligence でテキスト抽出、PdfPig で画像抽出
- **複数画像の位置保持**：図の OCR テキストを削除し、正しい位置に画像を挿入
- **画像プロキシ**：Storage Account のネットワーク制限下でも画像表示可能
- Markdown 形式で構造を保持
- PDF 保存はブラウザの印刷機能で対応
- 専門用語やトーンのカスタマイズが可能

## 使い分けガイド

### Document Intelligence を使うべき場合
- ✅ 高精度な文字認識が必要
- ✅ 信頼度スコアで品質を確認したい
- ✅ 請求書、領収書、フォームなど構造化されたドキュメント
- ✅ 標準的な OCR 処理で十分

### GPT-4o Vision を使うべき場合
- ✅ カスタムプロンプトで抽出ルールを指定したい
- ✅ 文脈を理解した上でテキストを抽出したい
- ✅ 特定のフォーマットで出力したい
- ✅ AI による解釈や要約が必要

### ドキュメント翻訳 (Azure Translator) を使うべき場合
- ✅ ビジネスドキュメントの多言語化が必要
- ✅ ドキュメントのレイアウトを完全に保持したい
- ✅ Excel、PowerPoint などの翻訳
- ✅ 大量のドキュメントを効率的に処理したい

### AI 翻訳 (GPT-4o) を使うべき場合
- ✅ コンテキストを理解した高品質な翻訳が必要
- ✅ 専門用語やトーンをカスタマイズしたい
- ✅ PDF/Word を Markdown に変換して翻訳したい
- ✅ 法律、医療、技術文書など専門分野の翻訳

## 技術スタック

- **フレームワーク**: ASP.NET Core (.NET 10) - Razor Pages
- **Azure サービス**:
  - Azure AI Document Intelligence (prebuilt-read / prebuilt-layout モデル)
  - Azure OpenAI Service (GPT-4o Vision / GPT-4o 翻訳)
  - Azure Translator (Document Translation API)
  - Azure Blob Storage (翻訳用一時ストレージ)
- **PDF 処理**: PdfPig (画像抽出)
- **Markdown 処理**: Markdig
- **認証**: Azure Entra ID (DefaultAzureCredential / Managed Identity)
- **可観測性**: OpenTelemetry + Application Insights
- **UI**: Bootstrap 5 + カスタム CSS

## プロジェクト構造

```
Read-Text-in-Images-with-Azure-AI/
├── src/
│   └── WebApp/
│       ├── Pages/
│       │   ├── Index.cshtml              # ホーム画面
│       │   ├── Index.cshtml.cs
│       │   ├── Error.cshtml              # エラーページ
│       │   ├── Error.cshtml.cs
│       │   ├── OCR/
│       │   │   ├── DocumentIntelligence.cshtml     # Document Intelligence OCR 画面
│       │   │   ├── DocumentIntelligence.cshtml.cs
│       │   │   ├── GPT.cshtml                      # GPT-4o Vision OCR 画面
│       │   │   └── GPT.cshtml.cs
│       │   ├── Translator/
│       │   │   ├── AzureTranslator.cshtml          # Azure Translator 翻訳画面
│       │   │   ├── AzureTranslator.cshtml.cs
│       │   │   ├── GPT.cshtml                      # GPT-4o AI 翻訳画面
│       │   │   └── GPT.cshtml.cs
│       │   ├── Shared/
│       │   │   ├── _Layout.cshtml        # 共通レイアウト
│       │   │   ├── _Layout.cshtml.css
│       │   │   └── _ValidationScriptsPartial.cshtml
│       │   ├── _ViewImports.cshtml
│       │   └── _ViewStart.cshtml
│       ├── Services/
│       │   ├── IOcrService.cs                    # OCR インターフェース
│       │   ├── DocumentIntelligenceService.cs    # Document Intelligence 実装
│       │   ├── IGptVisionService.cs              # GPT Vision インターフェース
│       │   ├── OpenAIVisionService.cs            # GPT-4o Vision 実装
│       │   ├── ITranslatorService.cs             # Azure Translator インターフェース
│       │   ├── AzureTranslatorService.cs         # Azure Translator 実装
│       │   ├── IGptTranslatorService.cs          # GPT 翻訳インターフェース
│       │   ├── GptTranslatorService.cs           # GPT-4o 翻訳実装
│       │   └── HealthChecks/
│       │       ├── DocumentIntelligenceHealthCheck.cs  # Document Intelligence ヘルスチェック
│       │       ├── AzureOpenAIHealthCheck.cs           # Azure OpenAI ヘルスチェック
│       │       ├── AzureTranslatorHealthCheck.cs       # Azure Translator ヘルスチェック
│       │       └── AzureBlobStorageHealthCheck.cs      # Azure Blob Storage ヘルスチェック
│       ├── Models/
│       │   ├── OcrResult.cs              # Document Intelligence 結果
│       │   ├── VisionOcrResult.cs        # GPT-4o Vision 結果
│       │   ├── TranslationResult.cs      # Azure Translator 翻訳結果
│       │   ├── GptTranslationResult.cs   # GPT 翻訳結果
│       │   ├── GptTranslationOptions.cs  # GPT 翻訳オプション
│       │   ├── ExtractedImage.cs         # 抽出画像情報
│       │   ├── OcrError.cs               # エラー情報
│       │   └── FileUploadOptions.cs      # ファイルアップロード設定
│       ├── wwwroot/
│       │   ├── css/
│       │   │   └── site.css              # カスタムスタイル
│       │   ├── js/
│       │   │   ├── ocr-app.js            # Document Intelligence UI
│       │   │   ├── gpt-vision.js         # GPT-4o Vision UI
│       │   │   ├── translator.js         # Azure Translator UI
│       │   │   ├── gpt-translator.js     # GPT-4o 翻訳 UI
│       │   │   └── site.js
│       │   └── lib/
│       │       ├── bootstrap/
│       │       ├── jquery/
│       │       └── jquery-validation/
│       ├── Properties/
│       │   └── launchSettings.json
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── WebApp.csproj
├── references/
│   ├── plan.md                           # 実装計画 (Phase 1-10)
│   ├── plan_add-on-1.md                  # 追加機能計画 (GPT-4o Vision)
│   ├── plan_add-on-2.md                  # 追加機能計画 (OpenTelemetry)
│   ├── plan_translator.md                # 追加機能計画 (Azure Translator)
│   ├── plan_translator_gpt.md            # 追加機能計画 (GPT-4o 翻訳)
│   ├── architecture.md                   # アーキテクチャ設計
│   ├── features.md                       # 機能一覧
│   ├── implementation-plan.md            # 実装計画
│   ├── image1.png                        # UIモック (アップロード前)
│   └── image2.png                        # UIモック (アップロード後)
├── AzureAISample.sln
└── README.md
```

## セットアップ

### 前提条件
- .NET 10 SDK
- Azure サブスクリプション
- Azure AI Document Intelligence リソース
- Azure OpenAI Service リソース (GPT-4o モデルデプロイ済み)

### 環境変数の設定

#### 必須環境変数

App Service のアプリケーション設定に以下の環境変数を設定してください：

```bash
# Document Intelligence エンドポイント
DocumentIntelligence_Endpoint=https://your-resource.cognitiveservices.azure.com/

# Azure OpenAI エンドポイント
AzureOpenAI__Endpoint=https://your-resource.openai.azure.com/

# Azure OpenAI デプロイメント名
AzureOpenAI__DeploymentName=gpt-4o

# Azure Translator エンドポイント
AzureTranslator__Endpoint=https://your-translator.cognitiveservices.azure.com/

# Azure Translator リージョン
AzureTranslator__Region=japaneast

# Azure Storage アカウント名（ドキュメント翻訳用）
AzureStorage__AccountName=your-storage-account-name

# Azure Storage コンテナ名（Azure Translator 用）
AzureStorage__SourceContainerName=source
AzureStorage__TargetContainerName=target

# Azure Storage コンテナ名（GPT-4o 翻訳用）
AzureStorage__TranslatedContainerName=translated
```

> **注意**: 
> - Storage への認証は Entra ID (DefaultAzureCredential / Managed Identity) を使用します。接続文字列は不要です。
> - App Service のマネージド ID に「Storage Blob Data Contributor」ロールを割り当ててください。
> - Application Insights の接続文字列（`APPLICATIONINSIGHTS_CONNECTION_STRING`）は、App Service で Application Insights を有効にすると自動的に設定されます。

#### オプション環境変数

```bash
# ファイルアップロードの最大サイズ（MB、デフォルト: 10）
FileUpload__MaxFileSizeMB=10

# ヘルスチェックのタイムアウト（秒、デフォルト: 10）
HealthChecks__TimeoutSeconds=10

# ヘルスチェックの詳細エラー表示（デフォルト: true）
HealthChecks__EnableDetailedErrors=true
```

> **注意**: Azure App Service では、環境変数名の `:` を `__` (アンダースコア2つ) に置き換えてください。

### 実行方法
```bash
cd src/WebApp
dotnet run
```

ブラウザで `http://localhost:5269` にアクセス

## 可観測性とヘルスチェック

### OpenTelemetry 統合
アプリケーションは OpenTelemetry を使用して、トレーシングとメトリクスを Application Insights に送信します。

- **トレーシング**: HTTP リクエスト、Azure API 呼び出しなどの分散トレース
- **メトリクス**: カスタムメトリクス（ヘルスチェック、OCR、GPT Vision）
- **ログ**: 構造化ログの出力

### ヘルスチェックエンドポイント

#### `/warmup`
アプリケーション起動時にすべての依存サービスの初期化と接続確認を行います。App Service の warmup トリガーで使用されます。
```bash
curl http://localhost:5269/warmup
```

レスポンス例:
```json
{
  "status": "ready",
  "message": "Application warmed up successfully"
}
```

**確認対象サービス:**
| サービス | 確認内容 |
|----------|----------|
| Document Intelligence | エンドポイント接続 |
| OCR サービス | DI 初期化 |
| Azure OpenAI | エンドポイント接続 |
| GPT Vision サービス | DI 初期化 |
| Azure Translator | 言語一覧 API 接続 |
| Azure Blob Storage | アカウント接続 |
| GPT 翻訳サービス | DI 初期化 |
| PDF 変換サービス | DI 初期化 |

**特徴:**
- HTTP アクセス可能（HTTPSリダイレクトなし）
- すべてのサービスの初期化を確認
- Azure サービスへの実際の接続確認（HTTP HEAD/GET リクエスト）
- エラー時は HTTP 503 (Service Unavailable) を返却

#### `/health`
すべてのヘルスチェックの詳細情報を JSON 形式で返します。
```bash
curl http://localhost:5269/health
```

レスポンス例:
```json
{
  "status": "Healthy",
  "checks": [
    {
      "name": "document_intelligence",
      "status": "Healthy",
      "description": "Document Intelligence endpoint is reachable",
      "duration": "00:00:00.234"
    },
    {
      "name": "azure_openai",
      "status": "Healthy",
      "description": "Azure OpenAI endpoint is reachable",
      "duration": "00:00:00.189"
    },
    {
      "name": "azure_translator",
      "status": "Healthy",
      "description": "Azure Translator サービスは正常です（リージョン: japaneast）",
      "duration": "00:00:00.156"
    },
    {
      "name": "azure_blob_storage",
      "status": "Healthy",
      "description": "Azure Blob Storage は正常です - コンテナ: source(source): OK, target(target): OK, translated(translated): OK",
      "duration": "00:00:00.312"
    }
  ],
  "totalDuration": "00:00:00.891"
}
```

#### `/health/ready`
Readiness プローブ。外部依存関係のチェック結果を返します（Kubernetes/Container Apps 用）。
```bash
curl http://localhost:5269/health/ready
```

#### `/health/live`
Liveness プローブ。アプリケーションが起動していることを確認します（外部依存関係はチェックしません）。
```bash
curl http://localhost:5269/health/live
```

### Application Insights の設定

Application Insights に接続する場合、環境変数 `APPLICATIONINSIGHTS_CONNECTION_STRING` を設定します：

```bash
# ローカル開発環境（appsettings.Development.json）
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=your-key;IngestionEndpoint=https://...

# App Service では自動的に設定されるため、手動設定は不要
```

> **注意**: Azure App Service で Application Insights を有効にすると、`APPLICATIONINSIGHTS_CONNECTION_STRING` 環境変数が自動的に設定されます。

## 主な実装機能

### OCR 機能
- ✅ ドラッグ&ドロップによるファイルアップロード
- ✅ 画像プレビュー表示
- ✅ リアルタイム処理進捗表示
- ✅ 信頼度スコア付きテキスト抽出 (Document Intelligence)
- ✅ カスタムプロンプト対応 (GPT-4o Vision)
- ✅ Azure API エラーハンドリング (429, 401/403 対応)
- ✅ レスポンシブデザイン

### 翻訳機能
- ✅ Azure Translator によるドキュメント翻訳（48言語対応）
- ✅ GPT-4o による高品質 AI 翻訳（カスタムプロンプト対応）
- ✅ Document Intelligence 連携（テキスト＋画像抽出）
- ✅ Markdown 形式での構造保持
- ✅ PDF 保存はブラウザ印刷機能で対応

### 可観測性とヘルスチェック
- ✅ OpenTelemetry による分散トレーシングとメトリクス
- ✅ Application Insights 統合
- ✅ ヘルスチェックエンドポイント (`/health`, `/health/ready`, `/health/live`)
  - Document Intelligence ヘルスチェック
  - Azure OpenAI ヘルスチェック
  - Azure Translator ヘルスチェック
  - Azure Blob Storage ヘルスチェック（コンテナ存在確認）
- ✅ Warmup エンドポイント (`/warmup`) - App Service の起動時初期化
  - 8つのサービスの初期化と接続確認
- ✅ カスタムメトリクスによるヘルスチェック状態の記録
- ✅ Azure Container Apps/AKS の Readiness/Liveness プローブ対応

## ライセンス

MIT License