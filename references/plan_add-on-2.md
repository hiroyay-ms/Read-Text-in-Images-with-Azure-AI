# OpenTelemetry & ヘルスチェック機能 - 段階的実装計画

**作成日**: 2026年1月2日  
**更新日**: 2026年1月3日  
**対象**: OpenTelemetry による Application Insights 統合とヘルスチェックエンドポイントの追加  
**実装環境**: ローカル開発環境  
**状態**: Phase 3 完了

---

## 📋 背景と目的

Azure への展開を見据えて、アプリケーションの可観測性とヘルスチェック機能を強化します。

### 目的
- **可観測性の向上**: OpenTelemetry による分散トレーシング、メトリクス、ログの統合
- **監視の強化**: Application Insights への自動テレメトリ送信
- **ヘルスチェック**: Azure Container Apps/AKS での自動ヘルスプローブ対応
- **外部依存関係の監視**: Document Intelligence と Azure OpenAI の接続確認

### 実装アプローチ
既存の Document Intelligence と GPT-4o 機能に、OpenTelemetry とヘルスチェック機能を追加します。

---

## Phase 1: NuGet パッケージのインストールと基本設定 (推定: 30分)

### ゴール
必要な NuGet パッケージをインストールし、appsettings.Development.json に基本設定を追加

---

### Step 1.1: NuGet パッケージのインストール (15分) ✅

#### タスク
- [x] `src/WebApp/` ディレクトリに移動
- [x] 以下のパッケージをインストール:
  - OpenTelemetry.Exporter.Console (1.9.0)
  - OpenTelemetry.Extensions.Hosting (1.9.0)
  - OpenTelemetry.Instrumentation.AspNetCore (1.9.0)
  - OpenTelemetry.Instrumentation.Http (1.9.0)
  - Azure.Monitor.OpenTelemetry.AspNetCore (1.2.0)
  - ~~Microsoft.Extensions.Diagnostics.HealthChecks (10.0.0)~~ (ASP.NET Core に含まれるため不要)
  - AspNetCore.HealthChecks.UI.Client (8.0.1)

#### コマンド例
```bash
cd src/WebApp
dotnet add package OpenTelemetry.Exporter.Console --version 1.9.0
# (その他のパッケージも同様に追加)
```

#### 検証
- [x] WebApp.csproj にパッケージ参照が追加されている
- [x] `dotnet restore` が正常に完了する
- [x] ビルドエラーがない

---

### Step 1.2: appsettings.Development.json の更新 (15分) ✅

#### タスク
- [x] appsettings.Development.json を開く
- [x] Logging セクションに Azure.Core と OpenTelemetry のログレベルを追加
- [x] ApplicationInsights セクションを追加（ConnectionString は空）
- [x] HealthChecks セクションを追加

#### 追加する設定内容
- `Logging:LogLevel` に以下を追加:
  - `Azure.Core`: Warning
  - `OpenTelemetry`: Information
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: ""（空文字列、またはローカルの Application Insights 接続文字列）
- `HealthChecks` セクション:
  - `TimeoutSeconds`: 10
  - `EnableDetailedErrors`: true

#### 検証
- [x] appsettings.Development.json が正しく更新されている
- [x] JSON 構文エラーがない
- [x] アプリケーションが起動できる

---

## Phase 2: ヘルスチェック機能の実装 (推定: 2-3時間)

### ゴール
Document Intelligence と Azure OpenAI の接続を確認するヘルスチェックエンドポイントを実装

---

### Step 2.1: ヘルスチェックディレクトリの作成 (5分) ✅

#### タスク
- [x] `src/WebApp/Services/HealthChecks/` ディレクトリを作成

#### 検証
- [x] ディレクトリが作成されている

---

### Step 2.2: DocumentIntelligenceHealthCheck の実装 (45分) ✅

#### タスク
- [x] `Services/HealthChecks/DocumentIntelligenceHealthCheck.cs` ファイルを作成
- [x] IHealthCheck インターフェースを実装
- [x] IConfiguration を DI で受け取るコンストラクタを実装
- [x] CheckHealthAsync メソッドを実装:
  - **HTTP HEAD リクエスト**でエンドポイントへの接続を確認（軽量化のため）
  - タイムアウト: 2秒
  - 成功時: HealthCheckResult.Healthy を返す
  - タイムアウト/接続エラー時: HealthCheckResult.Unhealthy を返す
