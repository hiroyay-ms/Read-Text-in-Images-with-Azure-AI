// GPT-4o ドキュメント翻訳アプリケーション

class GptTranslatorApp {
    constructor() {
        // DOM 要素の取得
        this.documentFileInput = document.getElementById('documentFile');
        this.fileSelectLink = document.getElementById('fileSelectLink');
        this.translateBtn = document.getElementById('translateBtn');
        this.sourceLanguageSelect = document.getElementById('sourceLanguage');
        this.targetLanguageSelect = document.getElementById('targetLanguage');
        this.toneSelect = document.getElementById('tone');
        this.domainSelect = document.getElementById('domain');
        this.errorArea = document.getElementById('errorArea');
        this.errorMessage = document.getElementById('errorMessage');
        this.dropArea = document.getElementById('dropArea');
        
        // プロンプト関連
        this.systemPromptInput = document.getElementById('systemPrompt');
        this.userPromptInput = document.getElementById('userPrompt');
        this.resetPromptsBtn = document.getElementById('resetPromptsBtn');
        this.defaultSystemPrompt = document.getElementById('defaultSystemPrompt').value;
        this.defaultUserPrompt = document.getElementById('defaultUserPrompt').value;
        
        // ドキュメント情報エリア
        this.documentInfoContainer = document.getElementById('documentInfoContainer');
        this.fileIcon = document.getElementById('fileIcon');
        this.fileName = document.getElementById('fileName');
        this.fileSize = document.getElementById('fileSize');
        this.fileType = document.getElementById('fileType');
        this.removeFileBtn = document.getElementById('removeFileBtn');
        
        // 結果表示エリア
        this.resultContainer = document.getElementById('resultContainer');
        this.noResultMessage = document.getElementById('noResultMessage');
        this.loadingInResult = document.getElementById('loadingInResult');
        this.successResult = document.getElementById('successResult');
        this.resultSourceLang = document.getElementById('resultSourceLang');
        this.resultTargetLang = document.getElementById('resultTargetLang');
        this.resultCharCount = document.getElementById('resultCharCount');
        this.resultTokens = document.getElementById('resultTokens');
        this.resultDuration = document.getElementById('resultDuration');
        
        // アクションボタン
        this.viewResultBtn = document.getElementById('viewResultBtn');
        this.downloadMdBtn = document.getElementById('downloadMdBtn');
        this.downloadPdfBtn = document.getElementById('downloadPdfBtn');
        this.copyMarkdownBtn = document.getElementById('copyMarkdownBtn');
        
        // プレビューモーダル
        this.previewModal = new bootstrap.Modal(document.getElementById('previewModal'));
        this.previewLoading = document.getElementById('previewLoading');
        this.previewContent = document.getElementById('previewContent');
        
        // 隠しフィールド
        this.currentBlobNameInput = document.getElementById('currentBlobName');
        
        // 状態
        this.selectedFile = null;
        this.currentBlobName = null;
        this.currentMarkdown = null;
        
        // 初期化
        this.initialize();
    }

    async initialize() {
        // 言語一覧を取得
        await this.loadLanguages();
        
        // イベントリスナーの設定
        this.setupEventListeners();
        
        // ドラッグ&ドロップの初期化
        this.initializeDragAndDrop();
    }

    async loadLanguages() {
        try {
            const response = await fetch('?handler=Languages');
            if (!response.ok) {
                throw new Error('言語一覧の取得に失敗しました');
            }
            
            const languages = await response.json();
            
            // ソース言語と翻訳先言語のドロップダウンを構築
            for (const [code, name] of Object.entries(languages)) {
                // ソース言語（自動検出を除く）
                const sourceOption = document.createElement('option');
                sourceOption.value = code;
                sourceOption.textContent = `${name} (${code})`;
                this.sourceLanguageSelect.appendChild(sourceOption);
                
                // 翻訳先言語
                const targetOption = document.createElement('option');
                targetOption.value = code;
                targetOption.textContent = `${name} (${code})`;
                this.targetLanguageSelect.appendChild(targetOption);
            }
        } catch (error) {
            console.error('言語一覧の読み込みエラー:', error);
            this.showError('言語一覧の読み込みに失敗しました');
        }
    }

