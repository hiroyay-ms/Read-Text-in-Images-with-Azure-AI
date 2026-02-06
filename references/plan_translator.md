# Azure Translator によるドキュメント翻訳機能 - 段階的実装計画

**作成日**: 2026年2月2日  
**対象**: Azure Translator (AI Foundry) を使用したドキュメント翻訳機能の追加  
**状態**: ✅ Phase 17 完了！ → 次は Phase 18 (テストとデバッグ)  
**認証方式**: Entra ID 認証（DefaultAzureCredential）✅

---

## 📋 背景と目的

既存の OCR 機能（Document Intelligence、GPT-4o）に加え、Azure Translator を使用したドキュメント翻訳機能を追加します。

### メリット
- **多言語対応**: 100以上の言語間での翻訳
- **ドキュメント形式保持**: PDF、Word、Excel などの形式を保持したまま翻訳
- **高精度**: ニューラル機械翻訳による高品質な翻訳
- **シンプルな操作**: 1つのファイルを選択して即座に翻訳

### 実装アプローチ
Azure Translator の Document Translation API を使用し、Azure Blob Storage を介したドキュメント翻訳を実装します。翻訳は同期的に実行され、完了するまでバックエンドで待機します。

---

## Phase 16: Azure Translator 基盤構築 (推定: 3-4時間) ✅ 完了

### ゴール
Azure Translator Service と Azure Blob Storage を統合し、ドキュメント翻訳の基盤を構築

**進捗**: Phase 16 完了 🎉 → 次は Phase 17 (UI と API エンドポイントの実装)

---

### Step 16.1: Azure リソースの準備 (1時間) ✅ 完了

#### タスク
- [x] Azure Portal で Azure Translator リソースを確認または作成
- [x] Azure Storage Account の確認または作成
- [x] Storage Account にコンテナを作成（source, target）
- [x] エンドポイント URL、リージョン、キーを取得
- [x] `appsettings.Development.json` に設定項目を追加

#### Azure Portal での作業手順

1. **Translator リソースの確認・作成**
   ```
   Azure Portal → AI + Machine Learning → Translator
   リソースの「キーとエンドポイント」から以下を取得:
   - エンドポイント URL
   - リージョン
   - キー（または Entra ID 認証を使用）
   ```

2. **Storage Account の確認・作成**
   ```
   Azure Portal → Storage accounts
   新規作成または既存を使用:
   - アカウント名をメモ
   - アクセスキーまたは Entra ID 認証を設定
   ```

3. **Blob コンテナの作成**
   ```
   Storage Account → Containers → 以下を作成:
   - translator-source（翻訳元ドキュメント用）
   - translator-target（翻訳済みドキュメント用）
   ※ 1つのファイルごとに一時的に使用します
   ```

4. **ロール割り当ての設定（重要）**
   
   **ローカル開発環境の場合:**
   - Storage Account (`sttest7docs`) → アクセス制御 (IAM) → ロールの割り当て
     - ロール: `Storage Blob Data Contributor`
     - メンバー: 自分の Azure ユーザーアカウント
   - Translator リソース → アクセス制御 (IAM) → ロールの割り当て
     - ロール: `Cognitive Services User`
     - メンバー: 自分の Azure ユーザーアカウント
   
   **App Service デプロイ時の場合:**
   - App Service → ID → システム割り当てマネージド ID を「オン」に設定
   - Storage Account (`sttest7docs`) → アクセス制御 (IAM) → ロールの割り当て
     - ロール: `Storage Blob Data Contributor`
     - メンバー: App Service のマネージド ID
   - Translator リソース → アクセス制御 (IAM) → ロールの割り当て
     - ロール: `Cognitive Services User`
     - メンバー: App Service のマネージド ID
   
   ※ `DefaultAzureCredential` が環境に応じて自動的に適切な認証を選択します

#### 設定ファイルの更新

**ファイル: `appsettings.Development.json`**

既存の設定に以下を追加（既存の `AzureOpenAI` と同じオブジェクト構造で統一）:

```json
{
  "AzureTranslator": {
    "Endpoint": "https://api.cognitive.microsofttranslator.com/",
    "Region": "japaneast"
  },
  "AzureStorage": {
    "AccountName": "your-storage-account-name",
    "SourceContainerName": "translator-source",
    "TargetContainerName": "translator-target"
  }
}
```

**注意**: 
- 既存の `DocumentIntelligence_Endpoint` と `AzureOpenAI` の設定は変更しません
- **認証方式**: Entra ID 認証（DefaultAzureCredential）を使用 ✅
- **必須ロール割り当て**:
  - Translator リソース: 「Cognitive Services User」ロール
  - Storage Account: 「Storage Blob Data Contributor」ロール
  - Storage Account: 「Storage Blob Delegator」ロール（ユーザー委任 SAS 生成用）← **重要**
  - ローカル開発: 自分の Azure ユーザーアカウントに割り当て
  - App Service: マネージド ID に割り当て

