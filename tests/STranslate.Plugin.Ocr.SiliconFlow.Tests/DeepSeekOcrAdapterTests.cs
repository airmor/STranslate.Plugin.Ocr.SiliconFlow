using STranslate.Plugin.Ocr.SiliconFlow;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;

namespace STranslate.Plugin.Ocr.SiliconFlow.Tests;

public class DeepSeekOcrAdapterTests
{
    private static readonly OcrRequest Request = new([0xFF], LangEnum.Auto, 100, 100);

    /// <summary>真实 API 返回的 grounding 输出样例（像素坐标 + ref 包裹文本）</summary>
    private const string GroundingSample =
        """
        <|ref|>text<|/ref|><|det|>[[81, 77, 737, 157]]<|/det|>
        - 定义：对于有界数列 \(\{x_n\}\)，定义

        <|ref|>text<|/ref|><|det|>[[149, 270, 808, 350]]<|/det|>
        - \(a_n = \inf\{x_n, x_{n+1}, \dots\}\) (单调递增)

        <|ref|>text<|/ref|><|det|>[[149, 460, 817, 540]]<|/det|>
        - \(b_n = \sup\{x_n, x_{n+1}, \dots\}\) (单调递减)
        """;

    [Theory]
    [InlineData(nameof(DeepSeekOcrTemplate.Markdown), "<|grounding|>Convert the document to markdown.")]
    [InlineData(nameof(DeepSeekOcrTemplate.Ocr), "<|grounding|>OCR this image.")]
    [InlineData(nameof(DeepSeekOcrTemplate.FreeOcr), "Free OCR.")]
    [InlineData(nameof(DeepSeekOcrTemplate.ParseFigure), "Parse the figure.")]
    [InlineData(nameof(DeepSeekOcrTemplate.Describe), "Describe this image in detail.")]
    public void BuildPromptText_OfficialTemplates(string template, string expected)
    {
        var adapter = new DeepSeekOcrAdapter();
        var settings = new Settings { DeepSeekTemplate = template };

        var prompt = adapter.BuildPromptText(settings);

        Assert.Equal(expected, prompt);
        // 硅基流动接口红线：不得包含 <image> 占位符
        Assert.DoesNotContain("<image>", prompt);
    }

    [Fact]
    public void BuildPromptText_UnknownTemplate_FallsBackToMarkdown()
    {
        var adapter = new DeepSeekOcrAdapter();
        var settings = new Settings { DeepSeekTemplate = "bogus" };

        Assert.Equal("<|grounding|>Convert the document to markdown.", adapter.BuildPromptText(settings));
    }

    [Fact]
    public void ParseResponse_Grounding_ParsesDetBoxes()
    {
        var adapter = new DeepSeekOcrAdapter();
        var settings = new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Markdown) };

        var result = adapter.ParseResponse(GroundingSample, Request, settings);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Regions[0].Paragraphs.Count);

        // OCR 窗口划选定位依赖 OcrContents 扁平列表（每项带 BoxPoints）——双填断言
        Assert.Equal(3, result.OcrContents.Count);
        Assert.Equal("- 定义：对于有界数列 \\(\\{x_n\\}\\)，定义", result.OcrContents[0].Text);
        Assert.Equal(4, result.OcrContents[0].BoxPoints.Count);

        var first = result.Regions[0].Paragraphs[0];
        Assert.Equal("- 定义：对于有界数列 \\(\\{x_n\\}\\)，定义", first.Lines[0].Text);
        // det 坐标为 0~1000 千分比：100×100 图上 81 千分比 → 8.1px
        Assert.Equal(8.1f, first.BoxPoints[0].X, 1);
        Assert.Equal(7.7f, first.BoxPoints[0].Y, 1);
        Assert.Equal(73.7f, first.BoxPoints[1].X, 1);
        Assert.Equal(15.7f, first.BoxPoints[2].Y, 1);
    }

    [Fact]
    public void ParseResponse_Grounding_NoDetBoxes_FallsBackToText()
    {
        var adapter = new DeepSeekOcrAdapter();
        var settings = new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Markdown) };
        var content = "# Doc\n\n公式 $\\int_0^1 x dx$";

        var result = adapter.ParseResponse(content, Request, settings);

        Assert.True(result.IsSuccess);
        Assert.Equal("# Doc", result.OcrContents[0].Text);
    }

    [Fact]
    public void ParseResponse_FreeOcr_PlainText()
    {
        var adapter = new DeepSeekOcrAdapter();
        var settings = new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.FreeOcr) };

        var result = adapter.ParseResponse("line1\nline2", Request, settings);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.OcrContents.Count);
    }

    [Fact]
    public void ParseResponse_Empty_Fails()
    {
        var adapter = new DeepSeekOcrAdapter();
        Assert.False(adapter.ParseResponse("", Request, new Settings()).IsSuccess);
    }

    [Fact]
    public void SupportsCoordinates_OnlyGroundingTemplates()
    {
        var adapter = new DeepSeekOcrAdapter();
        Assert.True(adapter.SupportsCoordinates(new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Markdown) }));
        Assert.True(adapter.SupportsCoordinates(new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Ocr) }));
        Assert.False(adapter.SupportsCoordinates(new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.FreeOcr) }));
        Assert.False(adapter.SupportsCoordinates(new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.ParseFigure) }));
        Assert.False(adapter.SupportsCoordinates(new Settings { DeepSeekTemplate = nameof(DeepSeekOcrTemplate.Describe) }));
    }
}
