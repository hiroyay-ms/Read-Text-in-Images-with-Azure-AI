// Azure Translator ドキュメント翻訳アプリケーション

class TranslatorApp {
    constructor() {
        // DOM 要素の取得
        this.documentFileInput = document.getElementById('documentFile');
        this.fileSelectLink = document.getElementById('fileSelectLink');
        this.translateBtn = document.getElementById('translateBtn');
        this.sourceLanguageSelect = document.getElementById('sourceLanguage');
        this.targetLanguageSelect = document.getElementById('targetLanguage');
        this.errorArea = document.getElementById('errorArea');
        this.fileNameDisplay = document.getElementById('fileNameDisplay');
        this.dropArea = document.getElementById('dropArea');
        
        // ドキュメント情報エリア
        this.noDocumentMessage = document.getElementById('noDocumentMessage');
        this.documentInfo = document.getElementById('documentInfo');
        this.fileIcon = document.getElementById('fileIcon');
        this.fileName = document.getElementById('fileName');
        this.fileSize = document.getElementById('fileSize');
        this.fileType = document.getElementById('fileType');
        
        // 結果表示エリア
        this.noResultMessage = document.getElementById('noResultMessage');
        this.loadingInResult = document.getElementById('loadingInResult');
        this.successResult = document.getElementById('successResult');
        this.resultSourceLang = document.getElementById('resultSourceLang');
        this.resultTargetLang = document.getElementById('resultTargetLang');
        this.resultCharCount = document.getElementById('resultCharCount');
        this.resultDuration = document.getElementById('resultDuration');
        
        this.selectedFile = null;
        
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
        
        // 翻訳先言語選択
        this.targetLanguageSelect.addEventListener('change', () => {
            this.updateTranslateButton();
        });
        
        // 翻訳ボタンクリック
        this.translateBtn.addEventListener('click', () => {
            this.translateDocument();
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
            this.selectedFile = null;
            this.resetUI();
            return;
        }
        
        this.selectedFile = file;
        
        // ファイル名を表示
        this.fileNameDisplay.textContent = `(${file.name})`;
        
        // ドキュメント情報を表示
        this.showDocumentInfo(file);
        
        // エラーをクリア
        this.hideError();
        
        // 結果表示エリアをリセット
        this.resetResultArea();
        
        // 翻訳ボタンの状態を更新
        this.updateTranslateButton();
    }

    showDocumentInfo(file) {
        this.noDocumentMessage.style.display = 'none';
        this.documentInfo.style.display = 'block';
        
        // ファイル情報を設定
        this.fileName.textContent = file.name;
        this.fileSize.textContent = this.formatFileSize(file.size);
        this.fileType.textContent = this.getFileExtension(file.name).toUpperCase();
        
        // ファイルアイコンを設定
        const icon = this.getFileIcon(file.name);
        this.fileIcon.className = `bi ${icon}`;
    }

    getFileIcon(fileName) {
        const ext = this.getFileExtension(fileName).toLowerCase();
        const iconMap = {
            'pdf': 'bi-file-earmark-pdf',
            'docx': 'bi-file-earmark-word',
            'doc': 'bi-file-earmark-word',
            'xlsx': 'bi-file-earmark-excel',
            'xls': 'bi-file-earmark-excel',
            'pptx': 'bi-file-earmark-ppt',
            'ppt': 'bi-file-earmark-ppt',
            'html': 'bi-file-earmark-code',
            'htm': 'bi-file-earmark-code',
            'txt': 'bi-file-earmark-text',
            'csv': 'bi-file-earmark-spreadsheet',
            'tsv': 'bi-file-earmark-spreadsheet'
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
            
            const sourceLanguage = this.sourceLanguageSelect.value;
            if (sourceLanguage) {
                formData.append('sourceLanguage', sourceLanguage);
            }
            
            // アンチフォージェリトークンを取得
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            
            // 翻訳リクエスト（同期的に完了を待機）
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
            
            // 翻訳成功 - ファイルをダウンロード
            const blob = await response.blob();
            const contentDisposition = response.headers.get('content-disposition');
            let fileName = 'translated_document';
            
            // ファイル名を抽出
            if (contentDisposition) {
                const matches = /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/.exec(contentDisposition);
                if (matches != null && matches[1]) {
                    fileName = matches[1].replace(/['"]/g, '');
                }
            }
            
            // 翻訳情報をカスタムヘッダーから取得
            const charactersTranslated = response.headers.get('X-Translation-Characters') || '不明';
            const durationSeconds = response.headers.get('X-Translation-Duration') || '不明';
            const sourceLangCode = response.headers.get('X-Translation-Source-Language') || sourceLanguage || 'auto';
            const targetLangCode = response.headers.get('X-Translation-Target-Language') || targetLanguage;
            
            // ファイルをダウンロード
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
            
            // 成功メッセージを表示（翻訳情報を含む）
            this.showSuccess(targetLangCode, sourceLangCode, charactersTranslated, durationSeconds);
            
        } catch (error) {
            console.error('翻訳エラー:', error);
            this.showError(error.message || '翻訳中にエラーが発生しました');
            this.hideLoading();
        } finally {
            this.translateBtn.disabled = false;
        }
    }

    showLoading() {
        this.noResultMessage.style.display = 'none';
        this.successResult.style.display = 'none';
        this.loadingInResult.style.display = 'block';
        this.hideError();
    }

    hideLoading() {
        this.loadingInResult.style.display = 'none';
    }

    showSuccess(targetLang, sourceLang, charactersTranslated, durationSeconds) {
        this.hideLoading();
        this.successResult.style.display = 'block';
        
        // 翻訳情報を表示
        const targetLangName = this.getLanguageName(targetLang);
        const sourceLangName = sourceLang && sourceLang !== 'auto' 
            ? this.getLanguageName(sourceLang) 
            : '自動検出';
        
        this.resultSourceLang.textContent = sourceLangName;
        this.resultTargetLang.textContent = targetLangName;
        
        // 文字数を表示（数値をカンマ区切りでフォーマット）
        if (charactersTranslated && charactersTranslated !== '不明') {
            const formattedCount = Number(charactersTranslated).toLocaleString();
            this.resultCharCount.textContent = `${formattedCount} 文字`;
        } else {
            this.resultCharCount.textContent = '不明';
        }
        
        // 処理時間を表示
        if (durationSeconds && durationSeconds !== '不明') {
            const duration = parseFloat(durationSeconds);
            if (duration < 60) {
                this.resultDuration.textContent = `${duration.toFixed(1)} 秒`;
            } else {
                const minutes = Math.floor(duration / 60);
                const seconds = Math.floor(duration % 60);
                this.resultDuration.textContent = `${minutes} 分 ${seconds} 秒`;
            }
        } else {
            this.resultDuration.textContent = '不明';
        }
    }

    getLanguageName(code) {
        const option = Array.from(this.targetLanguageSelect.options)
            .find(opt => opt.value === code);
        return option ? option.textContent : code;
    }

    resetResultArea() {
        this.noResultMessage.style.display = 'block';
        this.loadingInResult.style.display = 'none';
        this.successResult.style.display = 'none';
    }

    resetUI() {
        this.fileNameDisplay.textContent = '';
        this.noDocumentMessage.style.display = 'block';
        this.documentInfo.style.display = 'none';
        this.translateBtn.disabled = true;
        this.resetResultArea();
    }

    showError(message) {
        this.errorArea.textContent = message;
        this.errorArea.style.display = 'block';
    }

    hideError() {
        this.errorArea.style.display = 'none';
    }
}

// DOMContentLoaded イベントでアプリケーションを初期化
document.addEventListener('DOMContentLoaded', () => {
    new TranslatorApp();
});
