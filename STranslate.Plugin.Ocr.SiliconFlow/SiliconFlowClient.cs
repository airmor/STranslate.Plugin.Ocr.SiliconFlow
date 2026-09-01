using System.IO;
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

        // 硅基流动只接受 png/jpg/jpeg/webp/gif（code 20015）
        // 截图场景常传入 bmp 或坏的 png，统一转码为合法 png
        var (imageData, mime) = ImageTranscoder.ToPng(request.ImageData);

        var content = new
        {
            model = adapter.ResolveRequestModel(settings),
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
                                url = $"data:{mime};base64,{Convert.ToBase64String(imageData)}"
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

        return adapter.ParseResponse(responseContent, request, settings);
    }

    /// <summary>从错误信息中移除 ApiKey，防止泄漏（参考 Bailian 插件 Redact 模式）</summary>
    public static string Redact(string message, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(message))
            return message;
        return message.Replace(apiKey, "***");
    }
}

/// <summary>
/// 图片转码：保证发给硅基流动的是合法 PNG。
/// 不引入依赖，用 System.Drawing（Windows 自带）做 bmp→png 与 png 修复重编码。
/// </summary>
internal static class ImageTranscoder
{
    public static (byte[] Data, string Mime) ToPng(byte[] image)
    {
        try
        {
            using var ms = new MemoryStream(image);
            using var bitmap = new System.Drawing.Bitmap(ms);
            using var outMs = new MemoryStream();
            bitmap.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
            return (outMs.ToArray(), "image/png");
        }
        catch (Exception)
        {
            // 解码失败：原样返回（服务端会给出具体报错）
            return (image, "image/png");
        }
    }
}
