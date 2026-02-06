# GPT-4o によるドキュメント翻訳機能 - 段階的実装計画

**作成日**: 2026年2月3日  
**対象**: GPT-4o を使用したドキュメント翻訳機能の追加  
**状態**: ✅ Phase 20-22 完了、画像保持機能実装済み → Phase 23 は App Service 展開後  
**認証方式**: Entra ID 認証（DefaultAzureCredential）  
**最終更新**: 2026年2月5日（PDF 変換機能を削除、ブラウザ印刷に変更、画像保持機能を実装）

---

## 📋 背景と目的

既存の Azure Translator によるドキュメント翻訳機能に加え、GPT-4o を使用した翻訳機能を追加します。

### Azure Translator との比較

| 項目 | Azure Translator | GPT-4o 翻訳 |
|------|-----------------|-------------|
| 書式保持 | ✅ 完全保持 | △ Markdown で構造保持 |
| 翻訳品質 | 高い | 非常に高い（コンテキスト理解） |
| カスタマイズ | 用語集のみ | プロンプトで柔軟に調整可能 |
| Storage 依存 | 必要 | 不要 |
| ネットワーク制限 | 複雑な設定が必要 | シンプル |
| 出力形式 | 元の形式 | Markdown / HTML（PDF は印刷で対応） |
| コスト | 文字数ベース | トークンベース |

### 対応ファイル形式

| ファイル形式 | 拡張子 | 備考 |
|-------------|--------|------|
| PDF | .pdf | Document Intelligence で解析 |
| Word | .docx | Document Intelligence で解析 |

> **注意**: Excel (.xlsx)、PowerPoint (.pptx)、HTML、TXT などは対象外です。

### 画像の取り扱い

| 項目 | 対応 |
|------|------|
| 画像の抽出 | ✅ Document Intelligence で抽出 |
| 画像の翻訳 | ❌ 翻訳対象外（画像内テキストは翻訳しない） |
| 画像の保持 | ✅ Markdown に埋め込み（レイアウト再現のため） |
| 画像の保存先 | Blob Storage（translated コンテナ内の images/ フォルダ） |

> **重要**: 画像内のテキスト（図表のラベル、キャプションなど）は翻訳されません。
> 画像は元のレイアウトを再現するために Markdown 内で参照されます。

### メリット
- **高品質な翻訳**: コンテキストを理解した自然な翻訳
- **カスタマイズ可能**: 専門用語、トーン、スタイルをプロンプトで指定
- **レイアウト再現**: 見出し、リスト、表、画像配置を Markdown で再現
- **ネットワーク制限に強い**: プライベートエンドポイント経由で Azure OpenAI にアクセス
- **既存インフラ活用**: 既に構築済みの Azure OpenAI 環境を使用

### デメリット
- **完全なレイアウト再現は不可**: 元の PDF/Word と全く同じレイアウトにはならない
- **画像内テキストは翻訳対象外**: 図表内のテキストは翻訳されない
- **トークン制限**: 長いドキュメントは分割処理が必要

### 実装アプローチ
1. **ファイル検証**: PDF または Word (.docx) のみ受け付け
2. **Document Intelligence でテキスト＋構造＋画像を抽出**
   - テキスト: 段落、見出し、リスト、表
   - 画像: 位置情報と画像データを抽出
3. **画像を Blob Storage に保存**（translated/images/ フォルダ）
4. **GPT-4o で翻訳（Markdown 形式）**
   - テキストのみ翻訳（画像内テキストは対象外）
   - 画像は Markdown の画像参照として挿入
5. **翻訳結果（Markdown）を Blob Storage に保存**
6. リンクをクリックして翻訳結果を確認
7. PDF 変換ボタンで Markdown → PDF 変換（オプション）

### ワークフロー概要
```
[ドキュメントアップロード（PDF/Word のみ）]
        ↓
[ファイル形式の検証]
        ↓
[Document Intelligence でテキスト＋構造＋画像を抽出]
        ↓
[画像を Blob Storage に保存（translated/images/）]
        ↓
[GPT-4o でテキストを翻訳（Markdown 形式）]
   ※ 画像内テキストは翻訳対象外
   ※ 画像は Markdown 内で参照（![alt](url)）
        ↓
[翻訳済み Markdown を Blob Storage に保存]
        ↓
[翻訳結果リンクを表示] ← ユーザーがクリックして確認
        ↓
[印刷 / PDF 保存ボタン] ← ブラウザの印刷機能で PDF 保存
```

### 環境設定について

> **注意**: Document Intelligence および Azure OpenAI のエンドポイントは `appsettings.Development.json` に既に登録されています。
> Azure Blob Storage の設定も既存の `AzureStorage` セクションを共用します。

#### 追加の環境変数

既存の `AzureStorage` セクションに `TranslatedContainerName` を追加します：

```json
// appsettings.json または appsettings.Development.json
{
  "AzureStorage": {
    "AccountName": "sttest7docs",
    "SourceContainerName": "source",
    "TargetContainerName": "target",
    "TranslatedContainerName": "translated"  // GPT翻訳結果の保存先コンテナ名（追加）
  }
}
```

または App Service のアプリケーション設定：
```bash
AzureStorage__TranslatedContainerName=translated
```

---

## Phase 20: GPT 翻訳サービスの実装 (推定: 2-3時間)

### ゴール
既存の GPT-4o サービスを拡張し、翻訳機能を追加

---

### Step 20.1: 翻訳インターフェースの拡張 (30分)

#### タスク
- [ ] `IGptTranslatorService.cs` インターフェースの作成
- [ ] メソッドシグネチャの定義

#### ファイル: `Services/IGptTranslatorService.cs`

以下のメソッドを定義:
- `Task<bool> ValidateDocumentAsync(IFormFile document)` - ドキュメントの検証（PDF/Word のみ許可）
- `Task<GptTranslationResult> TranslateTextAsync(string text, string targetLanguage, TranslationOptions? options = null)` - テキストを翻訳
- `Task<GptTranslationResult> TranslateDocumentAsync(IFormFile document, string targetLanguage, TranslationOptions? options = null)` - ドキュメントを翻訳（テキスト抽出 → 画像保存 → 翻訳 → Markdown 保存）
- `Task<string> GetTranslationResultAsync(string blobName)` - Blob Storage から翻訳結果（Markdown）を取得
- `Task<byte[]> ConvertToPdfAsync(string blobName)` - Blob Storage の Markdown を PDF に変換
- `Task<Dictionary<string, string>> GetSupportedLanguagesAsync()` - サポート言語一覧

#### 対応ファイル形式
- **PDF**: `.pdf`
- **Word**: `.docx`
- **その他の形式は拒否**: Excel、PowerPoint、HTML、TXT などはエラーを返す