    setupEventListeners() {
        // ファイル選択リンククリック
        this.fileSelectLink.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.documentFileInput.click();
        });
        
        // ファイル選択
        this.documentFileInput.addEventListener('change', (e) => {
            const file = e.target.files[0];
            this.handleFileSelect(file);
        });
        
        // ファイル削除
        this.removeFileBtn.addEventListener('click', () => {
            this.clearFile();
        });
        
        // 翻訳先言語選択
        this.targetLanguageSelect.addEventListener('change', () => {
            this.updateTranslateButton();
        });
        
        // 翻訳ボタンクリック
        this.translateBtn.addEventListener('click', () => {
            this.translateDocument();
        });
        
        // プロンプトリセットボタン
        this.resetPromptsBtn.addEventListener('click', () => {
            this.resetPrompts();
        });
        
        // 翻訳結果を見るボタン
        this.viewResultBtn.addEventListener('click', () => {
            this.showPreview();
        });
        
        // Markdown ダウンロードボタン
        this.downloadMdBtn.addEventListener('click', () => {
            this.downloadMarkdown();
        });
        
        // PDF 変換ボタン
        this.downloadPdfBtn.addEventListener('click', () => {
            this.convertToPdf();
        });
        
        // Markdown コピーボタン
        this.copyMarkdownBtn.addEventListener('click', () => {
            this.copyMarkdown();
        });
    }

    initializeDragAndDrop() {
        // デフォルトの動作を防止
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, (e) => {
                e.preventDefault();
                e.stopPropagation();
            }, false);
        });
        
        // ドラッグ中のスタイル変更
        ['dragenter', 'dragover'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, () => {
                this.dropArea.classList.add('drag-over');
            }, false);
        });
        
        ['dragleave', 'drop'].forEach(eventName => {
            this.dropArea.addEventListener(eventName, () => {
                this.dropArea.classList.remove('drag-over');
            }, false);
        });
        
        // ファイルがドロップされた時の処理
        this.dropArea.addEventListener('drop', (e) => {
            const files = e.dataTransfer.files;
            if (files.length > 0) {
                this.documentFileInput.files = files;
                this.handleFileSelect(files[0]);
            }
        }, false);
    }

    handleFileSelect(file) {
        if (!file) {
            this.clearFile();
            return;
        }
        
        // ファイル形式の検証（PDF, Word のみ）
        const ext = this.getFileExtension(file.name).toLowerCase();
        if (!['pdf', 'docx'].includes(ext)) {
            this.showError('対応形式は PDF (.pdf) と Word (.docx) のみです');
            this.clearFile();
            return;
        }
        
        // ファイルサイズの検証（40MB まで）
        const maxSize = 40 * 1024 * 1024; // 40MB
        if (file.size > maxSize) {
            this.showError('ファイルサイズは 40MB 以下にしてください');
            this.clearFile();
            return;
        }
        
        this.selectedFile = file;
        
        // ドキュメント情報を表示
        this.showDocumentInfo(file);
        
        // エラーをクリア
        this.hideError();
        
        // 結果表示エリアをリセット
        this.resetResultArea();
        
        // 翻訳ボタンの状態を更新
        this.updateTranslateButton();
    }

    clearFile() {
        this.selectedFile = null;
        this.documentFileInput.value = '';
        this.documentInfoContainer.style.display = 'none';
        this.updateTranslateButton();
    }

    showDocumentInfo(file) {
        this.documentInfoContainer.style.display = 'block';
        
        // ファイル情報を設定
        this.fileName.textContent = file.name;
        this.fileSize.textContent = this.formatFileSize(file.size);
        this.fileType.textContent = this.getFileExtension(file.name).toUpperCase();
        
        // ファイルアイコンを設定
        const icon = this.getFileIcon(file.name);
        this.fileIcon.className = `bi ${icon} me-3`;
        this.fileIcon.style.fontSize = '2.5rem';
        this.fileIcon.style.color = '#667eea';
    }

    getFileIcon(fileName) {
        const ext = this.getFileExtension(fileName).toLowerCase();
        const iconMap = {
            'pdf': 'bi-file-earmark-pdf',
            'docx': 'bi-file-earmark-word',
            'doc': 'bi-file-earmark-word'
        };
        return iconMap[ext] || 'bi-file-earmark';
    }

    getFileExtension(fileName) {
        return fileName.split('.').pop() || '';
    }

    formatFileSize(bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
    }

    updateTranslateButton() {
        // ファイルが選択され、翻訳先言語が選択されている場合のみボタンを有効化
        const isValid = this.selectedFile !== null && 
                       this.targetLanguageSelect.value !== '';
        this.translateBtn.disabled = !isValid;
    }

    resetPrompts() {
        this.systemPromptInput.value = this.defaultSystemPrompt;
        this.userPromptInput.value = this.defaultUserPrompt;
    }

    async translateDocument() {
        if (!this.selectedFile) {
            this.showError('ドキュメントを選択してください');
            return;
        }
        
        const targetLanguage = this.targetLanguageSelect.value;
        if (!targetLanguage) {
            this.showError('翻訳先言語を選択してください');
            return;
        }
        
        try {
            // ローディング表示
            this.showLoading();
            this.translateBtn.disabled = true;
            
            // フォームデータの作成
            const formData = new FormData();
            formData.append('document', this.selectedFile);
            formData.append('targetLanguage', targetLanguage);
            
            // ソース言語（オプション）
            const sourceLanguage = this.sourceLanguageSelect.value;
            if (sourceLanguage) {
                formData.append('sourceLanguage', sourceLanguage);
            }
            
            // プロンプト
            const systemPrompt = this.systemPromptInput.value.trim();
            const userPrompt = this.userPromptInput.value.trim();
            if (systemPrompt) {
                formData.append('systemPrompt', systemPrompt);
            }
            if (userPrompt) {
                formData.append('userPrompt', userPrompt);
            }
            
            // オプション
            const tone = this.toneSelect.value;
            const domain = this.domainSelect.value;
            if (tone) {
                formData.append('tone', tone);
            }
            if (domain) {
                formData.append('domain', domain);
            }
            
            // アンチフォージェリトークンを取得
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            
            // 翻訳リクエスト
            const response = await fetch('?handler=Translate', {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token
                },
                body: formData
            });
            
            if (!response.ok) {
                // エラーレスポンスの処理
                const errorData = await response.json().catch(() => ({ error: 'エラーが発生しました' }));
                throw new Error(errorData.error || `エラーが発生しました (${response.status})`);
            }
            
            // 翻訳成功
            const result = await response.json();
            
            // Blob 名を保存
            this.currentBlobName = result.blobName;
            this.currentBlobNameInput.value = result.blobName;
            
            // 成功メッセージを表示
            this.showSuccess(result);
            
        } catch (error) {
            console.error('翻訳エラー:', error);
            this.showError(error.message || '翻訳中にエラーが発生しました');
            this.hideLoading();
        } finally {
            this.updateTranslateButton();
        }
    }

    async showPreview() {
        if (!this.currentBlobName) {
            this.showError('翻訳結果がありません');
            return;
        }
        
        // モーダルを表示
        this.previewModal.show();
        this.previewLoading.style.display = 'block';
        this.previewContent.style.display = 'none';
        
        try {
            // Markdown を取得
            const response = await fetch(`?handler=Result&blobName=${encodeURIComponent(this.currentBlobName)}`);
            
            if (!response.ok) {
                throw new Error('翻訳結果の取得に失敗しました');
            }
            
            const result = await response.json();
            this.currentMarkdown = result.markdown;
            
            // Markdown を HTML に変換
            const html = marked.parse(result.markdown);
            
            // HTML を表示
            this.previewContent.innerHTML = html;
            this.previewLoading.style.display = 'none';
            this.previewContent.style.display = 'block';
            
        } catch (error) {
            console.error('プレビューエラー:', error);
            this.previewContent.innerHTML = `<div class="alert alert-danger">${error.message}</div>`;
            this.previewLoading.style.display = 'none';
            this.previewContent.style.display = 'block';
        }
    }

    async downloadMarkdown() {
        if (!this.currentBlobName) {
            this.showError('翻訳結果がありません');
            return;
        }
        
        try {
            // Markdown ダウンロード用のリンクを生成
            const downloadUrl = `?handler=DownloadMarkdown&blobName=${encodeURIComponent(this.currentBlobName)}`;
            
            // ダウンロード
            const a = document.createElement('a');
            a.href = downloadUrl;
            a.download = this.currentBlobName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            
        } catch (error) {
            console.error('ダウンロードエラー:', error);
            this.showError('Markdown のダウンロードに失敗しました');
        }
    }

    async convertToPdf() {
        if (!this.currentBlobName) {
            this.showError('翻訳結果がありません');
            return;
        }
        
        try {
            // PDF 変換中の表示
            this.downloadPdfBtn.disabled = true;
            this.downloadPdfBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span> 変換中...';
            
            // アンチフォージェリトークンを取得
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            
            // PDF 変換リクエスト
            const response = await fetch(`?handler=ConvertToPdf&blobName=${encodeURIComponent(this.currentBlobName)}`, {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token
                }
            });
            
            if (!response.ok) {
                throw new Error('PDF 変換に失敗しました');
            }
            
            // PDF をダウンロード
            const blob = await response.blob();
            const pdfFileName = this.currentBlobName.replace('.md', '.pdf');
            
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = pdfFileName;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
            
        } catch (error) {
            console.error('PDF 変換エラー:', error);
            this.showError(error.message || 'PDF 変換に失敗しました');
        } finally {
            this.downloadPdfBtn.disabled = false;
            this.downloadPdfBtn.innerHTML = '<i class="bi bi-file-pdf"></i> PDF 変換';
        }
    }

    async copyMarkdown() {
        if (!this.currentMarkdown) {
            // Markdown がまだ読み込まれていない場合は取得
            try {
                const response = await fetch(`?handler=Result&blobName=${encodeURIComponent(this.currentBlobName)}`);
                if (response.ok) {
                    const result = await response.json();
                    this.currentMarkdown = result.markdown;
                }
            } catch (error) {
                console.error('Markdown 取得エラー:', error);
            }
        }
        
        if (this.currentMarkdown) {
            try {
                await navigator.clipboard.writeText(this.currentMarkdown);
                
                // ボタンのテキストを一時的に変更
                const originalText = this.copyMarkdownBtn.innerHTML;
                this.copyMarkdownBtn.innerHTML = '<i class="bi bi-check"></i> コピーしました';
                setTimeout(() => {
                    this.copyMarkdownBtn.innerHTML = originalText;
                }, 2000);
                
            } catch (error) {
                console.error('コピーエラー:', error);
            }
        }
    }

    showLoading() {
        this.noResultMessage.style.display = 'none';
        this.successResult.style.display = 'none';
        this.loadingInResult.style.display = 'block';
    }

    hideLoading() {
        this.loadingInResult.style.display = 'none';
        this.noResultMessage.style.display = 'block';
    }

    showSuccess(result) {
        this.loadingInResult.style.display = 'none';
        this.noResultMessage.style.display = 'none';
        this.successResult.style.display = 'block';
        
        // 翻訳情報を表示
        this.resultSourceLang.textContent = result.sourceLanguage || '自動検出';
        this.resultTargetLang.textContent = result.targetLanguage;
        this.resultCharCount.textContent = result.characterCount.toLocaleString() + ' 文字';
        this.resultTokens.textContent = result.tokensUsed.toLocaleString() + ' トークン';
        this.resultDuration.textContent = result.duration.toFixed(1) + ' 秒';
    }

    resetResultArea() {
        this.noResultMessage.style.display = 'block';
        this.loadingInResult.style.display = 'none';
        this.successResult.style.display = 'none';
        this.currentBlobName = null;
        this.currentMarkdown = null;
    }

    showError(message) {
        this.errorArea.style.display = 'block';
        this.errorMessage.textContent = message;
    }

    hideError() {
        this.errorArea.style.display = 'none';
        this.errorMessage.textContent = '';
    }
}

// ページ読み込み時に初期化
document.addEventListener('DOMContentLoaded', function() {
    new GptTranslatorApp();
});