- [x] 適切なログ出力を追加

#### 実装の特徴
- 認証不要の HTTP HEAD リクエストで高速チェック（sub-second）
- 当初計画の AnalyzeDocumentAsync ではなく、軽量なエンドポイント確認を採用
- Kubernetes/Container Apps の高頻度プローブに対応

#### 検証
- [x] ファイルが作成されている
- [x] 名前空間が `WebApp.Services.HealthChecks`
- [x] IHealthCheck インターフェースを実装している
- [x] ビルドエラーがない
- [x] パフォーマンスが良好（1秒未満）

---

### Step 2.3: AzureOpenAIHealthCheck の実装 (45分) ✅

#### タスク
- [x] `Services/HealthChecks/AzureOpenAIHealthCheck.cs` ファイルを作成
- [x] IHealthCheck インターフェースを実装
- [x] IConfiguration を DI で受け取るコンストラクタを実装
- [x] CheckHealthAsync メソッドを実装:
  - 設定からエンドポイントを取得
  - **HTTP HEAD リクエスト**でエンドポイントへの接続を確認（軽量化のため）
  - タイムアウト: 2秒
  - 成功時: HealthCheckResult.Healthy を返す
  - タイムアウト/接続エラー時: HealthCheckResult.Unhealthy を返す
- [x] 適切なログ出力を追加

#### 実装の特徴
- DefaultAzureCredential 認証を使用しない（認証に30秒以上かかる問題を回避）
- CompleteChatAsync ではなく HTTP HEAD リクエストで高速チェック
- パフォーマンス改善: 36秒 → 1秒未満

#### 検証
- [x] ファイルが作成されている
- [x] ビルドエラーがない
- [x] パフォーマンスが良好（1秒未満）

---

### Step 2.4: Program.cs へのヘルスチェック登録 (30分) ✅

#### タスク
- [x] Program.cs を開く
- [x] 必要な using ディレクティブを追加:
  - Microsoft.Extensions.Diagnostics.HealthChecks
  - Microsoft.AspNetCore.Diagnostics.HealthChecks
  - WebApp.Services.HealthChecks
- [x] サービス登録セクションにヘルスチェックを追加:
  - AddHealthChecks() を呼び出す
  - DocumentIntelligenceHealthCheck を登録（tags: "ready", "external"）
  - AzureOpenAIHealthCheck を登録（tags: "ready", "external"）
- [x] ミドルウェアセクションにヘルスチェックエンドポイントをマッピング:
  - `/health` - 詳細な JSON レスポンス（すべてのチェック）
  - `/health/ready` - Readiness プローブ（"ready" タグのみ、簡易レスポンス）
  - `/health/live` - Liveness プローブ（外部依存なし、常に Healthy）
- [x] カスタム ResponseWriter を実装してメトリクスを記録

#### 検証
- [x] コードが正しく追加されている
- [x] ビルドエラーがない
- [x] アプリケーションが起動できる
- [x] 3つのエンドポイントすべてが機能する

---

### Step 2.5: ヘルスチェックの動作確認 (30分) ✅

#### タスク
- [x] アプリケーションを起動
- [x] ブラウザまたは curl で以下のエンドポイントにアクセス:
  - https://localhost:5001/health
  - https://localhost:5001/health/ready
  - https://localhost:5001/health/live
- [x] レスポンスの内容を確認
- [x] エラー時の動作確認（無効なホスト名設定でテスト）

#### 検証
- [x] `/health` エンドポイントが応答する
- [x] `/health/ready` エンドポイントが応答する
- [x] `/health/live` エンドポイントが応答する
- [x] JSON 形式のレスポンスが返される
- [x] 各チェックの status が "Healthy" または "Unhealthy"
- [x] Document Intelligence のチェック結果が含まれている
- [x] Azure OpenAI のチェック結果が含まれている
- [x] レスポンス時間が1秒未満で良好
- [x] エラー時のログが適切に出力される

---

## Phase 3: OpenTelemetry の基本統合 (推定: 1-1.5時間)