#### 検証
- [x] Azure Translator リソースが存在している
- [x] Azure Storage Account が存在している
- [x] Blob コンテナ（source, target）が作成されている
- [x] エンドポイント URL とリージョンが取得できている
- [x] 認証方式が決定している（**Entra ID 認証を使用**）
- [x] ロール割り当てが完了している（ローカル開発または App Service マネージド ID）
- [x] appsettings.Development.json が更新されている

**実装完了日**: 2026年2月2日  
**認証方式**: Entra ID 認証（DefaultAzureCredential）  
**ロール割り当て**: Storage Blob Data Contributor, Cognitive Services User

---

### Step 16.2: NuGet パッケージのインストール (30分) ✅ 完了

#### タスク
- [x] `Azure.AI.Translation.Document` パッケージをインストール
- [x] `Azure.Storage.Blobs` パッケージをインストール（既にインストール済みの可能性あり）
- [x] 依存関係の確認

#### コマンド

```bash
cd src/WebApp
dotnet add package Azure.AI.Translation.Document
dotnet add package Azure.Storage.Blobs
dotnet restore
dotnet build
```

#### パッケージ情報
- **Azure.AI.Translation.Document**: Azure Translator の Document Translation API SDK
- **Azure.Storage.Blobs**: Azure Blob Storage SDK
- **推奨バージョン**: 最新の安定版

#### 検証
- [x] パッケージが `WebApp.csproj` に追加されている ✅
- [x] ビルドが成功する ✅
- [x] 依存関係の競合がない ✅

**実装完了日**: 2026年2月2日  
**インストールされたパッケージ**:
- Azure.AI.Translation.Document 2.0.0
- Azure.Storage.Blobs 12.27.0

---

### Step 16.3: サービスインターフェースの作成 (30分) ✅ 完了

#### タスク
- [x] `ITranslatorService.cs` インターフェースの作成
- [x] メソッドシグネチャの定義

#### ファイル: `Services/ITranslatorService.cs`

以下のメソッドを定義:
- `Task<TranslationResult> TranslateDocumentAsync(IFormFile document, string targetLanguage, string? sourceLanguage = null)` - ドキュメントを翻訳し、完了まで待機して結果を返す
- `Task<bool> ValidateDocumentAsync(IFormFile document)` - ドキュメントの妥当性を検証
- `Task<Dictionary<string, string>> GetSupportedLanguagesAsync()` - サポートされている言語の辞書（コード→名前）を取得

#### 検証
- [x] ファイルが作成されている ✅
- [x] インターフェースが定義されている ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月2日

---

### Step 16.4: Translator サービスの実装 (1.5時間) ✅ 完了

#### タスク
- [x] `AzureTranslatorService.cs` の実装
- [x] DocumentTranslationClient の初期化
- [x] BlobServiceClient の初期化
- [x] ドキュメント検証ロジックの実装
- [x] ドキュメントアップロードロジックの実装
- [x] 翻訳ジョブ開始ロジックの実装
- [x] 翻訳状態確認ロジックの実装
- [x] 翻訳済みドキュメントダウンロードロジックの実装
- [x] エラーハンドリングの実装

#### ファイル: `Services/AzureTranslatorService.cs`

実装のポイント:
1. **認証**: DefaultAzureCredential を使用（Entra ID 認証） ✅
2. **コンテナの初期化（クリーンアップ）**: 翻訳開始前に source/target コンテナ内の全 Blob を削除し、残留ファイルを防止 ✅
3. **Blob へのアップロード**: ソースコンテナに一時的にドキュメントをアップロード（一意のファイル名を生成）
4. **翻訳ジョブの開始**: Document Translation API を呼び出し
5. **同期的な待機**: `WaitForCompletionAsync()` を使用して翻訳完了まで待機（タイムアウト設定あり）
6. **結果の取得**: ターゲットコンテナから翻訳済みドキュメントをダウンロード
7. **後処理クリーンアップ**: 処理完了後、ソースとターゲットの一時ファイルを削除
8. **エラーハンドリング**: HTTP ステータスコードに応じた詳細なエラーメッセージ
9. **ロギング**: 各ステップでの詳細なログ出力

#### 検証
- [x] ファイルが作成されている ✅
- [x] すべてのメソッドが実装されている ✅
- [x] ビルドが成功する ✅
- [x] エラーハンドリングが実装されている ✅

**実装完了日**: 2026年2月2日  
**実装内容**:
- DocumentTranslationClient と BlobServiceClient の初期化（DefaultAzureCredential）
- ドキュメント検証（40MB 制限、対応形式チェック）
- **翻訳前コンテナクリーンアップ**（CleanupContainerAsync メソッド）
  - source/target コンテナ内の全 Blob を削除
  - 前回の翻訳で残留したファイルを防止
  - クリーンアップ失敗時は警告ログを出力し、翻訳処理は継続
