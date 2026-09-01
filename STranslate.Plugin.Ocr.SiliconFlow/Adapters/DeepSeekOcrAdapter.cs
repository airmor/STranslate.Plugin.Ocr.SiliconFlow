namespace STranslate.Plugin.Ocr.SiliconFlow.Adapters;

/// <summary>
/// DeepSeek-OCR 适配器。
/// 官方提示词模板原文（硬编码，UI 只允许枚举切换）：
///   &lt;image&gt;\n&lt;|grounding|&gt;Convert the document to markdown.
/// 注意：&lt;image&gt;\n 是本地推理的图像占位约定；硅基流动 chat 接口中图像
/// 已由 image_url 传入，发送前必须剥离占位符，仅保留正文。
/// </summary>
public class DeepSeekOcrAdapter : IOcrModelAdapter
{
    public string ModelId => "deepseek-ai/DeepSeek-OCR";
    public string DisplayName => "DeepSeek-OCR";

    /// <summary>本地推理的图像占位前缀，硅基流动接口下需剥离</summary>
    private const string ImagePlaceholder = "<image>\n";

    public string BuildPromptText(Settings settings) => settings.DeepSeekTemplate switch
    {
        nameof(DeepSeekOcrTemplate.Ocr) => "<|grounding|>OCR this image.",
        nameof(DeepSeekOcrTemplate.FreeOcr) => "Free OCR.",
        nameof(DeepSeekOcrTemplate.ParseFigure) => "Parse the figure.",
        nameof(DeepSeekOcrTemplate.Describe) => "Describe this image in detail.",
        _ => "<|grounding|>Convert the document to markdown."
    };

    public OcrResult ParseResponse(string content, OcrRequest request)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new OcrResult().Fail("未检测到文字");

        // 输出按 Markdown 透传（grounding 模式带布局结构，LaTeX 公式原样保留）
        return PaddleOcrVlAdapter.ParsePlainText(content);
    }

    public bool SupportsCoordinates(Settings settings) => false;
}
