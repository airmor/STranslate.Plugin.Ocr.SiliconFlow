namespace STranslate.Plugin.Ocr.SiliconFlow;

/// <summary>
/// PaddleOCR-VL 识别模式（官方提示词枚举，模板原文硬编码在 Adapter 中）
/// </summary>
public enum PaddleOcrMode
{
    /// <summary>文档解析（Markdown 输出）</summary>
    Document,

    /// <summary>文本定位（逐行文字 + LOC 坐标，支持选区高亮）</summary>
    Spotting,

    /// <summary>表格识别（Markdown 表格输出）</summary>
    Table,

    /// <summary>公式识别（LaTeX 输出）</summary>
    Formula
}

/// <summary>
/// DeepSeek-OCR 官方提示词模板（原文硬编码在 Adapter 中，UI 只允许在此枚举间切换）
/// </summary>
public enum DeepSeekOcrTemplate
{
    /// <summary>文档转 Markdown（带布局 grounding）</summary>
    Markdown,

    /// <summary>通用 OCR（带布局 grounding）</summary>
    Ocr,

    /// <summary>自由 OCR（无布局）</summary>
    FreeOcr,

    /// <summary>图表解析</summary>
    ParseFigure,

    /// <summary>图像详细描述</summary>
    Describe
}

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>模型完整 ID：PaddlePaddle/PaddleOCR-VL-1.5 | deepseek-ai/DeepSeek-OCR | Qwen/Qwen3.5-4B</summary>
    public string Model { get; set; } = "PaddlePaddle/PaddleOCR-VL-1.5";

    /// <summary>PaddleOCR-VL 识别模式</summary>
    public string PaddleMode { get; set; } = nameof(PaddleOcrMode.Document);

    /// <summary>DeepSeek-OCR 官方模板</summary>
    public string DeepSeekTemplate { get; set; } = nameof(DeepSeekOcrTemplate.Markdown);

    /// <summary>Qwen3.5-4B 自由提示词（唯一可编辑的提示词——该模型无官方 OCR 协议）</summary>
    public string QwenPrompt { get; set; } =
        "识别图中所有文字，保持原有排版结构，公式用 LaTeX 表示，只输出识别内容。";

    public double Temperature { get; set; } = 0.0;
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 30;
}
