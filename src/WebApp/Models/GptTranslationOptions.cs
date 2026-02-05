namespace WebApp.Models;

/// <summary>
/// GPT-4o 翻訳のオプション設定
/// </summary>
public class GptTranslationOptions
{
    /// <summary>
    /// 翻訳元言語（オプション、null の場合は自動検出）
    /// </summary>
    public string? SourceLanguage { get; set; }

    /// <summary>
    /// トーン（例: formal, casual, technical）
    /// </summary>
    public string? Tone { get; set; }

    /// <summary>
    /// ドメイン（例: legal, medical, technical, general）
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// カスタムシステムプロンプト（空の場合はデフォルトを使用）
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// カスタムユーザープロンプト（空の場合はデフォルトを使用）
    /// </summary>
    public string? UserPrompt { get; set; }

    /// <summary>
    /// カスタム指示（追加の翻訳指示）
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// 構造（見出し、リスト）を保持するかどうか
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// デフォルトのシステムプロンプト
    /// </summary>
    public static string DefaultSystemPrompt => @"あなたはプロフェッショナルな翻訳者です。
以下のルールに従って翻訳してください：

1. 原文の意味を正確に保ちながら、自然な表現で翻訳する
2. 文書の構造（見出し、リスト、表）を Markdown 形式で保持する
3. 専門用語は適切に翻訳し、必要に応じて原語を括弧内に残す
4. 文化的なニュアンスを考慮した翻訳を行う
5. 画像プレースホルダー（[IMAGE:xxx]）はそのまま保持し、翻訳しない
6. 元のドキュメントのレイアウト（段落構成、見出し階層）を可能な限り再現する";

    /// <summary>
    /// デフォルトのユーザープロンプトテンプレート
    /// {targetLanguage} はプレースホルダーとして置換されます
    /// </summary>
    public static string DefaultUserPromptTemplate => @"以下のテキストを{targetLanguage}に翻訳してください。

注意事項:
- 画像プレースホルダー [IMAGE:xxx] は翻訳せず、そのままの位置に残してください
- 見出しの階層（#, ##, ### など）を維持してください
- 表形式は Markdown テーブル形式で保持してください";

    /// <summary>
    /// 有効なシステムプロンプトを取得します（カスタムまたはデフォルト）
    /// </summary>
    public string GetEffectiveSystemPrompt()
    {
        return string.IsNullOrWhiteSpace(SystemPrompt) ? DefaultSystemPrompt : SystemPrompt;
    }

    /// <summary>
    /// 有効なユーザープロンプトを取得します（カスタムまたはデフォルト）
    /// </summary>
    /// <param name="targetLanguage">翻訳先言語名</param>
    public string GetEffectiveUserPrompt(string targetLanguage)
    {
        var template = string.IsNullOrWhiteSpace(UserPrompt) ? DefaultUserPromptTemplate : UserPrompt;
        return template.Replace("{targetLanguage}", targetLanguage);
    }
}
