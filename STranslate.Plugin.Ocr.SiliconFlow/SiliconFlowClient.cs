using System.Text.Json.Nodes;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;

namespace STranslate.Plugin.Ocr.SiliconFlow;

/// <summary>
/// 硅基流动 OpenAI 兼容调用层：三个模型共用，无模型特定逻辑。
/// </summary>
public static class SiliconFlowClient
{
    public const string BaseUrl = "https://api.siliconflow.cn/v1/chat/completions";

    public static async Task<OcrResult> RecognizeAsync(
        IPluginContext context,
        Settings settings,
        IOcrModelAdapter adapter,
        OcrRequest request,
        CancellationToken cancellationToken)
    {
        var url = UrlHelper.BuildFinalUrl(BaseUrl, "/v1/chat/completions", UrlPathMatchRule.Strict);

        var content = new
        {
            model = adapter.ModelId,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = $"data:image/png;base64,{Convert.ToBase64String(request.ImageData)}"
                            }
                        },
                        new
                        {
                            type = "text",
                            text = adapter.BuildPromptText(settings)
                        }
                    }
                }
            },
            temperature = Math.Clamp(settings.Temperature, 0, 2),
            max_tokens = settings.MaxTokens
        };

        var options = new Options
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 300))
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            options.Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + settings.ApiKey }
            };
        }

        var response = await context.HttpService.PostAsync(url, content, options, cancellationToken);
        var parsedData = JsonNode.Parse(response);
        var responseContent = parsedData?["choices"]?[0]?["message"]?["content"]?.ToString()
            ?? throw new InvalidOperationException($"No data\nRaw: {response}");

        return adapter.ParseResponse(responseContent, request);
    }

    /// <summary>从错误信息中移除 ApiKey，防止泄漏（参考 Bailian 插件 Redact 模式）</summary>
    public static string Redact(string message, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(message))
            return message;
        return message.Replace(apiKey, "***");
    }
}
