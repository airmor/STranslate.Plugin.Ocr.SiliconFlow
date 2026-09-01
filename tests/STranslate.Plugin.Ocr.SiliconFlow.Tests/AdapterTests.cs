using STranslate.Plugin.Ocr.SiliconFlow;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;

namespace STranslate.Plugin.Ocr.SiliconFlow.Tests;

public class DeepSeekOcrAdapterTests
{
    private static readonly OcrRequest Request = new([0xFF], LangEnum.Auto, 100, 100);

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
    public void ParseResponse_MarkdownPassthrough()
    {
        var adapter = new DeepSeekOcrAdapter();
        var content = "# Doc\n\n公式 $\\int_0^1 x dx$";

        var result = adapter.ParseResponse(content, Request);

        Assert.True(result.IsSuccess);
        Assert.Equal("# Doc", result.OcrContents[0].Text);
        Assert.Equal("公式 $\\int_0^1 x dx$", result.OcrContents[2].Text);
    }

    [Fact]
    public void ParseResponse_Empty_Fails()
    {
        var adapter = new DeepSeekOcrAdapter();
        Assert.False(adapter.ParseResponse("", Request).IsSuccess);
    }

    [Fact]
    public void SupportsCoordinates_AlwaysFalse()
    {
        var adapter = new DeepSeekOcrAdapter();
        Assert.False(adapter.SupportsCoordinates(new Settings()));
    }
}

public class QwenAdapterTests
{
    private static readonly OcrRequest Request = new([0xFF], LangEnum.Auto, 100, 100);

    [Fact]
    public void BuildPromptText_UsesUserPrompt()
    {
        var adapter = new QwenAdapter();
        var settings = new Settings { QwenPrompt = "自定义提示词" };

        Assert.Equal("自定义提示词", adapter.BuildPromptText(settings));
    }

    [Fact]
    public void BuildPromptText_EmptyPrompt_FallsBackToDefault()
    {
        var adapter = new QwenAdapter();
        var settings = new Settings { QwenPrompt = "   " };

        Assert.Equal("识别图中所有文字，保持原有排版结构，公式用 LaTeX 表示，只输出识别内容。",
            adapter.BuildPromptText(settings));
    }

    [Fact]
    public void ParseResponse_TextLines()
    {
        var adapter = new QwenAdapter();
        var result = adapter.ParseResponse("line1\nline2", Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.OcrContents.Count);
        Assert.Equal("line1", result.OcrContents[0].Text);
    }

    [Fact]
    public void SupportsCoordinates_AlwaysFalse()
    {
        Assert.False(new QwenAdapter().SupportsCoordinates(new Settings()));
    }
}

public class OcrModelRegistryTests
{
    [Fact]
    public void Resolve_KnownModels()
    {
        Assert.IsType<PaddleOcrVlAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "PaddlePaddle/PaddleOCR-VL-1.5" }));
        Assert.IsType<DeepSeekOcrAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "deepseek-ai/DeepSeek-OCR" }));
        Assert.IsType<QwenAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "Qwen/Qwen3.5-4B" }));
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        Assert.IsType<DeepSeekOcrAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "deepseek-ai/deepseek-ocr" }));
    }

    [Fact]
    public void Resolve_UnknownModel_FallsBackToPaddle()
    {
        Assert.IsType<PaddleOcrVlAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "unknown/model" }));
    }
}

public class RedactTests
{
    [Fact]
    public void Redact_RemovesApiKey()
    {
        var msg = "Request failed with Bearer sk-abc123 header";
        Assert.Equal("Request failed with Bearer *** header", SiliconFlowClient.Redact(msg, "sk-abc123"));
    }

    [Fact]
    public void Redact_EmptyKey_ReturnsOriginal()
    {
        Assert.Equal("msg", SiliconFlowClient.Redact("msg", ""));
        Assert.Equal("msg", SiliconFlowClient.Redact("msg", null!));
    }
}