- Blob へのアップロード（一意のファイル名生成）
- **ユーザー委任 SAS トークンの生成**（Entra ID 認証対応）
  - ソースコンテナ: Read + List 権限
  - ターゲットコンテナ: Read + Write + List 権限
- **コンテナベースの URI 使用**（Document Translation API の要件）
- 翻訳ジョブの開始と同期的な待機（WaitForCompletionAsync）
- 翻訳済みドキュメントのダウンロード（ソースと同じファイル名）
- 後処理クリーンアップ（翻訳完了後の一時ファイル削除）
- 詳細なエラーハンドリングとロギング

**重要な実装ポイント**:
- Document Translation API はアカウントキーではなく、ユーザー委任 SAS を使用
- ソースとターゲットの両方でコンテナ URI が必要
- 翻訳後のファイル名はソースファイル名と同じ
- **コンテナクリーンアップ**: 翻訳開始前に両コンテナを初期化することで、エラーやタイムアウト時の残留ファイルを防止

---

### Step 16.5: モデルクラスの作成 (30分) ✅ 完了

#### タスク
- [x] `TranslationResult.cs` の作成

#### ファイル: `Models/TranslationResult.cs`

必要なプロパティ:
- `string OriginalFileName`: 元のファイル名
- `string TranslatedFileName`: 翻訳後のファイル名
- `byte[] TranslatedContent`: 翻訳済みドキュメントのバイナリデータ
- `string ContentType`: ファイルの MIME タイプ
- `string SourceLanguage`: 翻訳元言語（検出された言語）
- `string TargetLanguage`: 翻訳先言語
- `int CharactersTranslated`: 翻訳された文字数
- `DateTime StartedAt`: 開始時刻
- `DateTime CompletedAt`: 完了時刻
- `TimeSpan Duration`: 処理時間

#### 検証
- [x] ファイルが作成されている ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月2日

---

### Step 16.6: Program.cs への依存性注入の追加 (15分) ✅ 完了

#### タスク
- [x] Program.cs に ITranslatorService の DI 登録を追加

#### ファイル: `Program.cs` の更新

既存のサービス登録の後に追加:

```csharp
// Azure Translator サービスの登録
builder.Services.AddScoped<ITranslatorService, AzureTranslatorService>();
```

#### 検証
- [x] Program.cs が正しく更新されている ✅
- [x] ビルドが成功する ✅
- [x] アプリケーションが起動する ✅

**実装完了日**: 2026年2月2日

---

### Phase 16 完了チェックリスト

- [x] Azure Translator リソースが準備されている ✅
- [x] Azure Storage Account とコンテナが作成されている ✅
- [x] appsettings.Development.json に設定が追加されている ✅
- [x] Azure.AI.Translation.Document パッケージがインストールされている ✅
- [x] Azure.Storage.Blobs パッケージがインストールされている ✅
- [x] ITranslatorService インターフェースが作成されている ✅
- [x] AzureTranslatorService が実装されている ✅
- [x] モデルクラスが作成されている ✅
- [x] Program.cs に DI が登録されている ✅
- [x] ビルドが成功する ✅
- [x] エラーがない ✅

**実装完了日**: 2026年2月2日  
**Phase 16 完全完了！** 🎉

---

## Phase 17: UI と API エンドポイントの実装 (推定: 3-4時間)

### ゴール
Azure Translator を使用したドキュメント翻訳機能の UI とバックエンド API を実装

---

### Step 17.1: PageModel の実装 (1時間) ✅ 完了

#### タスク
- [x] `Pages/Translator/Index.cshtml.cs` の作成
- [x] 翻訳処理のハンドラー実装（同期的に完了を待機し、ファイルを返却）
- [x] 言語一覧取得のハンドラー実装
- [x] エラーハンドリングの実装

#### ファイル: `Pages/Translator/Index.cshtml.cs`

実装するハンドラー:
1. `OnPostTranslateAsync(IFormFile document, string targetLanguage, string? sourceLanguage)`
   - ドキュメントの検証
   - 翻訳サービスを呼び出し（完了まで待機）
   - 翻訳済みファイルを直接返却（FileResult）
   - エラー時は JSON でエラー情報を返却

2. `OnGetLanguagesAsync()`
   - サポートされている言語一覧の取得
   - JSON で返却（言語コードと言語名の辞書）

#### 検証
- [x] ファイルが作成されている ✅
- [x] すべてのハンドラーが実装されている ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月2日  
**実装内容**:
- OnGetLanguagesAsync() - サポート言語一覧を JSON で返却
- OnPostTranslateAsync() - ドキュメント翻訳を実行し、FileResult で翻訳済みファイルを返却
- ダウンロードファイル名を改善：`元のファイル名_言語コード.拡張子`
- 翻訳情報をカスタムヘッダーとして追加（文字数、処理時間、言語）
- バリデーションとエラーハンドリング

---

### Step 17.2: Razor Page の実装 (1.5時間) ✅ 完了

