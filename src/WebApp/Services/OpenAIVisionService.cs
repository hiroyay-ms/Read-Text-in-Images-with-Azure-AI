using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;

namespace WebApp.Services;

/// <summary>
/// Azure OpenAI の GPT-4o を使用した画像テキスト抽出サービス
/// </summary>
public class OpenAIVisionService : IGptVisionService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;
    private readonly ILogger<OpenAIVisionService> _logger;

    public OpenAIVisionService(
        IConfiguration configuration,
        ILogger<OpenAIVisionService> logger)
    {
        _logger = logger;
        
        var endpoint = configuration["AzureOpenAI:Endpoint"] 
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint が設定されていません");
        _deploymentName = configuration["AzureOpenAI:DeploymentName"] 
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName が設定されていません");

        _client = new AzureOpenAIClient(
            new Uri(endpoint),
            new DefaultAzureCredential());

        _logger.LogInformation("OpenAI Vision Service initialized with endpoint: {Endpoint}, deployment: {Deployment}", 
            endpoint, _deploymentName);
    }

    /// <summary>
    /// 画像からテキストを抽出します
    /// </summary>
    public async Task<string> ExtractTextFromImageAsync(
        Stream imageStream, 
        string? prompt = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("画像からテキスト抽出を開始します");

            // 画像を Base64 エンコード
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            var imageBytes = memoryStream.ToArray();
            var base64Image = Convert.ToBase64String(imageBytes);

            // デフォルトプロンプトまたはカスタムプロンプトを使用
            var systemPrompt = prompt ?? "この画像に含まれているすべてのテキストを抽出してください。テキストのみを返し、説明や追加情報は含めないでください。";

            // ChatClient を取得
            var chatClient = _client.GetChatClient(_deploymentName);

            // メッセージを構築
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(
                    ChatMessageContentPart.CreateTextPart("この画像からテキストを抽出してください。"),
                    ChatMessageContentPart.CreateImagePart(
                        BinaryData.FromBytes(imageBytes),
                        "image/png"))
            };

            // GPT-4o を呼び出し
            var response = await chatClient.CompleteChatAsync(
                messages,
                cancellationToken: cancellationToken);

            var extractedText = response.Value.Content[0].Text;

            _logger.LogInformation("テキスト抽出が完了しました。文字数: {Length}", extractedText?.Length ?? 0);

            return extractedText ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像からのテキスト抽出中にエラーが発生しました");
            throw new InvalidOperationException("画像からのテキスト抽出に失敗しました。", ex);
        }
    }
}