### ゴール
OpenTelemetry の基本的なトレーシングとメトリクスを設定し、Console Exporter で確認できるようにする

---

### Step 3.1: Program.cs への OpenTelemetry 設定追加 (45分) ✅

#### タスク
- [x] Program.cs を開く
- [x] 必要な using ディレクティブを追加:
  - Azure.Monitor.OpenTelemetry.AspNetCore
  - OpenTelemetry.Metrics
  - OpenTelemetry.Resources
  - OpenTelemetry.Trace
  - System.Diagnostics.Metrics
- [x] builder 作成直後に OpenTelemetry の設定を追加:
  - APPLICATIONINSIGHTS_CONNECTION_STRING 環境変数から接続文字列を取得（App Service では自動設定）
  - AddOpenTelemetry() を呼び出す
  - ConfigureResource でサービス名とバージョンを設定
  - WithTracing でトレーシングを設定:
    - AddAspNetCoreInstrumentation（ヘルスチェックは除外）
    - AddHttpClientInstrumentation
  - WithMetrics でメトリクスを設定:
    - AddAspNetCoreInstrumentation
    - AddHttpClientInstrumentation
    - カスタムメーター追加（"WebApp.HealthChecks", "WebApp.OCR", "WebApp.GPTVision"）
  - ConnectionString が設定されている場合は UseAzureMonitor() を呼び出す
- [x] ヘルスチェックメトリクスの設定:
  - Meter "WebApp.HealthChecks" を作成
  - Counter "health_check.executions" を作成（tags: check_name, status）
  - Histogram "health_check.duration" を作成（tags: check_name, status）
  - RecordHealthCheckMetrics() ヘルパーメソッドを実装
  - カスタム ResponseWriter からメトリクスを記録

#### 実装の特徴
- **ConsoleExporter は削除**（定期的なログ出力を停止）
- **エンドポイント呼び出し時のみ**テレメトリを記録（IHealthCheckPublisher パターンは不使用）
- Application Insights へのテレメトリ送信のみ有効化

#### 検証
- [x] コードが正しく追加されている
- [x] ビルドエラーがない
- [x] アプリケーションが起動できる
- [x] 定期的なコンソール出力がない

---

### Step 3.2: OpenTelemetry の動作確認 (30分) ✅

#### タスク
- [x] アプリケーションを起動
- [x] いくつかのページにアクセス:
  - https://localhost:5001/
  - https://localhost:5001/OCR
  - https://localhost:5001/GPT
- [x] ヘルスチェックエンドポイントにアクセス
- [x] ログ出力を確認

#### 検証
- [x] コンソールに定期的なログが出力されない（ConsoleExporter 削除済み）
- [x] ヘルスチェックエンドポイント呼び出し時にのみログが出力される
- [x] ヘルスチェックメトリクスが記録される:
  - health_check.executions（check_name, status タグ付き）
  - health_check.duration（ミリ秒単位）
- [x] エラー時のログが適切に出力される（無効なホスト名テストで確認）
- [x] Application Insights へのテレメトリ送信が有効（ConnectionString 設定時）
- [x] エラーログが適切に出力される

---

## Phase 4: カスタムメトリクスとトレーシングの追加 (推定: 2-3時間)

### ゴール
各サービスクラスにカスタムメトリクスと詳細なトレーシングを追加し、ビジネスロジックの可観測性を向上

---

### Step 4.1: DocumentIntelligenceService へのテレメトリ追加 (1時間)

#### タスク
- [ ] DocumentIntelligenceService.cs を開く
- [ ] 必要な using ディレクティブを追加:
  - System.Diagnostics
  - System.Diagnostics.Metrics
- [ ] クラスの先頭に static フィールドを追加:
  - Meter: "WebApp.OCR"
  - Counter: ocr.requests（リクエスト数）
  - Counter: ocr.errors（エラー数）
  - Histogram: ocr.duration（処理時間）
  - Counter: ocr.text_lines（抽出テキスト行数）
  - ActivitySource: "WebApp.OCR"
- [ ] AnalyzeImageAsync メソッドを更新:
  - メソッド開始時に Activity を開始
  - Stopwatch で処理時間を計測
  - リクエストカウンターをインクリメント
  - Activity にファイル名とサイズをタグとして追加
  - 成功時: 抽出行数を記録、処理時間を記録
  - 失敗時: エラーカウンターをインクリメント、例外を記録
  - 適切なログ出力を追加