#### タスク
- [x] `Pages/Translator/Index.cshtml` の作成
- [x] UI の実装（OCR ページと同様の構造）
- [x] ファイルアップロードエリアの作成
- [x] 言語選択ドロップダウンの実装
- [x] 翻訳結果表示エリアの実装
- [x] ダウンロードボタンの実装

#### ファイル: `Pages/Translator/Index.cshtml`

UI コンポーネント:
1. **左側カラム: アップロードエリア**
   - ドラッグ&ドロップエリア
   - ファイル選択ボタン
   - ファイルプレビュー
   - 翻訳元言語選択（オプション、自動検出がデフォルト）
   - 翻訳先言語選択（必須）
   - 翻訳実行ボタン

2. **右側カラム: 結果表示エリア**
   - 初期状態メッセージ
   - 翻訳中のローディング表示（スピナーとステータスメッセージ）
   - 翻訳完了メッセージ（自動ダウンロード開始）
   - 翻訳情報（言語、ファイル名、文字数、処理時間）

3. **対応ファイル形式の表示**
   - PDF, DOCX, XLSX, PPTX, HTML, TXT

#### 検証
- [x] ファイルが作成されている ✅
- [x] HTML 構造が正しい ✅
- [x] OCR ページと同様のデザイン ✅

**実装完了日**: 2026年2月2日  
**実装内容**:
- 2カラムレイアウト（左: アップロード＆言語選択、右: 結果表示）
- ドラッグ&ドロップエリア
- 翻訳元言語（自動検出）と翻訳先言語の選択ドロップダウン
- ドキュメント情報表示（ファイル名、サイズ、形式、アイコン）
  - テーブル形式ではなく縦書き形式で表示
  - ファイル名は改行で表示、サイズと形式は同じ行に表示
- 翻訳結果表示（ローディング、成功メッセージ、翻訳情報）
  - 翻訳文字数：カンマ区切りで表示
  - 処理時間：秒または分秒で表示
- 紫色のグラデーション背景

---

### Step 17.3: JavaScript の実装 (1.5時間) ✅ 完了

#### タスク
- [x] `wwwroot/js/translator.js` の作成
- [x] ファイルアップロード処理
- [x] ドラッグ&ドロップ処理
- [x] 言語選択処理
- [x] フォーム送信処理
- [x] ポーリング処理（翻訳状態の定期確認）
- [x] 結果表示処理
- [x] ダウンロード処理

#### ファイル: `wwwroot/js/translator.js`

実装する機能:
1. **TranslatorApp クラス**
   - ファイル選択・ドラッグ&ドロップ
   - 言語ドロップダウンの動的生成（ページ読み込み時に API から取得）
   - フォーム送信

2. **同期的な翻訳処理**
   - フォーム送信後、ローディング表示
   - バックエンドの完了を待機（長時間実行に対応）
   - 完了後、ブラウザの自動ダウンロード機能で翻訳済みファイルをダウンロード
   - 翻訳情報（言語、文字数、処理時間）を表示

3. **エラーハンドリング**
   - 詳細なエラーメッセージ表示
   - タイムアウトエラーの処理

#### 検証
- [x] ファイルが作成されている ✅
- [x] JavaScript 構文が正しい ✅
- [x] ocr-app.js と同様の構造 ✅

**実装完了日**: 2026年2月2日  
**実装内容**:
- TranslatorApp クラス
- 言語一覧の動的読み込み（ページ読み込み時に API から取得）
- ドラッグ&ドロップ処理
- ファイル選択とドキュメント情報表示
- 翻訳リクエスト（同期的な完了待機）
- 自動ダウンロード処理（FileResult を受信してダウンロード）
- ファイルアイコンの動的表示
- カスタムヘッダーから翻訳情報を取得（文字数、処理時間）
  - 文字数をカンマ区切りでフォーマットして表示
  - 60秒未満は秒表示、60秒以上は分秒表示
- エラーハンドリング

---

### Step 17.4: ナビゲーションの更新 (30分) ✅ 完了

#### タスク
- [x] `_Layout.cshtml` にナビゲーションリンクを追加
- [x] `Pages/Index.cshtml` にカードを追加（横スクロール対応）
- [x] カードレイアウトを将来の拡張に対応させる
- [x] サブタイトルと使い方のヒントセクションを更新

#### ファイル: `Pages/Shared/_Layout.cshtml` の更新

ナビゲーションメニューに Translator リンクを追加:
```html
<li class="nav-item">
    <a class="nav-link text-dark" asp-area="" asp-page="/Translator/Index">Translator</a>
</li>
```

#### ファイル: `Pages/Index.cshtml` の更新

**設計方針**:
- カードの幅は固定幅550pxで統一（テキスト折り返しを防ぎ、一貫性を保つ）
- 横スクロール可能なレイアウトに変更（将来の機能追加に対応）
- Bootstrap の `.overflow-auto` を活用
- 各カードは `flex: 0 0 auto` で幅を固定