#### 翻訳結果の保存先
- **コンテナ名**: 環境変数 `AzureStorage:TranslatedContainerName` で設定（デフォルト: `translated`）
- **Markdown ファイル**: `{元ファイル名}_{言語コード}_{タイムスタンプ}.md`
- **画像ファイル**: `images/{元ファイル名}_{タイムスタンプ}/{image_001.png, image_002.png, ...}`
- **例**: 
  - Markdown: `document_en_20260203120000.md`
  - 画像: `images/document_20260203120000/image_001.png`

#### モデル: `TranslationOptions`
- `string? SourceLanguage` - 翻訳元言語（オプション、自動検出）
- `string? Tone` - トーン（formal, casual, technical など）
- `string? Domain` - ドメイン（legal, medical, technical など）
- `string? CustomInstructions` - カスタム指示
- `bool PreserveFormatting` - 構造（見出し、リスト）を保持するか

#### 検証
- [ ] ファイルが作成されている
- [ ] インターフェースが定義されている
- [ ] ビルドが成功する

---

### Step 20.2: GPT 翻訳サービスの実装 (1.5時間)

#### タスク
- [ ] `GptTranslatorService.cs` の実装
- [ ] Azure OpenAI クライアントの初期化（既存の設定を使用）
- [ ] ファイル形式検証の実装（PDF/Word のみ）
- [ ] 翻訳プロンプトの設計
- [ ] Document Intelligence 連携（テキスト＋画像抽出）
- [ ] 画像の Blob Storage 保存処理
- [ ] 長文分割処理の実装
- [ ] エラーハンドリングの実装

#### ファイル: `Services/GptTranslatorService.cs`

実装のポイント:

1. **ファイル形式検証**
   - PDF (.pdf) と Word (.docx) のみ許可
   - その他の形式はエラーメッセージを返す
   - ファイルサイズ制限（40MB）

2. **翻訳プロンプト設計**
   - システムプロンプトで翻訳者としての役割を定義
   - 出力形式を Markdown に指定
   - 構造（見出し、リスト、表）を保持するよう指示
   - 画像プレースホルダーの挿入位置を指示

3. **Document Intelligence 連携**
   - **prebuilt-layout モデル**を使用（画像抽出対応）
   - テキスト抽出: 段落、見出し、リスト、表
   - 画像抽出: 位置情報と画像データ（Base64）
   - レイアウト情報を活用して元の構造を再現

4. **画像処理**
   - Document Intelligence から画像を抽出
   - 画像を Blob Storage に保存（`images/{document_id}/image_xxx.png`）
   - Markdown 内で画像を参照（`![図](blob_url)`）
   - **画像内のテキストは翻訳対象外**

5. **長文分割処理**
   - トークン制限（128K）を考慮
   - 段落単位で分割して翻訳
   - 画像プレースホルダーは分割せずに保持
   - 結果を結合

6. **エラーハンドリング**
   - サポート外ファイル形式のエラー
   - レート制限エラーのリトライ
   - トークン超過エラーの処理
   - API エラーの詳細なログ出力

#### プロンプト例

**システムプロンプト**:
```
あなたはプロフェッショナルな翻訳者です。
以下のルールに従って翻訳してください：

1. 原文の意味を正確に保ちながら、自然な表現で翻訳する
2. 文書の構造（見出し、リスト、表）を Markdown 形式で保持する
3. 専門用語は適切に翻訳し、必要に応じて原語を括弧内に残す
4. 文化的なニュアンスを考慮した翻訳を行う
5. 画像プレースホルダー（[IMAGE:xxx]）はそのまま保持し、翻訳しない
6. 元のドキュメントのレイアウト（段落構成、見出し階層）を可能な限り再現する
```

**ユーザープロンプト**:
```
以下のテキストを{targetLanguage}に翻訳してください。

注意事項:
- 画像プレースホルダー [IMAGE:xxx] は翻訳せず、そのままの位置に残してください
- 見出しの階層（#, ##, ### など）を維持してください
- 表形式は Markdown テーブル形式で保持してください

{追加指示（トーン、ドメインなど）}

--- 原文 ---
{text}
```

**画像プレースホルダーの後処理**:
- 翻訳後、`[IMAGE:xxx]` を実際の Markdown 画像参照 `![図xxx](blob_url)` に置換

#### 検証
- [ ] ファイルが作成されている
- [ ] すべてのメソッドが実装されている
- [ ] ビルドが成功する
- [ ] Document Intelligence と連携できている

---

### Step 20.3: モデルクラスの作成 (30分)

#### タスク
- [ ] `GptTranslationResult.cs` の作成
- [ ] `TranslationOptions.cs` の作成

#### ファイル: `Models/GptTranslationResult.cs`

必要なプロパティ:
- `string OriginalFileName`: 元のファイル名
- `string OriginalText`: 元のテキスト
- `string TranslatedText`: 翻訳済みテキスト（Markdown 形式）
- `string SourceLanguage`: 翻訳元言語（検出された言語）
- `string TargetLanguage`: 翻訳先言語
- `string BlobName`: Blob Storage に保存された Markdown ファイル名
- `string BlobUrl`: 翻訳結果の URL（SAS トークン付きまたは公開 URL）
- `List<string> ImageUrls`: Blob Storage に保存された画像の URL 一覧
- `int ImageCount`: 抽出された画像の数
- `int TokensUsed`: 使用したトークン数
- `int CharacterCount`: 文字数
- `DateTime StartedAt`: 開始時刻
- `DateTime CompletedAt`: 完了時刻
- `TimeSpan Duration`: 処理時間

#### ファイル: `Models/ExtractedImage.cs`

画像抽出結果を格納するモデル:
- `string ImageId`: 画像 ID（image_001, image_002, ...）
- `byte[] ImageData`: 画像のバイナリデータ
- `string ContentType`: MIME タイプ（image/png など）
- `int PageNumber`: 画像があるページ番号
- `BoundingBox Position`: 画像の位置情報

#### 検証
- [ ] ファイルが作成されている
- [ ] ビルドが成功する

---

### Phase 20 完了チェックリスト

- [x] IGptTranslatorService インターフェースが作成されている
- [x] GptTranslatorService が実装されている
- [x] モデルクラスが作成されている（GptTranslationResult, GptTranslationOptions, ExtractedImage）
- [x] Document Intelligence と連携できている（prebuilt-layout モデル使用）
- [x] ビルドが成功する
- [x] テキスト翻訳が動作する（TranslateTextAsync, TranslateDocumentAsync 実装済み）

**完了日**: 2026年2月5日

---

## Phase 20.5: 画像保持機能の実装 ✅ 完了

### 変更履歴

> **重要 (2026/02/06)**: PDF ファイルに含まれる複数画像の位置保持問題を解決しました。