#### 検証
- [ ] using ディレクティブが追加されている
- [ ] Meter と ActivitySource が定義されている
- [ ] メトリクスが記録されている
- [ ] Activity にタグが追加されている
- [ ] ビルドエラーがない

---

### Step 4.2: OpenAIVisionService へのテレメトリ追加 (1時間)

#### タスク
- [ ] OpenAIVisionService.cs を開く
- [ ] 必要な using ディレクティブを追加:
  - System.Diagnostics
  - System.Diagnostics.Metrics
- [ ] クラスの先頭に static フィールドを追加:
  - Meter: "WebApp.GPTVision"
  - Counter: gpt_vision.requests（リクエスト数）
  - Counter: gpt_vision.errors（エラー数）
  - Histogram: gpt_vision.duration（処理時間）
  - Counter: gpt_vision.tokens（使用トークン数）
  - ActivitySource: "WebApp.GPTVision"
- [ ] AnalyzeImageAsync メソッドを更新:
  - メソッド開始時に Activity を開始
  - Stopwatch で処理時間を計測
  - リクエストカウンターをインクリメント
  - Activity にファイル名、サイズ、プロンプトをタグとして追加
  - 成功時: トークン使用量を記録、処理時間を記録
  - Activity にトークン情報をタグとして追加
  - 失敗時: エラーカウンターをインクリメント、例外を記録
  - 適切なログ出力を追加

#### 検証
- [ ] メトリクスが記録されている
- [ ] トークン使用量が追跡されている
- [ ] Activity に詳細情報が含まれている
- [ ] ビルドエラーがない

---

### Step 4.3: テレメトリの動作確認 (30-45分)

#### タスク
- [ ] アプリケーションを起動（Development 環境）
- [ ] OCR 機能をテスト:
  - OCR ページで画像をアップロード
  - コンソール出力を確認
- [ ] GPT Vision 機能をテスト:
  - GPT ページで画像をアップロード
  - コンソール出力を確認

#### 検証
- [ ] カスタムメトリクスがコンソールに出力される
- [ ] カスタム Activity が記録される
- [ ] Activity に追加したタグが含まれている:
  - file.name
  - file.size
  - text_lines.count（OCR）
  - user_prompt（GPT Vision）
  - tokens.total（GPT Vision）
  - tokens.prompt（GPT Vision）
  - tokens.completion（GPT Vision）
- [ ] エラー時に例外が記録される
- [ ] メトリクスの値が正しい（リクエスト数、処理時間など）

---

## Phase 5: テストと検証 (推定: 1-1.5時間)

### ゴール
実装したすべての機能が正しく動作することを確認

---

### Step 5.1: 正常系テスト (45分)

#### テストシナリオ 1: ヘルスチェック

**タスク**:
- [ ] 全体のヘルスチェックを実行（/health）
- [ ] Readiness プローブを実行（/health/ready）
- [ ] Liveness プローブを実行（/health/live）
- [ ] レスポンスの内容を確認

**検証項目**:
- [ ] すべてのエンドポイントが応答する
- [ ] status が "Healthy"
- [ ] Document Intelligence チェックが成功している
- [ ] Azure OpenAI チェックが成功している
- [ ] duration が記録されている
- [ ] totalDuration が記録されている

#### テストシナリオ 2: OCR 機能のテレメトリ

**タスク**:
- [ ] OCR ページにアクセス
- [ ] テスト画像をアップロード
- [ ] コンソールログを確認

**検証項目**:
- [ ] OCR が正常に動作する
- [ ] カスタムメトリクスが記録される:
  - ocr.requests
  - ocr.text_lines
  - ocr.duration
- [ ] トレース情報が出力される:
  - Activity.DisplayName: "AnalyzeImage"
  - Activity.Tags: file.name, file.size, text_lines.count
- [ ] 処理時間が記録される
- [ ] 抽出行数が記録される

#### テストシナリオ 3: GPT Vision 機能のテレメトリ

**タスク**:
- [ ] GPT ページにアクセス
- [ ] テスト画像とプロンプトを入力
- [ ] コンソールログを確認