**新しいカード内容**:
- **タイトル**: ドキュメント翻訳 (Translator)
- **アイコン**: translate または globe
- **特徴**: 
  - 100以上の言語間での翻訳
  - ドキュメント形式を保持
  - 即座に翻訳完了
- **ボタン**: 紫色 (info)

**セクション更新**:
1. **サブタイトルの更新**
   - 「Azure AI を活用した OCR とドキュメント処理」
   → 「Azure AI を活用した OCR、翻訳、ドキュメント処理」

2. **使い方のヒントセクション**
   - OCR 用途の説明
   - GPT-4o 用途の説明
   - **新規追加**: Translator 用途の説明
     - ビジネスドキュメントの多言語化
     - 契約書・仕様書の翻訳
     - マニュアルのローカライゼーション

**実装例（横スクロール対応）**:
```html
<div class="row">
    <div class="col-12">
        <h2 class="text-center mb-4">機能一覧</h2>
        <p class="text-center text-muted mb-4">Azure AI を活用した OCR、翻訳、ドキュメント処理</p>
    </div>
</div>

<div class="row">
    <div class="col-12">
        <div class="d-flex overflow-auto pb-3" style="gap: 1rem;">
            <!-- Document Intelligence カード -->
            <div class="card shadow-sm" style="width: 550px; flex: 0 0 auto;">
                <!-- カード内容 -->
            </div>
            
            <!-- GPT-4o カード -->
            <div class="card shadow-sm" style="width: 550px; flex: 0 0 auto;">
                <!-- カード内容 -->
            </div>
            
            <!-- Translator カード（新規） -->
            <div class="card shadow-sm" style="width: 550px; flex: 0 0 auto;">
                <div class="card-body">
                    <h5 class="card-title">
                        <i class="bi bi-translate"></i> ドキュメント翻訳
                    </h5>
                    <h6 class="card-subtitle mb-3 text-muted">Azure Translator</h6>
                    <p class="card-text">Azure Translator を使用したドキュメント翻訳</p>
                    <ul class="small">
                        <li>100以上の言語間での翻訳</li>
                        <li>ドキュメント形式を保持</li>
                        <li>即座に翻訳完了</li>
                    </ul>
                    <a asp-page="/Translator/Index" class="btn btn-info">使ってみる</a>
                </div>
            </div>
        </div>
    </div>
</div>
```

**CSS 追加（site.css）**:
```css
/* 横スクロールのスムーズ化 */
.overflow-auto {
    scroll-behavior: smooth;
    -webkit-overflow-scrolling: touch;
}

/* スクロールバーのスタイリング（オプション） */
.overflow-auto::-webkit-scrollbar {
    height: 8px;
}

.overflow-auto::-webkit-scrollbar-track {
    background: #f1f1f1;
    border-radius: 4px;
}

.overflow-auto::-webkit-scrollbar-thumb {
    background: #888;
    border-radius: 4px;
}

.overflow-auto::-webkit-scrollbar-thumb:hover {
    background: #555;
}
```

#### 検証
- [x] ナビゲーションが更新されている
- [x] ホームページに Translator カードが追加されている
- [x] カードが横スクロール可能になっている
- [x] カードの幅が統一されている
- [x] サブタイトルが更新されている
- [x] 使い方のヒントが更新されている
- [x] リンクが正しく動作する
- [x] モバイル表示でも適切にスクロールできる

**実装完了日**: 2026年2月2日  
**実装内容**:
- `_Layout.cshtml` にナビゲーションリンク追加（Translator）
- `Pages/Index.cshtml` を横スクロール対応レイアウトに変更
- Translator カードを追加（btn-info で統一）
- サブタイトル更新：「OCR、翻訳、ドキュメント処理」
- 使い方のヒントに Translator 用途追加（ビジネスドキュメント、契約書、マニュアル）
- 対応フォーマットに Translator 対応形式を追加
- `site.css` に横スクロール用 CSS を追加（スムーズスクロール、スクロールバースタイリング）
- **カード幅の最適化**: テキスト折り返しを防ぐため、固定幅を段階的に調整（350px → 420px → 480px → 520px → 550px）し、最終的に550pxで統一

---

### Phase 17 完了チェックリスト

- [x] Pages/Translator/Index.cshtml.cs PageModel が実装されている ✅
- [x] Pages/Translator/Index.cshtml UI が作成されている ✅
- [x] wwwroot/js/translator.js JavaScript が実装されている ✅
- [x] ナビゲーションが更新されている（_Layout.cshtml と Index.cshtml）✅
- [x] ビルドが成功する ✅
- [x] アプリケーションが起動する ✅
- [x] 翻訳機能が動作する ✅

**実装完了日**: 2026年2月2日（Phase 17 完全完了！）🎉

---

## Phase 18: テストとデバッグ (推定: 2-3時間)

### ゴール
実装した Translator 機能をテストし、問題を修正

---