#### 問題
- Document Intelligence の `prebuilt-layout` モデル (v3.x SDK) では画像抽出がサポートされていなかった
- 翻訳後の Markdown に画像が含まれず、フォーマットも失われていた
- 2つ目以降の画像が `<table>` タグとして認識され、OCR テキストが残っていた
- GPT がプレースホルダー形式を変更することがあった（`[[IMG_PLACEHOLDER_001]]` → `[IMAGE:001]`）

#### 解決策
- `Azure.AI.DocumentIntelligence` v1.0.0 パッケージを追加（v4.0 API 対応）
- **Markdown 出力形式** (`outputContentFormat: "markdown"`) を使用
- **PdfPig ライブラリ** で PDF から画像を直接抽出（Document Intelligence の Figures API ではなく）
- **図の座標範囲と重なるコンテンツを検出・削除** - 段落、テーブルなども含む
- **複数プレースホルダーパターンに対応** - GPT が形式を変更しても置換可能

#### 実装内容

| 機能 | 説明 |
|------|------|
| Markdown 出力 | Document Intelligence v4.0 の Markdown 形式出力を使用 |
| 画像抽出 | **PdfPig** ライブラリで PDF から画像を直接抽出（ページ番号付き） |
| 画像保存 | Blob Storage の `images/` フォルダに保存、SAS トークン付き URL を生成 |
| 図のスパン取得 | `figures` プロパティから offset/length/座標情報を取得 |
| 重なるコンテンツ検出 | 図の座標範囲と重なる段落・テーブルのスパンも削除対象に含める |
| スパンマージ | 重なり合うスパンを統合し、1つのプレースホルダーにまとめる |
| プレースホルダー置換 | 複数パターンに対応（GPT が形式を変更しても置換可能） |
| 構造保持 | システムプロンプトに Markdown 構造と画像参照を保持する指示を追加 |

#### 追加したパッケージ

```xml
<PackageReference Include="Azure.AI.DocumentIntelligence" Version="1.0.0" />
<PackageReference Include="PdfPig" Version="0.1.9" />
```

#### コード変更

**`GptTranslatorService.cs`** の主な変更:

1. **DocumentIntelligenceClient を使用**
   - v4.0 API の `DocumentIntelligenceClient` を初期化
   - `AnalyzeDocumentAsync` で Markdown 形式出力を指定

2. **PdfPig で画像抽出**
   ```csharp
   // PdfPig で PDF から画像を直接抽出（ページ番号付き）
   var imageInfos = await ExtractImagesFromPdfAsync(fileBytes, documentId, timestamp, cancellationToken);
   ```

3. **図のスパン情報取得**
   ```csharp
   // figures プロパティからスパン情報を取得
   // 図の座標範囲と重なる段落・テーブルのスパンも削除対象に含める
   var figureSpans = ExtractFigureSpans(analyzeResult, extractedMarkdown);
   ```

4. **スパンマージとプレースホルダー置換**
   ```csharp
   // 重なり合うスパンをマージし、各図に1つのプレースホルダーを割り当て
   var (processedMarkdown, placeholderMapping) = ReplaceFigureOcrWithPlaceholders(extractedMarkdown, figureSpans, imageInfos);
   ```

5. **翻訳後のプレースホルダー置換**
   ```csharp
   // GPT がプレースホルダー形式を変更しても置換可能
   // [[IMG_PLACEHOLDER_001]], [IMAGE:001], [IMAGE:IMG_PLACEHOLDER_001] などに対応
   var translatedTextWithImages = ReplacePlaceholdersWithImages(translationResult.TranslatedText, placeholderMapping);
   ```

6. **翻訳時の構造保持**
   - システムプロンプトに「画像プレースホルダー [[IMG_PLACEHOLDER_NNN]] は翻訳せず保持」の指示を追加

#### 画像保存先

| 項目 | パス形式 |
|------|----------|
| コンテナ | `translated` |
| 画像フォルダ | `images/{documentId}_{timestamp}/` |
| 画像ファイル | `{figureId}.png` |
| 例 | `images/sample_20260205120000/1_1.png` |

#### 翻訳ワークフロー（更新版）

```
[ドキュメントアップロード（PDF/Word のみ）]
        ↓
[ファイル形式の検証]
        ↓
[ステップ1: PdfPig で画像を抽出]
   ※ PDF から直接画像を取得（ページ番号付き）
   ※ Blob Storage に保存（SAS トークン付き URL）
        ↓
[ステップ2: Document Intelligence v4.0 で解析]
   ※ outputContentFormat: "markdown"
   ※ 図（figures）のスパン情報を取得
        ↓
[ステップ3: 図のスパン情報取得]
   ※ figures の boundingRegions から座標を取得
   ※ 図の座標範囲と重なる段落・テーブルのスパンも取得
        ↓
[ステップ4: OCR テキスト削除＆プレースホルダー挿入]
   ※ 重なり合うスパンをマージ
   ※ 各図に1つの [[IMG_PLACEHOLDER_NNN]] を割り当て
        ↓
[ステップ5: GPT-4o で翻訳]
   ※ プレースホルダーを保持するよう指示
        ↓
[ステップ6: プレースホルダーを画像 URL に置換]
   ※ GPT が形式を変更しても置換可能
   ※ [[IMG_PLACEHOLDER_001]], [IMAGE:001] などに対応
        ↓
[ステップ7: Blob Storage に保存]
        ↓
[翻訳結果リンクを表示] ← 画像付きで表示
```

#### 検証項目

- [x] `Azure.AI.DocumentIntelligence` パッケージが追加されている
- [x] `PdfPig` パッケージが追加されている
- [x] Markdown 形式で抽出できる
- [x] 画像が Blob Storage に保存される
- [x] 複数画像が正しい位置に表示される
- [x] 図の OCR テキストが削除される（段落・テーブル含む）
- [x] GPT がプレースホルダー形式を変更しても置換できる
- [x] 翻訳結果に画像が含まれる
- [x] ビルドが成功する

**実装完了日**: 2026年2月6日

---

## Phase 21: PDF 出力機能 ~~(削除済み - ブラウザ印刷で代替)~~

### 変更履歴

> **重要 (2026/02/06)**: サーバーサイド PDF 変換機能を削除し、ブラウザの印刷機能で代替しました。

#### 削除理由

Azure App Service (Windows) 環境では、以下の制約により日本語フォントが正しく表示されませんでした：

1. **PuppeteerSharp**: サンドボックス環境で Chromium プロセスが起動不可
2. **PdfSharpCore + MigraDocCore**: TTC フォントの処理を改善しても文字化け（□□□）が発生
   - 游ゴシック、メイリオ等の TTC フォントから TTF を抽出する処理を実装
   - しかし Azure App Service 環境では依然として正しく動作しない

#### 採用した代替方法: ブラウザ印刷

