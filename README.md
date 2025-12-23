# Read-Text-in-Images-with-Azure-AI

Azure AI サービスを活用した画像からのテキスト抽出アプリケーション

## 概要

このアプリケーションは、Azure Document Intelligence と Azure OpenAI GPT-4o の2つの異なる AI サービスを使用して、画像からテキストを抽出する Web アプリケーションです。用途に応じて最適な OCR サービスを選択できます。

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

## 技術スタック

- **フレームワーク**: ASP.NET Core (.NET 10) - Razor Pages
- **Azure サービス**:
  - Azure AI Document Intelligence (prebuilt-read モデル)
  - Azure OpenAI Service (GPT-4o Vision)
- **認証**: Azure Entra ID (DefaultAzureCredential)
- **UI**: Bootstrap 5 + カスタム CSS

## プロジェクト構造

```
Read-Text-in-Images-with-Azure-AI/
├── src/
│   └── WebApp/
│       ├── Pages/
│       │   ├── Index.cshtml              # ホーム画面
│       │   ├── OCR/
│       │   │   ├── Index.cshtml          # Document Intelligence 画面
│       │   │   └── Index.cshtml.cs
│       │   ├── GPT/
│       │   │   ├── Index.cshtml          # GPT-4o Vision 画面
│       │   │   └── Index.cshtml.cs
│       │   └── Shared/
│       │       └── _Layout.cshtml        # 共通レイアウト
│       ├── Services/
│       │   ├── IOcrService.cs
│       │   ├── DocumentIntelligenceService.cs   # Document Intelligence 実装
│       │   ├── IGptVisionService.cs
│       │   └── OpenAIVisionService.cs           # GPT-4o Vision 実装
│       ├── Models/
│       │   ├── OcrResult.cs              # Document Intelligence 結果
│       │   └── VisionOcrResult.cs        # GPT-4o Vision 結果
│       └── wwwroot/
│           ├── css/
│           │   └── site.css              # カスタムスタイル
│           └── js/
│               ├── ocr-app.js            # Document Intelligence UI
│               └── gpt-vision.js         # GPT-4o Vision UI
├── references/
│   ├── plan.md                           # 実装計画 (Phase 1-10)
│   ├── plan_add-on-1.md                  # 追加機能計画 (GPT-4o)
│   ├── architecture.md                   # アーキテクチャ設計
│   └── features.md                       # 機能一覧
└── README.md
```

## セットアップ

### 前提条件
- .NET 10 SDK
- Azure サブスクリプション
- Azure AI Document Intelligence リソース
- Azure OpenAI Service リソース (GPT-4o モデルデプロイ済み)

### 環境変数の設定
```bash
# Document Intelligence
AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT=https://your-resource.cognitiveservices.azure.com/
# GPT-4o Vision
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o
```

### 実行方法
```bash
cd src/WebApp
dotnet run
```

ブラウザで `http://localhost:5269` にアクセス

## 主な実装機能

- ✅ ドラッグ&ドロップによるファイルアップロード
- ✅ 画像プレビュー表示
- ✅ リアルタイム処理進捗表示
- ✅ 信頼度スコア付きテキスト抽出 (Document Intelligence)
- ✅ カスタムプロンプト対応 (GPT-4o Vision)
- ✅ Azure API エラーハンドリング (429, 401/403 対応)
- ✅ レスポンシブデザイン

## ライセンス

MIT License