### Step 18.1: 統合テスト (1.5時間) ⬜ 未着手

#### テストシナリオ

##### 1. 正常系テスト

**テストケース 1.1: PDF ドキュメントの翻訳（日→英）**
- [ ] 日本語 PDF ファイルをアップロード
- [ ] 翻訳先言語: 英語 (en)
- [ ] ローディング表示が表示される
- [ ] 翻訳完了後、ファイルが自動的にダウンロードされる
- [ ] ダウンロードした PDF が英語になっている
- [ ] 翻訳情報（文字数、処理時間）が表示される

**テストケース 1.2: Word ドキュメントの翻訳（英→日）**
- [ ] 英語 DOCX ファイルをアップロード
- [ ] 翻訳先言語: 日本語 (ja)
- [ ] 翻訳が成功する
- [ ] 形式が保持されている

**テストケース 1.3: 自動言語検出**
- [ ] 翻訳元言語を「自動検出」に設定
- [ ] 翻訳が正しく実行される

**テストケース 1.4: ドラッグ&ドロップ**
- [ ] ファイルをドラッグ&ドロップ
- [ ] ファイル名が表示される
- [ ] 翻訳が成功し、ファイルがダウンロードされる

##### 2. 異常系テスト

**テストケース 2.1: ファイル未選択**
- [ ] ファイルを選択せずに送信
- [ ] エラーメッセージが表示される

**テストケース 2.2: サイズ超過**
- [ ] 制限サイズを超えるファイルをアップロード
- [ ] エラーメッセージが表示される

**テストケース 2.3: 非対応形式**
- [ ] ZIP や実行ファイルなどの非対応形式をアップロード
- [ ] エラーメッセージが表示される

**テストケース 2.4: Azure API エラー**
- [ ] 無効な設定でエラーを発生させる
- [ ] 適切なエラーメッセージが表示される

#### よくある問題と解決策

| 問題 | 原因 | 解決策 |
|------|------|--------|
| 401 Unauthorized | 認証エラー | Translator の認証設定を確認 |
| 403 Forbidden | コンテナへのアクセス権限不足 | Storage Account の RBAC 設定を確認 |
| 404 Not Found | コンテナが存在しない | Blob コンテナの作成を確認 |
| タイムアウト | 大きいファイルの処理時間超過 | ポーリング間隔とタイムアウト時間を調整 |

#### 検証
- [ ] すべての正常系テストが成功
- [ ] 異常系のエラーハンドリングが機能している

---

### Step 18.2: パフォーマンステスト (1時間) ⬜ 未着手

#### テスト項目

1. **処理時間の測定**
   - [ ] 小さいファイル（～1MB）の翻訳時間を計測
   - [ ] 大きいファイル（～10MB）の翻訳時間を計測
   - [ ] 複数ページの PDF の翻訳時間を計測

2. **コストの見積もり**
   - [ ] 翻訳した文字数を確認
   - [ ] 料金を計算

3. **同時リクエストのテスト**
   - [ ] 複数のブラウザタブで同時にリクエスト
   - [ ] ジョブが正しく管理されているか確認

#### 期待される結果

- **処理時間**: 1-5分（ファイルサイズと言語による）
- **文字数**: ファイル内容による
- **エラーレート**: 0%（正常な環境下）

#### 検証
- [ ] パフォーマンス測定が完了
- [ ] コスト見積もりが完了
- [ ] 同時リクエストのテストが成功

---

### Phase 18 完了チェックリスト

- [ ] すべての正常系テストが成功
- [ ] 異常系テストが成功
- [ ] エラーハンドリングが機能している
- [ ] パフォーマンステストが完了
- [ ] ログが適切に出力されている
- [ ] ドキュメント翻訳が正しく機能している

**実装完了日**: ___________

---

## Phase 19: ドキュメント作成（オプション） (推定: 1-2時間)

### Step 19.1: README.md の更新 ⬜ 未着手

#### タスク
- [ ] Translator 機能の追加を README.md に記載
- [ ] サポートされている言語の一覧を追加
- [ ] 対応ファイル形式の一覧を追加

#### 検証
- [ ] README.md が更新されている

---

### Step 19.2: 使い方ガイドの作成 ⬜ 未着手

#### タスク
- [ ] ユーザー向けの使い方ガイドを作成（オプション）
- [ ] スクリーンショットの追加（オプション）

#### 検証
- [ ] 使い方ガイドが作成されている

---

### Phase 19 完了チェックリスト

- [ ] README.md が更新されている
- [ ] 使い方ガイドが作成されている（オプション）

**実装完了日**: ___________

---

## Phase 19.5: ヘルスチェックと Warmup の実装 (推定: 1時間) ✅ 完了

### ゴール
Azure Translator と Azure Blob Storage のヘルスチェック、および Warmup エンドポイントに翻訳サービスの初期化確認を追加

---

### Step 19.5.1: Azure Translator ヘルスチェックの実装 ✅ 完了