**メリット**:
- ブラウザがシステムフォントを正しく処理
- 日本語・英語・その他言語すべて正しく表示
- サーバーサイドの実装が不要でシンプル
- Azure App Service の制約を回避

**ワークフロー**:
1. 翻訳完了後、「印刷 / PDF 保存」ボタンをクリック
2. 新しいウィンドウで印刷用スタイルの HTML が開く
3. 印刷ダイアログで「PDF として保存」を選択
4. ファイル名を指定して保存

#### 削除したファイル

| ファイル | 説明 |
|---------|------|
| `Services/IPdfConverterService.cs` | PDF 変換サービスインターフェース |
| `Services/PdfConverterService.cs` | PDF 変換サービス実装（約900行） |
| `Models/PdfOptions.cs` | PDF オプションモデル |

#### 変更したファイル

| ファイル | 変更内容 |
|---------|---------|
| `Program.cs` | IPdfConverterService の DI 登録を削除 |
| `Pages/Translator/GPT.cshtml.cs` | PDF 変換ハンドラーを削除 |
| `Pages/Translator/GPT.cshtml` | PDF ボタンを印刷ボタンに変更 |
| `wwwroot/js/gpt-translator.js` | `printPreview()` メソッドを追加 |

#### JavaScript 実装: printPreview()

```javascript
printPreview() {
    const printContent = this.previewContent.innerHTML;
    const printWindow = window.open('', '_blank');
    
    printWindow.document.write(`
        <!DOCTYPE html>
        <html lang="ja">
        <head>
            <meta charset="UTF-8">
            <title>翻訳結果 - 印刷プレビュー</title>
            <style>
                body {
                    font-family: 'Yu Gothic', 'Meiryo', sans-serif;
                    max-width: 800px;
                    margin: 0 auto;
                    padding: 40px;
                    line-height: 1.8;
                }
                /* 印刷用スタイル */
            </style>
        </head>
        <body>${printContent}</body>
        </html>
    `);
    
    printWindow.document.close();
    setTimeout(() => { printWindow.print(); }, 500);
}
```

---

### Phase 21 完了チェックリスト

- [x] ~~PDF 変換ライブラリがインストールされている~~ → 削除済み
- [x] ~~IPdfConverterService インターフェースが作成されている~~ → 削除済み
- [x] ~~PdfConverterService が実装されている~~ → 削除済み
- [x] ブラウザ印刷機能が実装されている (2026/02/06)
- [x] 印刷用スタイルが適用されている (2026/02/06)
- [x] 日本語が正しく表示される（ブラウザ印刷経由） (2026/02/06)

**完了日**: 2026年2月6日（ブラウザ印刷方式に変更）

---

## Phase 22: UI の実装 (推定: 2-3時間)

### ゴール
GPT 翻訳機能の UI を実装

---

### Step 22.1: PageModel の実装 (1時間)

#### タスク
- [x] `Pages/Translator/GPT.cshtml.cs` の作成 (2026/02/05)
- [x] 翻訳処理のハンドラー実装（プロンプト対応）(2026/02/05)
- [x] 出力形式選択の処理 (2026/02/05)
- [x] エラーハンドリングの実装 (2026/02/05)

#### ファイル: `Pages/Translator/GPT.cshtml.cs`

> **参考**: `Pages/OCR/GPT.cshtml.cs` の実装パターンを参考

実装するハンドラー:

1. `OnGetLanguagesAsync()`
   - サポート言語一覧を JSON で返却

2. `OnPostTranslateAsync(IFormFile document, string targetLanguage, string systemPrompt, string userPrompt, TranslationOptions options)`
   - **systemPrompt**: ユーザーが入力したシステムプロンプト（空の場合はデフォルト使用）
   - **userPrompt**: ユーザーが入力したユーザープロンプト（空の場合はデフォルト使用）
   - ドキュメントの翻訳を実行
   - 翻訳結果を Blob Storage に保存
   - 結果として翻訳情報（BlobName, BlobUrl, 処理時間など）を JSON で返却

3. `OnGetResultAsync(string blobName)`
   - Blob Storage から翻訳結果（Markdown）を取得
   - Markdown テキストを JSON で返却

4. `OnPostConvertToPdfAsync(string blobName)`
   - Blob Storage の Markdown を PDF に変換
   - PDF ファイルをダウンロード用に返却

#### デフォルトプロンプト定数

PageModel 内にデフォルトプロンプトを定数として定義:

```csharp
public static class DefaultPrompts
{
    public const string SystemPrompt = @"あなたはプロフェッショナルな翻訳者です。
以下のルールに従って翻訳してください：

1. 原文の意味を正確に保ちながら、自然な表現で翻訳する
2. 文書の構造（見出し、リスト、表）を Markdown 形式で保持する
3. 専門用語は適切に翻訳し、必要に応じて原語を括弧内に残す
4. 文化的なニュアンスを考慮した翻訳を行う
5. 画像プレースホルダー（[IMAGE:xxx]）はそのまま保持し、翻訳しない
6. 元のドキュメントのレイアウト（段落構成、見出し階層）を可能な限り再現する";

    public const string UserPromptTemplate = @"以下のテキストを{targetLanguage}に翻訳してください。

注意事項:
- 画像プレースホルダー [IMAGE:xxx] は翻訳せず、そのままの位置に残してください
- 見出しの階層（#, ##, ### など）を維持してください
- 表形式は Markdown テーブル形式で保持してください";
}
```

#### 検証
- [x] ファイルが作成されている (2026/02/05)
- [x] すべてのハンドラーが実装されている (2026/02/05)
- [x] プロンプトパラメータを受け取れる (2026/02/05)
- [x] ビルドが成功する (2026/02/05)

---

### Step 22.2: Razor Page の実装 (1時間)

#### タスク
- [x] `Pages/Translator/GPT.cshtml` の作成 (2026/02/05)
- [x] システムプロンプト入力エリア（サンプル入力済み）(2026/02/05)
- [x] ユーザープロンプト入力エリア（サンプル入力済み）(2026/02/05)
- [x] ファイルアップロードエリア (2026/02/05)
- [x] 言語選択 (2026/02/05)
- [x] 出力形式選択（Markdown / HTML / PDF）(2026/02/05)
- [x] 結果表示エリア (2026/02/05)
- [x] ダウンロードボタン (2026/02/05)

#### ファイル: `Pages/Translator/GPT.cshtml`

> **参考**: `Pages/OCR/GPT.cshtml` のカスタムプロンプト入力エリアを参考に実装

UI コンポーネント:

