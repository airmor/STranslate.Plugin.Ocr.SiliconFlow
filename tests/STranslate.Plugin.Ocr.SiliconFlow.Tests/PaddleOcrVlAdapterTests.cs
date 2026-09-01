using STranslate.Plugin.Ocr.SiliconFlow;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;

namespace STranslate.Plugin.Ocr.SiliconFlow.Tests;

public class PaddleOcrVlAdapterTests
{
    private static OcrRequest MakeRequest(int width = 1000, int height = 500) =>
        new([0xFF], LangEnum.Auto, width, height);

    [Fact]
    public void BuildPromptText_AllModes()
    {
        var adapter = new PaddleOcrVlAdapter();
        var settings = new Settings();

        settings.PaddleMode = nameof(PaddleOcrMode.Document);
        Assert.Equal("OCR:", adapter.BuildPromptText(settings));

        settings.PaddleMode = nameof(PaddleOcrMode.Spotting);
        Assert.Equal("Spotting:", adapter.BuildPromptText(settings));

        settings.PaddleMode = nameof(PaddleOcrMode.Table);
        Assert.Equal("Table Recognition:", adapter.BuildPromptText(settings));

        settings.PaddleMode = nameof(PaddleOcrMode.Formula);
        Assert.Equal("Formula Recognition:", adapter.BuildPromptText(settings));
    }

    [Fact]
    public void ParseResponse_Spotting_ParsesLocTokens()
    {
        var adapter = new PaddleOcrVlAdapter();
        var settings = new Settings { PaddleMode = nameof(PaddleOcrMode.Spotting) };
        // 一行 CJK 文本 + 一行公式 + 一行空 + 一行坏（7 token）
        var content = """
            你好世界<|LOC_100|><|LOC_50|><|LOC_300|><|LOC_60|><|LOC_300|><|LOC_100|><|LOC_100|><|LOC_90|>
            $E=mc^2$<|LOC_400|><|LOC_60|><|LOC_600|><|LOC_70|><|LOC_600|><|LOC_120|><|LOC_400|><|LOC_110|>

            坏行只有七个<|LOC_1|><|LOC_2|><|LOC_3|><|LOC_4|><|LOC_5|><|LOC_6|><|LOC_7|>
            """;

        var result = adapter.ParseResponse(content, MakeRequest(1000, 500));

        Assert.True(result.IsSuccess);
        var lines = result.Regions[0].Paragraphs[0].Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("你好世界", lines[0].Text);
        Assert.Equal("$E=mc^2$", lines[1].Text);
    }

    [Fact]
    public void ParseResponse_Spotting_DenormalizesCoords()
    {
        var adapter = new PaddleOcrVlAdapter();
        var content = "text<|LOC_500|><|LOC_250|><|LOC_500|><|LOC_250|><|LOC_500|><|LOC_250|><|LOC_500|><|LOC_250|>";

        // 1000×500 图：x 换算 LOC 500→250px, y 换算 LOC 250→125px
        // 注意：LOC 值与像素同值时（500/1000×1000），实测若未换算会得 500——本断言验证换算确实发生
        var result = adapter.ParseResponse(content, MakeRequest(500, 250));

        var box = result.Regions[0].Paragraphs[0].Lines[0].BoxPoints;
        Assert.Equal(4, box.Count);
        Assert.Equal(250f, box[0].X, 2);
        Assert.Equal(62.5f, box[0].Y, 2);
    }

    [Fact]
    public void ParseResponse_Spotting_ZeroPixel_FallsBackToRawCoords()
    {
        var adapter = new PaddleOcrVlAdapter();
        var content = "text<|LOC_500|><|LOC_500|><|LOC_500|><|LOC_500|><|LOC_500|><|LOC_500|><|LOC_500|><|LOC_500|>";

        // 旧宿主场景：PixelWidth/Height = 0，坐标原样返回
        var result = adapter.ParseResponse(content, MakeRequest(0, 0));

        var box = result.Regions[0].Paragraphs[0].Lines[0].BoxPoints;
        Assert.Equal(500f, box[0].X, 2);
    }

    [Fact]
    public void ParseResponse_Spotting_NineTokens_TakesFirstEight()
    {
        var adapter = new PaddleOcrVlAdapter();
        // 9 个 token：前 8 个构成框，第 9 个被忽略
        var content = "text<|LOC_10|><|LOC_11|><|LOC_12|><|LOC_13|><|LOC_14|><|LOC_15|><|LOC_16|><|LOC_17|><|LOC_18|>";

        var result = adapter.ParseResponse(content, MakeRequest(1000, 1000));

        var box = result.Regions[0].Paragraphs[0].Lines[0].BoxPoints;
        Assert.Equal(4, box.Count);
        Assert.Equal(10f, box[0].X, 2);   // 第 8 个 token(18) 不应出现
    }

    [Fact]
    public void ParseResponse_TextWithoutLocTokens_FallsBackToPlainText()
    {
        // 非 Spotting 输出（无任何 LOC token）→ Markdown 纯文本透传，而非报错
        var adapter = new PaddleOcrVlAdapter();
        var content = "全是坏行\n没有坐标";

        var result = adapter.ParseResponse(content, MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.OcrContents.Count);
        Assert.Equal("全是坏行", result.OcrContents[0].Text);
    }

    [Fact]
    public void ParseResponse_DocumentMode_MarkdownLinesPreserved()
    {
        var adapter = new PaddleOcrVlAdapter();
        var settings = new Settings { PaddleMode = nameof(PaddleOcrMode.Document) };
        Assert.False(adapter.SupportsCoordinates(settings));

        var content = "# 标题\n\n中英混排 text and 公式 $\\alpha + \\beta = \\pi$\n\n| a | b |";
        var result = adapter.ParseResponse(content, MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("# 标题", result.OcrContents[0].Text);
        Assert.Equal("中英混排 text and 公式 $\\alpha + \\beta = \\pi$", result.OcrContents[2].Text);
        Assert.Equal("| a | b |", result.OcrContents[^1].Text);
    }

    [Fact]
    public void ParseResponse_EmptyContent_Fails()
    {
        var adapter = new PaddleOcrVlAdapter();
        Assert.False(adapter.ParseResponse("", MakeRequest()).IsSuccess);
        Assert.False(adapter.ParseResponse("   \n  ", MakeRequest()).IsSuccess);
    }

    [Fact]
    public void SupportsCoordinates_OnlySpotting()
    {
        var adapter = new PaddleOcrVlAdapter();
        Assert.True(new Settings { PaddleMode = nameof(PaddleOcrMode.Spotting) } is var s1
                    && adapter.SupportsCoordinates(s1));
        Assert.False(adapter.SupportsCoordinates(new Settings { PaddleMode = nameof(PaddleOcrMode.Document) }));
        Assert.False(adapter.SupportsCoordinates(new Settings { PaddleMode = nameof(PaddleOcrMode.Table) }));
    }
}
