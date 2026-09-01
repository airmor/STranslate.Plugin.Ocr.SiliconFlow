using STranslate.Plugin;
using STranslate.Plugin.Ocr.SiliconFlow;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;
using System.Text.Json.Nodes;

// 真实 API 冒烟测试：不走宿主，直接调 SiliconFlowClient（绕过 IPluginContext，用 HttpClient）
// 用法：dotnet run --project tests/ConsoleTest -- <图片路径>
// ApiKey 从环境变量 SILICONFLOW_API_KEY 读取（绝不硬编码）

var apiKey = Environment.GetEnvironmentVariable("SILICONFLOW_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("请先设置环境变量 SILICONFLOW_API_KEY");
    return 1;
}

var imagePath = args.Length > 0 ? args[0] : null;
if (imagePath is null || !File.Exists(imagePath))
{
    Console.Error.WriteLine("用法：dotnet run -- <图片路径>");
    return 1;
}

var imageData = await File.ReadAllBytesAsync(imagePath);
using var probe = System.Drawing.Image.FromFile(imagePath);
int width = probe.Width, height = probe.Height;
probe.Dispose();

Console.WriteLine($"图片：{imagePath}（{width}×{height}，{imageData.Length} bytes）\n");

var settings = new Settings { ApiKey = apiKey };
var scenarios = new (string Title, Action<Settings> Configure)[]
{
    ("PaddleOCR-VL-1.5 · OCR:（文档解析）", s => { s.Model = "PaddlePaddle/PaddleOCR-VL-1.5"; s.PaddleMode = nameof(PaddleOcrMode.Document); }),
    ("PaddleOCR-VL-1.5 · Spotting:（文本定位，带坐标）", s => { s.Model = "PaddlePaddle/PaddleOCR-VL-1.5"; s.PaddleMode = nameof(PaddleOcrMode.Spotting); }),
    ("DeepSeek-OCR · Convert to markdown（grounding）", s => { s.Model = "deepseek-ai/DeepSeek-OCR"; s.DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Markdown); }),
    ("Qwen3.5-4B · 自由提示词", s => { s.Model = "Qwen/Qwen3.5-4B"; }),
};

foreach (var (title, configure) in scenarios)
{
    Console.WriteLine($"━━━ {title} ━━━");
    configure(settings);
    var adapter = OcrModelRegistry.Resolve(settings);

    try
    {
        var request = new OcrRequest(imageData, LangEnum.Auto, width, height);
        // 先打印模型原始输出（诊断坐标格式用），再打印解析结果
        var raw = await HttpRawAsync(adapter, settings, request);
        Console.WriteLine($"  [原始输出] {raw.Replace("\n", "\n    ").Truncate(600)}");
        Console.WriteLine();

        var result = adapter.ParseResponse(raw, request, settings);
        if (!result.IsSuccess)
        {
            Console.WriteLine($"  ✗ {result.ErrorMessage}");
            continue;
        }

        if (result.OcrContents.Any(c => c.BoxPoints.Count > 0))
        {
            var boxed = result.OcrContents.Where(c => c.BoxPoints.Count > 0).ToList();
            Console.WriteLine($"  ✓ {boxed.Count} 行（含坐标）");
            foreach (var line in boxed.Take(5))
            {
                var box = line.BoxPoints.FirstOrDefault();
                Console.WriteLine($"    [{box.X:F0},{box.Y:F0}] {line.Text}");
            }
        }
        else
        {
            Console.WriteLine($"  ✓ {result.OcrContents.Count} 行（纯文本，无坐标）");
            foreach (var content in result.OcrContents.Take(8))
                Console.WriteLine($"    {content.Text}");
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"  ✗ {SiliconFlowClient.Redact(exception.Message, apiKey)}");
    }
    Console.WriteLine();
}

return 0;

// ConsoleTest 无法构造 IPluginContext，这里内联一份最小 HTTP 调用（逻辑与 SiliconFlowClient 一致）
static async Task<string> HttpRawAsync(IOcrModelAdapter adapter, Settings settings, OcrRequest request)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

    var body = new
    {
        model = adapter.ModelId,
        messages = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "image_url", image_url = new { url = $"data:image/png;base64,{Convert.ToBase64String(request.ImageData)}" } },
                    new { type = "text", text = adapter.BuildPromptText(settings) }
                }
            }
        },
        temperature = Math.Clamp(settings.Temperature, 0, 2),
        max_tokens = settings.MaxTokens
    };

    var json = System.Text.Json.JsonSerializer.Serialize(body);
    using var response = await http.PostAsync(SiliconFlowClient.BaseUrl, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    var raw = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();

    var parsed = JsonNode.Parse(raw);
    return parsed?["choices"]?[0]?["message"]?["content"]?.ToString()
        ?? throw new InvalidOperationException($"No data\nRaw: {raw}");
}

internal static class StringExt
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