1. **プロンプト設定エリア**（上部、折りたたみ可能）

   **システムプロンプト入力**
   - ラベル: 「システムプロンプト（翻訳者の役割定義）」
   - 入力形式: テキストエリア（rows="6"）
   - **デフォルト値（サンプル）**:
     ```
     あなたはプロフェッショナルな翻訳者です。
     以下のルールに従って翻訳してください：

     1. 原文の意味を正確に保ちながら、自然な表現で翻訳する
     2. 文書の構造（見出し、リスト、表）を Markdown 形式で保持する
     3. 専門用語は適切に翻訳し、必要に応じて原語を括弧内に残す
     4. 文化的なニュアンスを考慮した翻訳を行う
     5. 画像プレースホルダー（[IMAGE:xxx]）はそのまま保持し、翻訳しない
     6. 元のドキュメントのレイアウト（段落構成、見出し階層）を可能な限り再現する
     ```
   - ヘルプテキスト: 「翻訳者としての振る舞いを定義します。空欄の場合はデフォルトを使用します」

   **ユーザープロンプト入力**
   - ラベル: 「ユーザープロンプト（翻訳指示）」
   - 入力形式: テキストエリア（rows="5"）
   - **デフォルト値（サンプル）**:
     ```
     以下のテキストを{targetLanguage}に翻訳してください。

     注意事項:
     - 画像プレースホルダー [IMAGE:xxx] は翻訳せず、そのままの位置に残してください
     - 見出しの階層（#, ##, ### など）を維持してください
     - 表形式は Markdown テーブル形式で保持してください
     ```
   - ヘルプテキスト: 「{targetLanguage} は選択した翻訳先言語に自動置換されます」
   - 注意: プロンプト内の `{targetLanguage}` はプレースホルダーとして処理

2. **左側カラム: 入力エリア**
   - ファイルアップロード（ドラッグ&ドロップ）
     - 対応形式: PDF (.pdf), Word (.docx) のみ
     - ドロップエリアのスタイルは OCR/GPT.cshtml を参考
   - 翻訳元言語選択（自動検出がデフォルト）
   - 翻訳先言語選択
   - 出力形式選択（Markdown / HTML / PDF）
   - 翻訳実行ボタン

3. **右側カラム: 結果エリア**
   - ローディング表示（「GPT-4o で翻訳しています...」）
   - 翻訳完了メッセージ
   - **翻訳結果リンク**（クリックで Markdown プレビュー表示）
   - 翻訳情報（トークン数、処理時間、保存先）
   - **PDF 変換ボタン**（結果確認後に有効化）
   - Markdown ダウンロードボタン

4. **翻訳結果プレビューモーダル**
   - Markdown を HTML に変換して表示
   - marked.js を使用
   - コピーボタン
   - 閉じるボタン

#### プロンプトのカスタマイズについて

ユーザーがプロンプトを編集することで、以下のようなカスタマイズが可能:

| カスタマイズ例 | システムプロンプトへの追記 |
|---------------|------------------------|
| フォーマル調 | 「敬語を使用し、ビジネス文書として適切な表現を使う」 |
| カジュアル調 | 「親しみやすい口語表現を使用する」 |
| 法律文書 | 「法律用語は正確に訳し、原語を併記する」 |
| 医療文書 | 「医学用語は正確に訳し、一般的な表現も併記する」 |
| 技術文書 | 「技術用語は英語のまま残すか、カタカナ表記を使用する」 |

#### 検証
- [x] ファイルが作成されている (2026/02/05)
- [x] システムプロンプト入力エリアにデフォルト値が表示される (2026/02/05)
- [x] ユーザープロンプト入力エリアにデフォルト値が表示される (2026/02/05)
- [x] プロンプトの編集が可能 (2026/02/05)
- [x] UI が正しく表示される (2026/02/05)
- [x] 出力形式選択が機能する (2026/02/05)

---

### Step 22.3: JavaScript の実装 (1時間)

#### タスク
- [x] `wwwroot/js/gpt-translator.js` の作成 (2026/02/05)
- [x] プロンプト入力エリアの初期化 (2026/02/05)
- [x] ファイルアップロード処理 (2026/02/05)
- [x] 翻訳リクエスト処理（プロンプト含む）(2026/02/05)
- [x] 結果表示処理 (2026/02/05)
- [x] ダウンロード処理 (2026/02/05)

#### ファイル: `wwwroot/js/gpt-translator.js`

> **参考**: `wwwroot/js/gpt-vision.js` の実装パターンを参考

実装する機能:

1. **GptTranslatorApp クラス**
   - ファイル選択・ドラッグ&ドロップ（OCR/GPT.cshtml のパターンを参考）
   - 言語ドロップダウンの動的生成
   - 出力形式選択
   - **システムプロンプト・ユーザープロンプトの取得**

2. **プロンプト処理**
   - システムプロンプト入力エリアからテキスト取得
   - ユーザープロンプト入力エリアからテキスト取得
   - `{targetLanguage}` プレースホルダーを選択言語に置換
   - 空欄の場合はデフォルトプロンプトを使用

3. **翻訳処理**
   - フォーム送信（プロンプトを含む）
   - ローディング表示
   - 結果の取得と表示

4. **翻訳完了後の表示**
   - 翻訳情報（処理時間、トークン数など）を表示
   - **翻訳結果を見る** リンクを表示
   - **PDF 変換** ボタンを有効化
   - **Markdown ダウンロード** ボタンを表示

5. **翻訳結果プレビュー**
   - リンククリックで Blob Storage から Markdown を取得
   - marked.js で HTML に変換してモーダル表示
   - シンタックスハイライト対応（オプション）

5. **PDF 変換処理**
   - PDF 変換ボタンクリックでサーバーにリクエスト
   - Blob Storage の Markdown を PDF に変換
   - ダウンロードを開始

6. **Markdown ダウンロード**
   - Blob Storage から直接ダウンロード

#### 検証
- [x] ファイルが作成されている (2026/02/05)
- [x] JavaScript 構文が正しい (2026/02/05)
- [ ] 翻訳処理が動作する（テスト待ち）

---

### Step 22.4: ナビゲーションの更新 (30分)

#### タスク
- [x] `_Layout.cshtml` にナビゲーションリンクを追加（`/Translator/GPT`）(2026/02/05)
- [x] `Pages/Index.cshtml` にカードを追加 (2026/02/05)

#### ファイル: `Pages/Index.cshtml` の更新

新しいカード内容:
- **タイトル**: AI 翻訳 (GPT-4o)
- **アイコン**: robot または magic
- **特徴**: 
  - コンテキストを理解した高品質翻訳
  - Markdown / PDF 出力対応
  - 専門用語やトーンのカスタマイズ
- **ボタン**: 緑色系
- **リンク先**: `/Translator/GPT`

#### 新しい URL 構造
- `/Translator/AzureTranslator` - Azure Translator ドキュメント翻訳
- `/Translator/GPT` - GPT-4o 翻訳（新規）

