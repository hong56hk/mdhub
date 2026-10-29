namespace Mdhub.Text;

public readonly record struct TextEdit(int Start, int End, string Replacement, int SelectionOffset, int SelectionLength);

public static class MarkdownFormatting
{
    public static TextEdit Apply(string text, int anchor, int caret, string action)
    {
        var start = Math.Clamp(Math.Min(anchor, caret), 0, text.Length);
        var end = Math.Clamp(Math.Max(anchor, caret), 0, text.Length);
        var selected = text[start..end];
        var delimiter = action switch { "bold" => "**", "italic" => "*", "strike" => "~~", "code" => "`", _ => null };
        if (delimiter is not null)
        {
            var content = selected.Length == 0 ? "text" : selected;
            return new(start, end, delimiter + content + delimiter, delimiter.Length, content.Length);
        }
        if (action == "link")
        {
            var content = selected.Length == 0 ? "link text" : selected;
            return new(start, end, $"[{content}](https://example.com)", content.Length + 3, "https://example.com".Length);
        }
        if (action == "table")
        {
            const string table = "\n\n| Column 1 | Column 2 |\n| --- | --- |\n| Text | Text |\n\n";
            return new(start, end, table, table.Length, 0);
        }
        var prefix = action switch { "h1" => "# ", "h2" => "## ", "h3" => "### ", "list" => "- ", "ordered" => "1. ", "quote" => "> ", _ => "" };
        if (prefix.Length == 0) return new(start, end, selected, 0, selected.Length);
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') start--;
        // A selection ending at the next line's start does not include that next line.
        if (end > start && end > 0 && text[end - 1] == '\n') end--;
        else while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
        var replacement = prefix + text[start..end].Replace("\n", "\n" + prefix);
        return new(start, end, replacement, 0, replacement.Length);
    }
}
