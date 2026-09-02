using STranslate.Plugin.Ocr.SiliconFlow;

namespace STranslate.Plugin.Ocr.SiliconFlow.Tests;

public class OutputFormatterTests
{
    /// <summary>用户实测 DeepSeek-OCR 输出的 HTML 表格样例</summary>
    private const string HtmlTable =
        "<table><tr><td>维度</td><td>DFS (深度优先)</td><td>BFS (广度优先)</td></tr>" +
        "<tr><td>数据结构</td><td>栈 (Stack)</td><td>队列 (Queue)</td></tr></table>";

    [Fact]
    public void Table_Markdown()
    {
        var result = OutputFormatter.ConvertTables(HtmlTable, OutputFormatter.TableFormat.Markdown);

        Assert.Equal(
            """
            | 维度 | DFS (深度优先) | BFS (广度优先) |
            | --- | --- | --- |
            | 数据结构 | 栈 (Stack) | 队列 (Queue) |
            """,
            result);
    }

    [Fact]
    public void Table_Latex()
    {
        var result = OutputFormatter.ConvertTables(HtmlTable, OutputFormatter.TableFormat.Latex);

        Assert.Contains(@"\begin{tabular}{|ccc|}", result);
        Assert.Contains(@"维度 & DFS (深度优先) & BFS (广度优先) \\", result);
        Assert.Contains(@"\end{tabular}", result);
    }

    [Fact]
    public void Table_Tsv()
    {
        var result = OutputFormatter.ConvertTables(HtmlTable, OutputFormatter.TableFormat.Tsv);

        Assert.Equal("维度\tDFS (深度优先)\tBFS (广度优先)\n数据结构\t栈 (Stack)\t队列 (Queue)", result);
    }

    [Fact]
    public void Table_PipesInCells_AreEscaped()
    {
        var html = "<table><tr><td>a|b</td></tr></table>";
        var result = OutputFormatter.ConvertTables(html, OutputFormatter.TableFormat.Markdown);
        Assert.Contains("a\\|b", result);
    }

    [Fact]
    public void Formula_Dollar_ConvertsParen()
    {
        var result = OutputFormatter.ConvertFormulaDelimiters(
            @"前文 \(E=mc^2\) 后文 \[\int_0^1 x\,dx\]",
            OutputFormatter.FormulaDelimiter.Dollar);

        Assert.Equal("前文 $E=mc^2$ 后文 $$\\int_0^1 x\\,dx$$", result);
    }

    [Fact]
    public void Formula_Latex_KeepsOriginal()
    {
        var raw = @"前文 \(E=mc^2\) 后文 \[\int_0^1 x\,dx\]";
        Assert.Equal(raw, OutputFormatter.ConvertFormulaDelimiters(raw, OutputFormatter.FormulaDelimiter.Latex));
    }

    [Fact]
    public void Formula_Raw_KeepsOriginal()
    {
        var raw = @"前文 \(E=mc^2\) 后文";
        Assert.Equal(raw, OutputFormatter.ConvertFormulaDelimiters(raw, OutputFormatter.FormulaDelimiter.Raw));
    }

    [Fact]
    public void Apply_CombinesTableAndFormula()
    {
        var content = "<table><tr><td>a</td></tr></table>\n行内 \\(x^2\\) 公式";
        var result = OutputFormatter.Apply(content,
            OutputFormatter.TableFormat.Markdown, OutputFormatter.FormulaDelimiter.Dollar);

        Assert.Contains("| a |", result);
        Assert.Contains("$x^2$", result);
        Assert.DoesNotContain("<table>", result);
    }

    [Fact]
    public void Apply_NoTable_NoChange()
    {
        var content = "普通文本无表格无公式";
        var result = OutputFormatter.Apply(content,
            OutputFormatter.TableFormat.Markdown, OutputFormatter.FormulaDelimiter.Dollar);
        Assert.Equal(content, result);
    }

    [Fact]
    public void ContainsHtmlTable_Detection()
    {
        Assert.True(OutputFormatter.ContainsHtmlTable(HtmlTable));
        Assert.False(OutputFormatter.ContainsHtmlTable("普通 <b>加粗</b> 文本"));
    }
}