#### 検証
- [x] ナビゲーションが更新されている (2026/02/05)
- [x] ホームページにカードが追加されている (2026/02/05)
- [x] リンクが正しく動作する (2026/02/05)

---

### Phase 22 完了チェックリスト

- [x] PageModel が実装されている（プロンプトパラメータ対応） ✅
- [x] Razor Page が作成されている ✅
- [x] システムプロンプト入力エリアが表示される（デフォルト値入力済み） ✅
- [x] ユーザープロンプト入力エリアが表示される（デフォルト値入力済み） ✅
- [x] プロンプトのカスタマイズが可能 ✅
- [x] JavaScript が実装されている（プロンプト処理対応） ✅
- [x] ナビゲーションが更新されている ✅
- [x] UI が正しく表示される ✅
- [ ] 翻訳が動作し、Blob Storage に保存される（App Service 展開後にテスト）
- [ ] 翻訳結果リンクで Markdown を確認できる（App Service 展開後にテスト）
- [ ] PDF 変換ボタンで PDF をダウンロードできる（App Service 展開後にテスト）

**完了日**: 2026年2月5日（UI 実装完了、翻訳テストは App Service 展開後）

---

## Phase 23: テストとデバッグ (推定: 2-3時間)

### ゴール
実装した GPT 翻訳機能をテストし、問題を修正

---

### Step 23.1: 統合テスト (1.5時間)

#### テストシナリオ

##### 1. 正常系テスト

**テストケース 1.1: PDF ドキュメントの翻訳（日→英）**
- [ ] 日本語 PDF ファイルをアップロード
- [ ] 翻訳先言語: 英語 (en)
- [ ] 翻訳が成功し、Blob Storage に Markdown が保存される
- [ ] 翻訳情報（トークン数、処理時間）が表示される

**テストケース 1.2: Word ドキュメントの翻訳（英→日）**
- [ ] 英語 .docx ファイルをアップロード
- [ ] 翻訳先言語: 日本語 (ja)
- [ ] 翻訳が成功し、Blob Storage に Markdown が保存される
- [ ] レイアウト（見出し、リスト）が保持されている

**テストケース 1.3: 画像を含むドキュメント**
- [ ] 画像を含む PDF/Word ファイルをアップロード
- [ ] 画像が Blob Storage に保存される
- [ ] Markdown 内で画像が正しく参照されている
- [ ] 画像がプレビューに表示される
- [ ] **画像内のテキストは翻訳されていないことを確認**

**テストケース 1.4: 翻訳結果の確認**
- [ ] 翻訳結果リンクをクリック
- [ ] Markdown プレビューがモーダルで表示される
- [ ] Markdown の内容が正しく翻訳されている
- [ ] 画像が正しく表示される

**テストケース 1.5: PDF 変換**
- [ ] PDF 変換ボタンをクリック
- [ ] PDF が正しく生成されている
- [ ] 画像が PDF 内に含まれている
- [ ] 日本語が正しく表示されている
- [ ] PDF がダウンロードされる

**テストケース 1.6: カスタムオプション**
- [ ] トーン: formal
- [ ] ドメイン: legal
- [ ] 翻訳結果がオプションを反映している

**テストケース 1.7: 長文ドキュメント**
- [ ] 10ページ以上の PDF をアップロード
- [ ] 分割処理が正しく動作する
- [ ] 翻訳結果が結合されている
- [ ] 画像の位置が正しく保持されている

##### 2. 異常系テスト

**テストケース 2.1: サポート外ファイル形式**
- [ ] Excel (.xlsx) をアップロード → エラーメッセージ表示
- [ ] PowerPoint (.pptx) をアップロード → エラーメッセージ表示
- [ ] テキストファイル (.txt) をアップロード → エラーメッセージ表示
- [ ] 画像ファイル (.png) をアップロード → エラーメッセージ表示

**テストケース 2.2: トークン超過**
- [ ] 非常に長いドキュメントをアップロード
- [ ] 適切なエラーメッセージが表示される

**テストケース 2.3: API エラー**
- [ ] レート制限エラーの処理
- [ ] リトライが動作する

#### 検証
- [ ] すべての正常系テストが成功
- [ ] 異常系のエラーハンドリングが機能している

---

### Step 23.2: パフォーマンステスト (1時間)

#### テスト項目

1. **処理時間の測定**
   - [ ] 1ページの PDF の処理時間
   - [ ] 10ページの PDF の処理時間
   - [ ] PDF 変換の処理時間

2. **トークン使用量の確認**
   - [ ] 小さいドキュメントのトークン数
   - [ ] 大きいドキュメントのトークン数
   - [ ] コスト見積もり

3. **品質評価**
   - [ ] 翻訳の自然さ
   - [ ] 構造の保持
   - [ ] 専門用語の処理

#### 検証
- [ ] パフォーマンス測定が完了
- [ ] 品質評価が完了

---

### Phase 23 完了チェックリスト

- [ ] すべての正常系テストが成功
- [ ] 異常系テストが成功
- [ ] パフォーマンステストが完了
- [ ] 品質評価が完了

---

## Phase 23.5: ヘルスチェックと Warmup の実装 (推定: 30分) ✅ 完了

### ゴール
GPT 翻訳機能で使用する Azure Blob Storage のヘルスチェック、および Warmup エンドポイントに GPT 翻訳サービスと PDF 変換サービスの初期化確認を追加

> **注意**: Azure OpenAI と Document Intelligence のヘルスチェックは既存の実装を共用します。

---

### Step 23.5.1: Azure Blob Storage ヘルスチェック ✅ 完了

#### タスク
- [x] `Services/HealthChecks/AzureBlobStorageHealthCheck.cs` の作成（Azure Translator と共用）
- [x] `translated` コンテナの存在確認を追加

#### 確認対象コンテナ
| コンテナ名 | 用途 | 使用機能 |
|-----------|------|----------|
| source | 翻訳元ドキュメント | Azure Translator |
| target | 翻訳済みドキュメント | Azure Translator |
| **translated** | GPT 翻訳結果（Markdown + 画像） | **GPT 翻訳** |

#### 検証
- [x] translated コンテナの確認が追加されている ✅

**実装完了日**: 2026年2月5日

---

### Step 23.5.2: Warmup エンドポイントの拡張 ✅ 完了

#### タスク
- [x] Azure Blob Storage の接続確認を追加
- [x] GPT 翻訳サービス（IGptTranslatorService）の初期化確認を追加
- [x] PDF 変換サービス（IPdfConverterService）の初期化確認を追加

#### Warmup で確認するサービス（GPT 翻訳関連）

