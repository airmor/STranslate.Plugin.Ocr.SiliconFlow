using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Ocr.SiliconFlow.Adapters;

/// <summary>
/// PaddleOCR-VL-1.5 适配器。
/// 官方提示词模板（硬编码，UI 只允许枚举切换）：
///   OCR:（文档解析）/ Spotting:（文本定位，逐行文字+LOC 坐标）
///   Table Recognition: / Formula Recognition:
/// Spotting 输出每行：文字<|LOC_x1|><|LOC_y1|>...<|LOC_y4|>（8 个 token 组成四角框，值域 0~999 千分比）
/// </summary>
public partial class PaddleOcrVlAdapter : IOcrModelAdapter
{
    public string ModelId => "PaddlePaddle/PaddleOCR-VL-1.5";
    public string DisplayName => "PaddleOCR-VL-1.5";

    [GeneratedRegex(@"<\|LOC_(\d+)\|>")]
    private static partial Regex LocTokenRegex();

    public string BuildPromptText(Settings settings) => settings.PaddleMode switch
    {
        nameof(PaddleOcrMode.Spotting) => "Spotting:",
        nameof(PaddleOcrMode.Table) => "Table Recognition:",
        nameof(PaddleOcrMode.Formula) => "Formula Recognition:",
        _ => "OCR:"
    };

    public OcrResult ParseResponse(string content, OcrRequest request, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new OcrResult().Fail("未检测到文字");

        if (settings.PaddleMode == nameof(PaddleOcrMode.Spotting)
            && TryParseSpotting(content, request, out var spotting))
            return spotting;

        // 其余模式：Markdown 按行透传（LaTeX 公式原样保留）
        return ParsePlainText(content);
    }

    public bool SupportsCoordinates(Settings settings) =>
        settings.PaddleMode == nameof(PaddleOcrMode.Spotting);

    /// <summary>
    /// Spotting 模式：解析逐行 LOC token 为带坐标的 Regions 结构。
    /// 有效行需 ≥8 个 LOC token；不足或文本为空的行跳过。
    /// </summary>
    private static bool TryParseSpotting(string content, OcrRequest request, out OcrResult result)
    {
        result = new OcrResult();
        var region = new OcrRegion();
        var paragraph = new OcrParagraph();
        var locRegex = LocTokenRegex();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var matches = locRegex.Matches(trimmed);
            if (matches.Count < 8) continue;

            var text = locRegex.Replace(trimmed, "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var coords = matches.Take(8).Select(m => int.Parse(m.Groups[1].Value)).ToList();

            // 4 个角点: (x1,y1) 左上 (x2,y2) 右上 (x3,y3) 右下 (x4,y4) 左下
            var boxPoints = new List<BoxPoint>
            {
                new(coords[0], coords[1]),
                new(coords[2], coords[3]),
                new(coords[4], coords[5]),
                new(coords[6], coords[7])
            };

            paragraph.Lines.Add(new OcrContent
            {
                Text = text,
                BoxPoints = DenormalizeCoords(boxPoints, request)
            });
        }

        if (paragraph.Lines.Count == 0)
            return false;

        region.Paragraphs.Add(paragraph);
        result.Regions.Add(region);
        return true;
    }

    /// <summary>
    /// LOC 千分比坐标（0~999）→ 像素坐标。
    /// PixelWidth/Height 不可用时（旧宿主）返回原始值兜底。
    /// </summary>
    private static List<BoxPoint> DenormalizeCoords(List<BoxPoint> locPoints, OcrRequest request)
    {
        var imgW = request.PixelWidth;
        var imgH = request.PixelHeight;
        if (imgW <= 0 || imgH <= 0)
            return locPoints;

        return locPoints.Select(p => new BoxPoint(
            p.X * imgW / 1000f,
            p.Y * imgH / 1000f
        )).ToList();
    }

    internal static OcrResult ParsePlainText(string content)
    {
        var result = new OcrResult();
        foreach (var line in content.Split('\n'))
        {
            result.OcrContents.Add(new OcrContent { Text = line.TrimEnd('\r') });
        }
        return result;
    }
}
