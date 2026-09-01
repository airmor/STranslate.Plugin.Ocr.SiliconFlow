namespace STranslate.Plugin.Ocr.SiliconFlow;

/// <summary>
/// 各模型固定提示词模板的目录（供 ILlm Prompts 只读展示）。
/// 协议硬编码红线：这些模板在 UI 上不可编辑。
/// </summary>
public static class PromptCatalog
{
    public static List<Prompt> BuildDefaultPrompts()
    {
        List<Prompt> prompts = [];

        // PaddleOCR-VL-1.5
        prompts.Add(Build("PaddleOCR-VL 文档解析", "OCR:"));
        prompts.Add(Build("PaddleOCR-VL 文本定位", "Spotting:"));
        prompts.Add(Build("PaddleOCR-VL 表格识别", "Table Recognition:"));
        prompts.Add(Build("PaddleOCR-VL 公式识别", "Formula Recognition:"));

        // DeepSeek-OCR（发送前由适配器剥离 <image>\n 占位）
        prompts.Add(Build("DeepSeek 文档转 Markdown", "<|grounding|>Convert the document to markdown."));
        prompts.Add(Build("DeepSeek 通用 OCR", "<|grounding|>OCR this image."));
        prompts.Add(Build("DeepSeek 自由 OCR", "Free OCR."));
        prompts.Add(Build("DeepSeek 图表解析", "Parse the figure."));
        prompts.Add(Build("DeepSeek 图像描述", "Describe this image in detail."));

        // Qwen3.5-4B 自由提示词（默认值，可编辑的只有它）
        prompts.Add(new Prompt(
            "Qwen 自由提示词",
            [new PromptItem("user", "识别图中所有文字，保持原有排版结构，公式用 LaTeX 表示，只输出识别内容。")],
            false));

        return prompts;
    }

    private static Prompt Build(string name, string template) =>
        new(name, [new PromptItem("user", template)], false);
}
