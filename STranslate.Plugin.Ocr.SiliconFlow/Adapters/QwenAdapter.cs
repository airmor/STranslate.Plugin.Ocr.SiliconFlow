namespace STranslate.Plugin.Ocr.SiliconFlow.Adapters;

/// <summary>
/// Qwen3.5-4B 适配器。
/// 通用多模态模型，无官方 OCR 协议——提示词是唯一允许用户编辑的。
/// 输出当纯文本处理，坐标由宿主 Smart 分段推断。
/// </summary>
public class QwenAdapter : IOcrModelAdapter
{
    public string ModelId => "Qwen/Qwen3.5-4B";
    public string DisplayName => "Qwen3.5-4B";

    public string BuildPromptText(Settings settings)
    {
        var prompt = settings.QwenPrompt?.Trim();
        return string.IsNullOrEmpty(prompt)
            ? "识别图中所有文字，保持原有排版结构，公式用 LaTeX 表示，只输出识别内容。"
            : prompt;
    }

    public OcrResult ParseResponse(string content, OcrRequest request, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new OcrResult().Fail("未检测到文字");

        return PaddleOcrVlAdapter.ParsePlainText(content);
    }

    public bool SupportsCoordinates(Settings settings) => false;
}
