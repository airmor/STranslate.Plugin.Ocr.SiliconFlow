using System.Text;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Ocr.SiliconFlow;

/// <summary>
/// 输出格式化：把模型原始输出中的特殊结构转换为用户选择的格式。
/// 当前处理：
///   1. HTML 表格（&lt;table&gt;…&lt;/table&gt;，DeepSeek/Paddle 实测会输出）→ Markdown / LaTeX / TSV
///   2. 公式定界符：\( \) \[ \]（DeepSeek/Paddle 原生） ↔ $ $ $$ $$
/// </summary>
public static partial class OutputFormatter
{
    /// <summary>表格输出格式</summary>
    public enum TableFormat
    {
        /// <summary>Markdown 管道表格（默认，宿主可渲染）</summary>
        Markdown,

        /// <summary>LaTeX tabular 环境</summary>
        Latex,

        /// <summary>制表符分隔（粘贴到 Word/Excel 自动成表）</summary>
        Tsv
    }

    /// <summary>公式定界符格式</summary>
    public enum FormulaDelimiter
    {
        /// <summary>$ …$ 行内 / $$ …$$ 块级（宿主 Markdown 渲染常用）</summary>
        Dollar,

        /// <summary>\( …\) 行内 / \[…\] 块级（模型原生，保持原样）</summary>
        Latex,

        /// <summary>不转换（模型输出什么就什么）</summary>
        Raw
    }

    /// <summary>
    /// 对模型输出应用全部转换（表格 + 公式定界符）。
    /// 各适配器在 ParseResponse 拆行前调用，作用于整段文本。
    /// </summary>
    public static string Apply(string content, TableFormat tableFormat, FormulaDelimiter formulaDelimiter)
    {
        var result = content;

        if (tableFormat != TableFormat.Latex || ContainsHtmlTable(result))
            result = ConvertTables(result, tableFormat);

        result = ConvertFormulaDelimiters(result, formulaDelimiter);
        return result;
    }

    /// <summary>是否包含 HTML 表格（决定是否需要转换）</summary>
    public static bool ContainsHtmlTable(string content) => HtmlTableRegex().IsMatch(content);

    // ───────────────────────── 表格转换 ─────────────────────────

    [GeneratedRegex(@"<table[^>]*>(.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlTableRegex();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableCellRegex();

    /// <summary>把所有 &lt;table&gt; 块转换为目标格式；Latex+已是表格格式时按 LaTeX 重排（统一外观）</summary>
    public static string ConvertTables(string content, TableFormat format)
    {
        return HtmlTableRegex().Replace(content, match =>
        {
            var rows = ParseTableRows(match.Groups[1].Value);
            return format switch
            {
                TableFormat.Latex => ToLatexTable(rows),
                TableFormat.Tsv => ToTsvTable(rows),
                _ => ToMarkdownTable(rows)
            };
        });
    }

    private static List<List<string>> ParseTableRows(string tableInner)
    {
        var rows = new List<List<string>>();
        foreach (var rowMatch in TableRowRegex().Matches(tableInner).Cast<Match>())
        {
            var cells = TableCellRegex().Matches(rowMatch.Groups[1].Value)
                .Cast<Match>()
                .Select(c => UnescapeHtml(c.Groups[1].Value).Trim())
                .ToList();
            if (cells.Count > 0)
                rows.Add(cells);
        }
        return rows;
    }

    private static string ToMarkdownTable(List<List<string>> rows)
    {
        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        var width = rows.Max(r => r.Count);

        AppendRow(sb, rows[0], width);
        sb.Append('|');
        for (var i = 0; i < width; i++)
            sb.Append(" --- |");
        sb.Append('\n');

        for (var i = 1; i < rows.Count; i++)
            AppendRow(sb, rows[i], width);

        // 最后的换行交给外层拆行逻辑
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendRow(StringBuilder sb, List<string> cells, int width)
    {
        sb.Append('|');
        for (var i = 0; i < width; i++)
        {
            var cell = i < cells.Count ? cells[i].Replace("|", "\\|") : string.Empty;
            sb.Append(' ').Append(cell).Append(" |");
        }
        sb.Append('\n');
    }

    private static string ToLatexTable(List<List<string>> rows)
    {
        if (rows.Count == 0) return string.Empty;

        var width = rows.Max(r => r.Count);
        var sb = new StringBuilder();
        sb.Append("$$\n");
        sb.Append(@"\begin{tabular}{|" + new string('c', width) + "|}\n");
        sb.Append(@"\hline" + '\n');
        foreach (var row in rows)
        {
            var cells = Enumerable.Range(0, width)
                .Select(i => i < row.Count ? LatexEscape(row[i]) : string.Empty);
            sb.Append(string.Join(" & ", cells) + @" \\" + '\n');
            sb.Append(@"\hline" + '\n');
        }
        sb.Append(@"\end{tabular}" + '\n');
        sb.Append("$$");
        return sb.ToString();
    }

    private static string LatexEscape(string cell) =>
        cell.Replace("&", "\\&").Replace("%", "\\%").Replace("#", "\\#")
            .Replace("_", "\\_").Replace("{", "\\{").Replace("}", "\\}");

    private static string ToTsvTable(List<List<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.Append(string.Join("\t", row) + '\n');
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string UnescapeHtml(string html) =>
        html.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'");

    // ───────────────────────── 公式定界符 ─────────────────────────

    /// <summary>\( …\) / \[…\] ↔ $ …$ / $$ …$$</summary>
    public static string ConvertFormulaDelimiters(string content, FormulaDelimiter format)
    {
        return format switch
        {
            // \( x \) → $ x $（行内）
            FormulaDelimiter.Dollar => InlineLatexRegex().Replace(content, m => "$" + m.Groups[1].Value.Trim() + "$")
                // \[ x \] → $$ x $$（块级）
                .Let(it => BlockLatexRegex().Replace(it, m => "$$" + m.Groups[1].Value.Trim() + "$$")),
            FormulaDelimiter.Latex => content, // 保持模型原生的 \( \) \[ \]
            _ => content // Raw：不动
        };
    }

    [GeneratedRegex(@"\\\( ?(.*?) ?\\\)", RegexOptions.Singleline)]
    private static partial Regex InlineLatexRegex();

    [GeneratedRegex(@"\\\[ ?(.*?) ?\\\]", RegexOptions.Singleline)]
    private static partial Regex BlockLatexRegex();
}

/// <summary>轻量函数式扩展（局部使用）</summary>
internal static class StringExt
{
    public static TResult Let<T, TResult>(this T self, Func<T, TResult> block) => block(self);
}