| サービス | 確認内容 | 状態 |
|----------|----------|------|
| Document Intelligence | エンドポイント接続 | 既存（共用） |
| Azure OpenAI | エンドポイント接続 | 既存（共用） |
| **Azure Blob Storage** | アカウント接続 | ✅ 新規追加 |
| **GPT 翻訳サービス** | DI 初期化 | ✅ 新規追加 |
| **PDF 変換サービス** | DI 初期化 | ✅ 新規追加 |

#### コード変更

```csharp
// Azure Blob Storage の接続確認
var storageAccountName = config["AzureStorage:AccountName"];

if (!string.IsNullOrEmpty(storageAccountName))
{
    var blobServiceEndpoint = $"https://{storageAccountName}.blob.core.windows.net";
    var credential = new DefaultAzureCredential();
    var blobServiceClient = new BlobServiceClient(new Uri(blobServiceEndpoint), credential);
    
    // サービスプロパティを取得して接続確認
    await blobServiceClient.GetPropertiesAsync();
    warmupLogger.LogInformation("Azure Blob Storage 接続確認成功 (Account: {AccountName})", storageAccountName);
    
    // GPT 翻訳サービスの初期化確認
    var gptTranslatorService = sp.GetRequiredService<IGptTranslatorService>();
    warmupLogger.LogInformation("GPT 翻訳サービスの初期化成功");
    
    // PDF 変換サービスの初期化確認
    var pdfConverterService = sp.GetRequiredService<IPdfConverterService>();
    warmupLogger.LogInformation("PDF 変換サービスの初期化成功");
}
```

#### 検証
- [x] Warmup エンドポイントが更新されている ✅
- [x] GPT 翻訳サービスの初期化が確認される ✅
- [x] PDF 変換サービスの初期化が確認される ✅

**実装完了日**: 2026年2月5日

---

### Phase 23.5 完了チェックリスト

- [x] Azure Blob Storage ヘルスチェックが translated コンテナを確認する ✅
- [x] Warmup で GPT 翻訳サービスの初期化が確認される ✅
- [x] Warmup で PDF 変換サービスの初期化が確認される ✅
- [x] ビルドが成功する ✅

**実装完了日**: 2026年2月5日

---

## 📊 実装完了後の全体構成

```
src/WebApp/
├── Services/
│   ├── IOcrService.cs                    # Document Intelligence インターフェース
│   ├── DocumentIntelligenceService.cs    # Document Intelligence 実装
│   ├── IGptVisionService.cs              # GPT-4o Vision インターフェース
│   ├── OpenAIVisionService.cs            # GPT-4o Vision 実装
│   ├── ITranslatorService.cs             # Azure Translator インターフェース
│   ├── AzureTranslatorService.cs         # Azure Translator 実装
│   ├── IGptTranslatorService.cs          # GPT 翻訳インターフェース（新規）
│   ├── GptTranslatorService.cs           # GPT 翻訳実装（新規）
│   ├── IPdfConverterService.cs           # PDF 変換インターフェース（新規）
│   ├── PdfConverterService.cs            # PDF 変換実装（新規）
│   └── HealthChecks/
│       ├── DocumentIntelligenceHealthCheck.cs
│       ├── AzureOpenAIHealthCheck.cs
│       ├── AzureTranslatorHealthCheck.cs     # 翻訳機能用
│       └── AzureBlobStorageHealthCheck.cs    # 翻訳機能用
├── Models/
│   ├── OcrResult.cs
│   ├── VisionOcrResult.cs
│   ├── TranslationResult.cs
│   ├── GptTranslationResult.cs           # GPT 翻訳結果（新規）
│   ├── GptTranslationOptions.cs          # 翻訳オプション（新規）
│   ├── PdfOptions.cs                     # PDF オプション（新規）
│   └── ...
├── Pages/
│   ├── Index.cshtml
│   ├── OCR/
│   │   ├── DocumentIntelligence.cshtml
│   │   ├── DocumentIntelligence.cshtml.cs
│   │   ├── GPT.cshtml
│   │   └── GPT.cshtml.cs
│   ├── Translator/
│   │   ├── AzureTranslator.cshtml        # Azure Translator
│   │   ├── AzureTranslator.cshtml.cs
│   │   ├── GPT.cshtml                    # GPT 翻訳（新規）
│   │   └── GPT.cshtml.cs
│   └── Shared/
├── wwwroot/
│   ├── css/
│   │   ├── site.css
│   │   └── pdf-styles.css                # PDF スタイル（新規）
│   └── js/
│       ├── ocr-app.js
│       ├── gpt-vision.js
│       ├── translator.js
│       └── gpt-translator.js             # GPT 翻訳 JS（新規）
└── Program.cs
```

---

## 🔧 実装チェックリスト（全体）

### Phase 20: GPT 翻訳サービスの実装 ✅
- [x] IGptTranslatorService インターフェースの作成
- [x] GptTranslatorService の実装
- [x] モデルクラスの作成
- [x] Document Intelligence 連携

### Phase 21: PDF 変換機能の実装 ✅
- [x] PDF 変換ライブラリのインストール
- [x] IPdfConverterService インターフェースの作成
- [x] PdfConverterService の実装
- [x] PDF スタイルシートの作成
- [x] Program.cs への DI 登録

### Phase 22: UI の実装 ✅
- [x] PageModel の実装
- [x] Razor Page の作成
- [x] JavaScript の実装
- [x] ナビゲーションの更新

### Phase 23: テストとデバッグ ⭕ App Service 展開後
- [ ] 統合テストの実施
- [ ] パフォーマンステスト
- [ ] 品質評価

---

## 💡 実装のヒント

### 対応ファイル形式の制限

#### サポート対象
- **PDF**: `.pdf`
- **Word**: `.docx`

#### サポート対象外（エラーを返す）
- Excel (.xlsx)
- PowerPoint (.pptx)
- HTML (.html, .htm)
- テキスト (.txt, .csv)
- 画像ファイル (.png, .jpg, .gif)

### 画像処理の注意点

#### 1. 画像抽出
- **PdfPig** ライブラリで PDF から直接画像を抽出（ページ番号付き）
- Document Intelligence の `figures` プロパティから座標情報を取得
- ページ番号と位置情報で画像を識別

#### 2. 画像内テキスト
- **翻訳対象外**: 図表のラベル、グラフの凡例などは翻訳されない
- 重要なテキストが画像内にある場合は、別途手動で対応が必要
- ユーザーへの注意事項として UI で明示

#### 3. 図の OCR テキスト削除
- `figures` プロパティから各図の座標範囲を取得
- 図の座標範囲と重なる段落・テーブルのスパンも削除対象に含める
- 重なり合うスパンをマージし、各図に1つのプレースホルダーを割り当て
- GPT がプレースホルダー形式を変更しても置換可能