**検証項目**:
- [ ] GPT Vision が正常に動作する
- [ ] トークン使用量が記録される:
  - gpt_vision.tokens
- [ ] カスタムメトリクスが出力される:
  - gpt_vision.requests
  - gpt_vision.duration
- [ ] トレース情報が出力される:
  - Activity.DisplayName: "AnalyzeImageWithGPT"
  - Activity.Tags: file.name, file.size, user_prompt, tokens.total, tokens.prompt, tokens.completion

---

### Step 5.2: エラー系テスト (30分)

#### テストシナリオ 1: 無効な画像のアップロード

**タスク**:
- [ ] サポートされていない形式のファイルをアップロード（例: .txt）
- [ ] 大きすぎるファイルをアップロード（10MB超）
- [ ] コンソールログを確認

**検証項目**:
- [ ] エラーが適切に処理される
- [ ] エラーメッセージが表示される
- [ ] エラーカウンターが増加する（ocr.errors または gpt_vision.errors）
- [ ] 例外がトレースに記録される（Activity.Status が Error）
- [ ] エラーログが出力される

#### テストシナリオ 2: サービス接続エラーのシミュレーション

**タスク**:
- [ ] appsettings.Development.json のエンドポイント設定を一時的に無効化
  - DocumentIntelligence_Endpoint を無効な値に変更
- [ ] ヘルスチェックを実行（/health）
- [ ] 元の設定に戻す

**検証項目**:
- [ ] ヘルスチェックが "Unhealthy" または "Degraded" を返す
- [ ] エラー詳細が exception フィールドに含まれている
- [ ] 適切なエラーログが出力される

---

### Step 5.3: 総合確認 (15分)

#### タスク
- [ ] すべての機能が正常に動作することを確認
- [ ] コンソール出力を確認し、期待される情報が記録されていることを確認
- [ ] ビルド警告がないことを確認

#### 検証
- [ ] アプリケーションが起動する
- [ ] すべてのページが正常に表示される
- [ ] ヘルスチェックエンドポイントが応答する
- [ ] OCR 機能が動作する
- [ ] GPT Vision 機能が動作する
- [ ] メトリクスが記録される
- [ ] トレースが記録される
- [ ] エラー処理が適切に動作する

---

## 📊 実装完了後の確認事項

### 機能チェックリスト
- [ ] ヘルスチェックエンドポイントが動作する
  - [ ] `/health` - 全体の正常性（JSON レスポンス）
  - [ ] `/health/ready` - Readiness プローブ
  - [ ] `/health/live` - Liveness プローブ
- [ ] Document Intelligence のヘルスチェックが動作する
- [ ] Azure OpenAI のヘルスチェックが動作する
- [ ] OpenTelemetry が設定されている
  - [ ] トレーシングが有効
  - [ ] メトリクスが有効
  - [ ] Console Exporter が動作
- [ ] カスタムメトリクスが記録される
  - [ ] OCR リクエスト数（ocr.requests）
  - [ ] OCR エラー数（ocr.errors）
  - [ ] OCR 処理時間（ocr.duration）
  - [ ] 抽出テキスト行数（ocr.text_lines）
  - [ ] GPT Vision リクエスト数（gpt_vision.requests）
  - [ ] GPT Vision エラー数（gpt_vision.errors）
  - [ ] GPT Vision 処理時間（gpt_vision.duration）
  - [ ] トークン使用量（gpt_vision.tokens）
- [ ] 分散トレーシングが動作する
  - [ ] HTTP リクエストのトレース
  - [ ] カスタム Activity のトレース
  - [ ] Activity タグの記録
- [ ] エラー時に例外が記録される
  - [ ] Activity.Status が Error
  - [ ] 例外の詳細が記録される

### コード品質チェックリスト
- [ ] ビルドエラーがない
- [ ] ビルド警告がない（または最小限）
- [ ] 適切なログレベルが使用されている
  - [ ] Information: 正常な操作
  - [ ] Warning: 軽微な問題
  - [ ] Error: エラー
- [ ] 例外処理が適切に実装されている
- [ ] リソースが適切に解放されている（using ステートメント）
- [ ] 名前空間が統一されている

