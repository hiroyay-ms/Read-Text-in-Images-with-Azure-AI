// OCR アプリケーションのクライアントサイドスクリプト

document.addEventListener('DOMContentLoaded', function () {
    const imageFileInput = document.getElementById('imageFile');
    const runBtn = document.getElementById('runBtn');
    const errorArea = document.getElementById('errorArea');
    const imagePreview = document.getElementById('imagePreview');
    const noImageText = document.getElementById('noImageText');
    const textAreaContainer = document.getElementById('textAreaContainer');
    const noTextMessage = document.getElementById('noTextMessage');
    const loadingInText = document.getElementById('loadingInText');
    const extractedText = document.getElementById('extractedText');
    const copyBtn = document.getElementById('copyBtn');
    const pageCount = document.getElementById('pageCount');
    const lineCount = document.getElementById('lineCount');
    const confidence = document.getElementById('confidence');
    const detailsArea = document.getElementById('detailsArea');

    let selectedFile = null;

    // ファイル選択時の処理
    imageFileInput.addEventListener('change', function (e) {
        const file = e.target.files[0];
        if (file) {
            selectedFile = file;
            
            // 画像プレビューを表示
            const reader = new FileReader();
            reader.onload = function (event) {
                imagePreview.src = event.target.result;
                imagePreview.style.display = 'block';
                noImageText.style.display = 'none';
                runBtn.disabled = false;
            };
            reader.readAsDataURL(file);

            // エラーをクリア
            hideError();
            
            // テキスト表示エリアをリセット
            resetTextArea();
        } else {
            selectedFile = null;
            imagePreview.style.display = 'none';
            noImageText.style.display = 'block';
            runBtn.disabled = true;
        }
    });

    // Run ボタンクリック時の処理
    runBtn.addEventListener('click', async function () {
        if (!selectedFile) {
            showError('ファイルが選択されていません');
            return;
        }

        // UI を更新
        runBtn.disabled = true;
        hideError();
        showLoading();

        try {
            // FormData を作成
            const formData = new FormData();
            formData.append('imageFile', selectedFile);
            
            // CSRF トークンを追加
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            formData.append('__RequestVerificationToken', token);

            // サーバーにアップロード
            const response = await fetch('/OCR?handler=Upload', {
                method: 'POST',
                body: formData
            });

            hideLoading();

            if (!response.ok) {
                const errorText = await response.text();
                let errorMessage = 'エラーが発生しました';
                try {
                    const errorJson = JSON.parse(errorText);
                    errorMessage = errorJson.error || errorMessage;
                } catch {
                    errorMessage = errorText || errorMessage;
                }
                throw new Error(errorMessage);
            }

            const result = await response.json();

            // 結果を表示
            displayResult(result);
        } catch (error) {
            hideLoading();
            resetTextArea();
            showError(error.message);
            runBtn.disabled = false;
        }
    });

    // コピーボタンクリック時の処理
    copyBtn.addEventListener('click', function () {
        const text = extractedText.textContent;
        navigator.clipboard.writeText(text).then(function () {
            // 成功時のフィードバック
            const originalText = copyBtn.innerHTML;
            copyBtn.innerHTML = '<i class="bi bi-check-circle"></i> コピーしました';
            copyBtn.classList.remove('btn-secondary');
            copyBtn.classList.add('btn-success');

            setTimeout(function () {
                copyBtn.innerHTML = originalText;
                copyBtn.classList.remove('btn-success');
                copyBtn.classList.add('btn-secondary');
            }, 2000);
        }).catch(function (err) {
            showError('コピーに失敗しました: ' + err);
        });
    });

    // 結果を表示する関数
    function displayResult(result) {
        // すべての要素を非表示
        noTextMessage.style.display = 'none';
        loadingInText.style.display = 'none';
        
        // テキストエリアを左上揃えに変更
        textAreaContainer.classList.remove('align-items-center', 'justify-content-center');
        textAreaContainer.classList.add('align-items-start', 'justify-content-start');
        
        // 抽出されたテキストを表示
        extractedText.textContent = result.extractedText || 'テキストが検出されませんでした';
        extractedText.style.display = 'block';

        // 詳細情報を表示
        pageCount.textContent = result.pageCount || 0;
        lineCount.textContent = result.lines ? result.lines.length : 0;
        confidence.textContent = result.confidenceScore 
            ? (result.confidenceScore * 100).toFixed(1) + '%'
            : 'N/A';

        // 詳細情報エリアを表示
        detailsArea.style.display = 'block';

        // コピーボタンを有効化
        copyBtn.disabled = false;

        // Run ボタンを再度有効化
        runBtn.disabled = false;
    }

    // ローディングを表示する関数
    function showLoading() {
        noTextMessage.style.display = 'none';
        extractedText.style.display = 'none';
        loadingInText.style.display = 'block';
        
        // テキストエリアを中央揃えに
        textAreaContainer.classList.add('align-items-center', 'justify-content-center');
        textAreaContainer.classList.remove('align-items-start', 'justify-content-start');
    }

    // ローディングを非表示にする関数
    function hideLoading() {
        loadingInText.style.display = 'none';
    }

    // テキストエリアをリセットする関数
    function resetTextArea() {
        noTextMessage.style.display = 'block';
        extractedText.style.display = 'none';
        loadingInText.style.display = 'none';
        copyBtn.disabled = true;
        detailsArea.style.display = 'none';
        
        // テキストエリアを中央揃えに
        textAreaContainer.classList.add('align-items-center', 'justify-content-center');
        textAreaContainer.classList.remove('align-items-start', 'justify-content-start');
    }

    // エラーを表示する関数
    function showError(message) {
        errorArea.textContent = message;
        errorArea.style.display = 'block';
    }

    // エラーを非表示にする関数
    function hideError() {
        errorArea.style.display = 'none';
        errorArea.textContent = '';
    }
});