#### 4. 画像の保存
- Blob Storage の `images/` フォルダに保存
- ファイル名形式: `{document_id}_{timestamp}/page{N}_img{M}.png`
- **プロキシエンドポイント経由で配信**（SAS トークンは不使用）
- Storage Account のネットワーク制限があってもブラウザから表示可能

### GPT 翻訳の注意点

#### 1. トークン制限
- **GPT-4o**: 128K トークン（入力 + 出力）
- **推奨**: 1回のリクエストで 4000-8000 トークン程度に分割
- **長文処理**: 段落単位で分割し、順次翻訳
- **画像プレースホルダー**: 分割時も位置を保持

#### 2. プロンプト設計のコツ
- システムプロンプトで役割を明確に定義
- 出力形式（Markdown）を明示
- 画像プレースホルダーの取り扱いを明示
- 具体的な例を含めると精度向上

#### 3. コスト管理
- **GPT-4o**: $5/1M input tokens, $15/1M output tokens
- **推定コスト**: $0.01-0.10/ページ（内容による）
- **最適化**: 不要なコンテキストを削減

### PDF 変換の注意点

> **ライブラリ変更**: PuppeteerSharp から PdfSharpCore + MigraDocCore に変更しました (2026/02/05)

#### 1. 日本語フォント
- カスタム `JapaneseFontResolver` クラスでフォントを解決
- 游ゴシック（Yu Gothic）をデフォルトフォントとして使用
- Azure App Service (Windows) では `C:\Windows\Fonts\` のフォントを使用
- フォールバック: メイリオ → MS Gothic → Arial

#### 2. ページ設定
- A4 サイズがデフォルト（A3, Letter, Legal も対応）
- マージンは mm, cm, pt で指定可能（デフォルト: 20mm）
- 縦向き・横向きの切り替え対応

#### 3. 対応 Markdown 要素
- 見出し（# 〜 ####）
- 段落、太字、斜体、コード
- リスト（箇条書き、番号付き、ネスト対応）
- テーブル（ヘッダー行のスタイル付き）
- コードブロック（Consolas フォント）
- 引用ブロック
- 水平線
- リンク（ハイパーリンク）

#### 4. パフォーマンス
- 純粋な .NET ライブラリなので高速
- 外部プロセスの起動不要
- Azure App Service で問題なく動作

---

## 📈 推定作業時間まとめ

| Phase | 内容 | 時間 | 状態 |
|-------|------|------|------|
| Phase 20 | GPT 翻訳サービスの実装 | 2-3時間 | ✅ 完了 |
| Phase 21 | PDF 変換機能の実装 | 2-3時間 | ✅ 完了 |
| Phase 22 | UI の実装 | 2-3時間 | ✅ 完了 |
| Phase 23 | テストとデバッグ | 2-3時間 | ⭕ App Service 展開後 |
| **合計** | | **8-12時間** | |

---

## 🎯 Azure Translator との使い分け

| ユースケース | 推奨 |
|-------------|------|
| PDF/Word の書式を完全に保持したい | Azure Translator |
| 画像内テキストも翻訳したい | Azure Translator |
| 高品質な翻訳が必要 | GPT-4o 翻訳 |
| 専門用語のカスタマイズ | GPT-4o 翻訳 |
| レイアウトを Markdown で再現したい | GPT-4o 翻訳 |
| 大量のドキュメント処理 | Azure Translator |
| コスト重視 | Azure Translator |
| ネットワーク制限環境 | GPT-4o 翻訳（シンプル） |
| Excel/PowerPoint の翻訳 | Azure Translator（GPT 翻訳は非対応） |

### 画像の取り扱い比較

| 項目 | Azure Translator | GPT-4o 翻訳 |
|------|-----------------|-------------|
| 画像内テキスト | ✅ 翻訳される | ❌ 翻訳対象外 |
| 画像の保持 | ✅ 元のまま保持 | ✅ Markdown で参照 |
| 図表のレイアウト | ✅ 完全保持 | △ 位置は保持（Markdown） |

---

## 🔮 将来の拡張

### 短期的な拡張（1-2週間）
1. **翻訳メモリ**: 過去の翻訳を参照して一貫性向上
2. **用語集**: カスタム用語辞書のサポート
3. **バッチ処理**: 複数ファイルの一括翻訳

### 中期的な拡張（1-2ヶ月）
1. **リアルタイムプレビュー**: 翻訳中のストリーミング表示
2. **比較表示**: 原文と翻訳文の並列表示
3. **編集機能**: 翻訳結果の手動修正

### 長期的な拡張（3-6ヶ月）
1. **Fine-tuning**: ドメイン特化モデルの作成
2. **品質評価**: BLEU スコアなどの自動評価
3. **ワークフロー統合**: レビュー・承認プロセス

---

**最終更新日**: 2026年2月5日  
**推定合計時間**: 8-12 時間  
**難易度**: 中級～上級

---

## 📝 変更履歴

### 2026年2月6日
- **複数画像の位置保持機能の実装**
  - PdfPig ライブラリで PDF から直接画像を抽出（ページ番号付き）
  - Document Intelligence の `figures` プロパティから座標情報を取得
  - 図の座標範囲と重なる段落・テーブルのスパンを削除対象に含める
  - 重なり合うスパンをマージし、各図に1つのプレースホルダーを割り当て
  - GPT がプレースホルダー形式を変更しても置換可能（`[[IMG_PLACEHOLDER_001]]`, `[IMAGE:001]` など）
  - 翻訳後に画像が正しい位置に表示されることを確認

- **画像プロキシエンドポイントの実装**
  - Storage Account のネットワーク制限下でもブラウザから画像を表示可能に
  - SAS URL から App Service 経由のプロキシ URL に変更
  - セキュリティチェック（`images/` フォルダ内のみ許可、パストラバーサル防止）
  - App Service 上での動作確認完了

### 2026年2月5日
- **TTC フォント処理の改善**
  - PdfSharpCore が TTC (TrueType Collection) ファイルを直接読み込めない問題を修正
  - `ExtractTtfFromTtc()` メソッドを追加し、TTC から個別の TTF を抽出
  - フォントキャッシュを実装してパフォーマンスを向上

- **多言語対応フォントリゾルバーへの拡張**
  - Noto Sans CJK、Segoe UI Symbol、Arial Unicode MS へのフォールバックを追加
  - Windows App Service でサポートされている主要言語に対応

- **対応言語の制限**
  - Windows フォントで正しく表示できない言語を翻訳先リストから削除
  - 削除した言語: アラビア語 (ar)、ヘブライ語 (he)、ヒンディー語 (hi)、ベンガル語 (bn)、タイ語 (th)、ペルシア語 (fa)、ウルドゥー語 (ur)
  - 理由: RTL（右から左）表記や特殊文字が Windows 標準フォントで正しく表示できない
  - 残り 48 言語をサポート（日本語、英語、中国語、韓国語、欧米言語など）