#### タスク
- [x] `Services/HealthChecks/AzureTranslatorHealthCheck.cs` の作成
- [x] Translator API の言語一覧エンドポイントで接続確認

#### ファイル: `Services/HealthChecks/AzureTranslatorHealthCheck.cs`

実装のポイント:
- `IHealthCheck` インターフェースを実装
- `AzureTranslator:Endpoint` と `AzureTranslator:Region` の設定確認
- `/languages?api-version=3.0` エンドポイントで接続確認
- タイムアウト: 5秒

#### 検証
- [x] ファイルが作成されている ✅
- [x] ヘルスチェックが動作する ✅

**実装完了日**: 2026年2月5日

---

### Step 19.5.2: Azure Blob Storage ヘルスチェックの実装 ✅ 完了

#### タスク
- [x] `Services/HealthChecks/AzureBlobStorageHealthCheck.cs` の作成
- [x] ストレージアカウントへの接続確認
- [x] 翻訳用コンテナ（source, target, translated）の存在確認

#### ファイル: `Services/HealthChecks/AzureBlobStorageHealthCheck.cs`

実装のポイント:
- `IHealthCheck` インターフェースを実装
- `DefaultAzureCredential` で認証（Entra ID）
- `BlobServiceClient.GetPropertiesAsync()` で接続確認
- 各コンテナの存在確認（source, target, translated）
- RBAC 権限エラーの詳細なエラーメッセージ

#### 検証
- [x] ファイルが作成されている ✅
- [x] ヘルスチェックが動作する ✅

**実装完了日**: 2026年2月5日

---

### Step 19.5.3: Program.cs へのヘルスチェック登録 ✅ 完了

#### タスク
- [x] `AzureTranslatorHealthCheck` の登録
- [x] `AzureBlobStorageHealthCheck` の登録

#### コード変更

```csharp
// ヘルスチェックの登録
builder.Services.AddHealthChecks()
    .AddCheck<DocumentIntelligenceHealthCheck>(
        "document_intelligence",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureOpenAIHealthCheck>(
        "azure_openai",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureTranslatorHealthCheck>(          // 新規追加
        "azure_translator",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" })
    .AddCheck<AzureBlobStorageHealthCheck>(         // 新規追加
        "azure_blob_storage",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "external" });
```

#### 検証
- [x] Program.cs が更新されている ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月5日

---

### Step 19.5.4: Warmup エンドポイントの拡張 ✅ 完了

#### タスク
- [x] Azure Translator の接続確認を追加
- [x] Azure Blob Storage の接続確認を追加

#### Warmup で確認するサービス

| サービス | 確認内容 | 状態 |
|----------|----------|------|
| Document Intelligence | エンドポイント接続 | 既存 |
| OCR サービス | DI 初期化 | 既存 |
| Azure OpenAI | エンドポイント接続 | 既存 |
| GPT Vision サービス | DI 初期化 | 既存 |
| **Azure Translator** | 言語一覧 API 接続 | ✅ 新規追加 |
| **Azure Blob Storage** | アカウント接続 | ✅ 新規追加 |

#### 検証
- [x] Warmup エンドポイントが更新されている ✅
- [x] 翻訳サービスの初期化が確認される ✅

**実装完了日**: 2026年2月5日

---

### Phase 19.5 完了チェックリスト

- [x] AzureTranslatorHealthCheck が実装されている ✅
- [x] AzureBlobStorageHealthCheck が実装されている ✅
- [x] Program.cs にヘルスチェックが登録されている ✅
- [x] Warmup エンドポイントが拡張されている ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月5日

---

## 📊 実装完了後の全体構成

```
src/WebApp/
├── Services/
│   ├── IOcrService.cs                    # Document Intelligence インターフェース
│   ├── DocumentIntelligenceService.cs    # Document Intelligence 実装
│   ├── IGptVisionService.cs              # GPT-4o インターフェース
│   ├── OpenAIVisionService.cs            # GPT-4o 実装
│   ├── ITranslatorService.cs             # Translator インターフェース（新規）
│   ├── AzureTranslatorService.cs         # Translator 実装（新規）
│   └── HealthChecks/
│       ├── DocumentIntelligenceHealthCheck.cs
│       ├── AzureOpenAIHealthCheck.cs
│       ├── AzureTranslatorHealthCheck.cs     # 新規追加
│       └── AzureBlobStorageHealthCheck.cs    # 新規追加
├── Models/
│   ├── OcrResult.cs                      # Document Intelligence 結果
│   ├── VisionOcrResult.cs                # GPT-4o 結果
│   ├── TranslationResult.cs              # Translator 結果（新規）
│   ├── OcrError.cs
│   └── FileUploadOptions.cs
├── Pages/
│   ├── Index.cshtml                      # ホームページ
│   ├── Index.cshtml.cs
│   ├── OCR/                              # Document Intelligence 機能
│   │   ├── Index.cshtml
│   │   └── Index.cshtml.cs
│   ├── GPT/                              # GPT-4o 機能
│   │   ├── Index.cshtml
│   │   └── Index.cshtml.cs
│   ├── Translator/                       # Translator 機能（新規）
│   │   ├── Index.cshtml
│   │   └── Index.cshtml.cs
│   └── Shared/
│       └── _Layout.cshtml
├── wwwroot/
│   ├── css/
│   │   └── site.css
│   └── js/
│       ├── ocr-app.js                    # Document Intelligence JS
│       ├── gpt-vision.js                 # GPT-4o JS
│       └── translator.js                 # Translator JS（新規）
└── Program.cs
```

