using STranslate.Plugin.Ocr.SiliconFlow;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;

namespace STranslate.Plugin.Ocr.SiliconFlow.Tests;

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
        var result = adapter.ParseResponse("line1\nline2", Request, new Settings());

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
    public void Resolve_UnknownModel_FallsBackToQwen()
    {
        Assert.IsType<QwenAdapter>(OcrModelRegistry.Resolve(new Settings { Model = "unknown/model" }));
        Assert.IsType<QwenAdapter>(OcrModelRegistry.Resolve(new Settings { Model = OcrModelRegistry.CustomModelId }));
    }

    [Fact]
    public void ResolveRequestModel_CustomModelUsesUserInput()
    {
        var settings = new Settings { Model = OcrModelRegistry.CustomModelId, CustomModel = " Qwen/Qwen3-VL-8B-Instruct " };
        Assert.Equal("Qwen/Qwen3-VL-8B-Instruct", new QwenAdapter().ResolveRequestModel(settings));
        // 非自定义时用默认 ID
        Assert.Equal("Qwen/Qwen3.5-4B", new QwenAdapter().ResolveRequestModel(new Settings()));
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
