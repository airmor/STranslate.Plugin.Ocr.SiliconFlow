using System.Text.RegularExpressions;

namespace STranslate.Plugin.Ocr.SiliconFlow.Adapters;

/// <summary>
/// DeepSeek-OCR 适配器。
/// 官方提示词模板原文（硬编码，UI 只允许枚举切换）：
///   &lt;image&gt;\n&lt;|grounding|&gt;Convert the document to markdown.
/// 注意：&lt;image&gt;\n 是本地推理的图像占位约定；硅基流动 chat 接口中图像
/// 已由 image_url 传入，发送前必须剥离占位符，仅保留正文。
/// grounding 模式输出带 &lt;|det|&gt;[[x1,y1,x2,y2]]&lt;|/det|&gt; 像素坐标（可略超图片边界），
/// 解析为 Regions 结构，支持图片翻译选区高亮。
/// </summary>
public partial class DeepSeekOcrAdapter : IOcrModelAdapter
{
    public string ModelId => "deepseek-ai/DeepSeek-OCR";
    public string DisplayName => "DeepSeek-OCR";

    /// <summary>grounding 输出的检测框：[[x1, y1, x2, y2]]</summary>
    [GeneratedRegex(@"<\|det\|>\s*\[\[(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\]\]\s*<\|/det\|>")]
    private static partial Regex DetBoxRegex();

    public string BuildPromptText(Settings settings) => settings.DeepSeekTemplate switch
    {
        nameof(DeepSeekOcrTemplate.Ocr) => "<|grounding|>OCR this image.",
        nameof(DeepSeekOcrTemplate.FreeOcr) => "Free OCR.",
        nameof(DeepSeekOcrTemplate.ParseFigure) => "Parse the figure.",
        nameof(DeepSeekOcrTemplate.Describe) => "Describe this image in detail.",
        _ => "<|grounding|>Convert the document to markdown."
    };

    public OcrResult ParseResponse(string content, OcrRequest request, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new OcrResult().Fail("未检测到文字");

        // grounding 模板输出带 <|det|> 检测框 → 坐标结构
        if (IsGrounding(settings) && TryParseDetBoxes(content, out var boxed))
            return boxed;

        // 其余（Free OCR 等）：Markdown 按行透传（LaTeX 公式原样保留）
        return PaddleOcrVlAdapter.ParsePlainText(content);
    }

    public bool SupportsCoordinates(Settings settings) => IsGrounding(settings);

    /// <summary>带 grounding 的模板（Markdown / Ocr）才有坐标输出</summary>
    private static bool IsGrounding(Settings? settings) =>
        settings is null
        || settings.DeepSeekTemplate is nameof(DeepSeekOcrTemplate.Markdown) or nameof(DeepSeekOcrTemplate.Ocr);

    /// <summary>
    /// 解析 grounding 输出：&lt;|det|&gt;[[x1,y1,x2,y2]]&lt;|/det|&gt; 开启一个块，
    /// 随后的非空文本行属于该块；坐标为像素（左上+右下 → 四角框）。
    /// 同时填充 OcrContents（扁平，宿主 OCR 窗口划选定位依赖）和
    /// Regions（结构化，图片翻译依赖）——官方 Baidu 插件同款双填模式。
    /// 无任何 det 块时返回 false（回退纯文本）。
    /// </summary>
    private static bool TryParseDetBoxes(string content, out OcrResult result)
    {
        result = new OcrResult();
        var region = new OcrRegion();
        OcrParagraph? paragraph = null;

        var detRegex = DetBoxRegex();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var match = detRegex.Match(trimmed);
            if (match.Success)
            {
                // 新检测块：x1,y1 左上 + x2,y2 右下 → 四角（顺时针）
                var x1 = float.Parse(match.Groups[1].Value);
                var y1 = float.Parse(match.Groups[2].Value);
                var x2 = float.Parse(match.Groups[3].Value);
                var y2 = float.Parse(match.Groups[4].Value);

                var boxPoints = new List<BoxPoint>
                {
                    new(x1, y1),
                    new(x2, y1),
                    new(x2, y2),
                    new(x1, y2)
                };

                paragraph = new OcrParagraph { BoxPoints = boxPoints };
                region.Paragraphs.Add(paragraph);
                continue;
            }

            // det 块后的文本行（去掉 <|ref|> 包裹的占位词），继承所在段落框
            var text = trimmed.Replace("<|ref|>", "").Replace("<|/ref|>", "").Trim();
            if (string.IsNullOrEmpty(text) || paragraph is null) continue;

            var ocrContent = new OcrContent
            {
                Text = text,
                BoxPoints = paragraph.BoxPoints
            };
            paragraph.Lines.Add(ocrContent);
            result.OcrContents.Add(ocrContent);
        }

        var hasLines = region.Paragraphs.Any(p => p.Lines.Count > 0);
        if (!hasLines)
            return false;

        region.BoxPoints = CreateUnionBoxPoints(region.Paragraphs.Select(p => p.BoxPoints));
        result.Regions.Add(region);
        return true;
    }

    private static List<BoxPoint> CreateUnionBoxPoints(IEnumerable<IReadOnlyList<BoxPoint>> groups)
    {
        var valid = groups.Where(g => g.Count > 0).ToList();
        if (valid.Count == 0) return [];

        float minX = valid.Min(g => g.Min(p => p.X));
        float minY = valid.Min(g => g.Min(p => p.Y));
        float maxX = valid.Max(g => g.Max(p => p.X));
        float maxY = valid.Max(g => g.Max(p => p.Y));

        return
        [
            new BoxPoint(minX, minY),
            new BoxPoint(maxX, minY),
            new BoxPoint(maxX, maxY),
            new BoxPoint(minX, maxY)
        ];
    }
}