---

## 🔧 実装チェックリスト（全体）

### Phase 16: Azure Translator 基盤構築 ✅ 完了
- [x] Azure Translator リソースの準備 ✅
- [x] Azure Storage Account とコンテナの作成 ✅
- [x] appsettings.Development.json の設定 ✅
- [x] Azure.AI.Translation.Document パッケージのインストール ✅
- [x] Azure.Storage.Blobs パッケージのインストール ✅
- [x] ITranslatorService インターフェースの作成 ✅
- [x] AzureTranslatorService の実装 ✅
- [x] モデルクラスの作成 ✅
- [x] Program.cs への DI 登録 ✅

### Phase 17: UI と API 実装 ✅ 完了
- [x] Pages/Translator/Index.cshtml.cs PageModel の実装 ✅
- [x] Pages/Translator/Index.cshtml UI の作成 ✅
- [x] wwwroot/js/translator.js JavaScript の実装 ✅
- [x] ナビゲーションの更新（_Layout.cshtml と Index.cshtml）✅

### Phase 18: テストとデバッグ ⬜ 未着手
- [ ] 統合テストの実施（正常系・異常系）
- [ ] パフォーマンステスト

### Phase 19: ドキュメント（オプション） ⬜ 未着手
- [ ] README.md の更新
- [ ] 使い方ガイドの作成

---

## 💡 実装のヒント

### Azure Translator 使用時の注意点

#### 1. コスト管理
- **翻訳料金**: 文字数ベース（100万文字あたり $10-15）
- **ストレージ料金**: Blob Storage の使用料金
- **推定コスト**: $0.01-0.50/ドキュメント（サイズによる）

#### 2. 対応ファイル形式
- **ドキュメント**: PDF, DOCX, XLSX, PPTX
- **Web**: HTML, HTM
- **テキスト**: TXT, TSV, CSV
- **最大サイズ**: 40MB/ファイル

#### 3. サポート言語
- **100以上の言語**: ja, en, zh-Hans, zh-Hant, ko, fr, de, es, it, pt, ru など
- **自動検出**: 翻訳元言語の自動検出に対応

#### 4. 翻訳品質
- **ニューラル翻訳**: 高品質な翻訳
- **ドメイン特化**: カスタム翻訳モデルの作成も可能（Advanced）
- **用語集**: カスタム用語集の使用も可能（Advanced）

### おすすめの使用例

```
# ビジネスドキュメント
契約書、仕様書、プレゼン資料などの翻訳

# 多言語対応
製品マニュアルの多言語展開

# コンテンツローカライゼーション
Web サイトやアプリのコンテンツ翻訳
```

---

## 📈 推定作業時間まとめ

| Phase | 内容 | 時間 | 状態 |
|-------|------|------|------|
| Phase 16 | Azure Translator 基盤構築 | 3-4時間 | ⬜ 未着手 |
| Phase 17 | UI と API 実装 | 3-4時間 | ⬜ 未着手 |
| Phase 18 | テストとデバッグ | 2-3時間 | ⬜ 未着手 |
| Phase 19 | ドキュメント（オプション） | 1-2時間 | ⬜ 未着手 |
| **合計** | | **9-13時間** | |

---

## 🎯 次のステップ（将来の拡張）

### 短期的な拡張（1-2週間）
1. **カスタム用語集**: ドメイン特化用語の登録
2. **翻訳履歴**: 過去の翻訳結果の保存と表示
3. **プレビュー機能**: ドキュメントのサムネイル表示

### 中期的な拡張（1-2ヶ月）
1. **バッチ翻訳**: 複数ファイルの一括翻訳（非同期処理）
2. **カスタムモデル**: Fine-tuning による翻訳品質向上
3. **翻訳前後の比較**: 元のドキュメントと翻訳済みドキュメントの並列表示
4. **API 公開**: REST API エンドポイントの提供

### 長期的な拡張（3-6ヶ月）
1. **リアルタイム翻訳**: WebSocket を使用したストリーミング翻訳
2. **音声翻訳**: Speech Service との統合
3. **OCR + 翻訳**: Document Intelligence で OCR → Translator で翻訳のワークフロー

---

**最終更新日**: 2026年2月2日  
**推定合計時間**: 9-13 時間  
**難易度**: 中級～上級