### ドキュメントチェックリスト
- [ ] README.md に新機能の説明を追加（必要に応じて）
- [ ] ヘルスチェックエンドポイントの使用方法を記載
- [ ] Application Insights への接続方法を記載（将来の Azure デプロイ用）

---

## 🚀 次のステップ（将来の Azure デプロイ時）

### Azure デプロイ時の追加作業

#### 1. Application Insights リソースの作成
- [ ] Azure Portal で Application Insights リソースを作成
- [ ] 接続文字列をコピー

#### 2. アプリケーション設定の更新
- [ ] Azure App Service で Application Insights を有効化すると、`APPLICATIONINSIGHTS_CONNECTION_STRING` 環境変数が自動的に設定されます
- [ ] Container Apps の場合は、環境変数 `APPLICATIONINSIGHTS_CONNECTION_STRING` に Application Insights の接続文字列を手動で設定

#### 3. ヘルスプローブの設定
- [ ] Azure Container Apps の場合:
  - Liveness Probe: `/health/live`
  - Readiness Probe: `/health/ready`
- [ ] Azure App Service の場合:
  - ヘルスチェックパス: `/health/ready`

#### 4. Application Insights での確認
- [ ] Azure Portal で Application Insights を開く
- [ ] 以下を確認:
  - トレース（Application Insights > トランザクションの検索）
  - メトリクス（Application Insights > メトリクス）
  - ログ（Application Insights > ログ）
  - アプリケーションマップ（Application Insights > アプリケーションマップ）

---

## 📝 トラブルシューティング

### よくある問題と解決策

#### 問題 1: ヘルスチェックが失敗する
**原因**: Azure サービスへの接続設定が不正  
**解決策**:
- appsettings.Development.json のエンドポイント設定を確認
- Azure リソースが存在し、アクセス可能であることを確認
- DefaultAzureCredential で認証できることを確認

#### 問題 2: メトリクスがコンソールに出力されない
**原因**: Console Exporter が設定されていない  
**解決策**:
- Program.cs で `AddConsoleExporter()` が呼ばれているか確認
- OpenTelemetry のログレベルを Information に設定

#### 問題 3: トレースに詳細情報が含まれない
**原因**: Activity が開始されていない  
**解決策**:
- サービスクラスで `s_activitySource.StartActivity()` が呼ばれているか確認
- using ステートメントで Activity を適切に破棄しているか確認

#### 問題 4: ビルドエラー "Meter が見つからない"
**原因**: using ディレクティブが不足  
**解決策**:
- `using System.Diagnostics.Metrics;` を追加
- NuGet パッケージが正しくインストールされているか確認

#### 問題 5: ヘルスチェックのレスポンスが空
**原因**: エンドポイントマッピングが正しくない  
**解決策**:
- Program.cs でヘルスチェックエンドポイントが正しくマッピングされているか確認
- ResponseWriter が正しく設定されているか確認

---

## 📚 参考リソース

### 公式ドキュメント
- [OpenTelemetry for .NET](https://opentelemetry.io/docs/languages/net/)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
- [System.Diagnostics.Metrics](https://learn.microsoft.com/dotnet/core/diagnostics/metrics)
- [System.Diagnostics.Activity](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing)

### サンプルコード
- [OpenTelemetry .NET Samples](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/examples)
- [Azure Monitor OpenTelemetry Samples](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore)

---

## 📈 実装の利点

### OpenTelemetry + Application Insights
- ✅ エンドツーエンドの分散トレーシング
- ✅ カスタムメトリクスによるビジネス指標の可視化
- ✅ 構造化ログによる詳細なデバッグ情報
- ✅ Azure Monitor との統合で一元管理
- ✅ パフォーマンスボトルネックの特定
- ✅ 本番環境での問題の迅速な診断

### ヘルスチェック
- ✅ Kubernetes/Container Apps の Readiness/Liveness プローブ対応
- ✅ 外部依存関係の自動監視
- ✅ 障害の早期検出
- ✅ デプロイ前の接続確認
- ✅ JSON 形式の詳細な診断情報
- ✅ 運用チームによる監視の簡素化

---

この計画に従って段階的に実装を進めることで、各フェーズで動作確認を行いながら安全に機能を追加できます。

**実装開始**: Phase 1 から順に進めてください。
