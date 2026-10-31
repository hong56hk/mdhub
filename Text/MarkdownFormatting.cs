namespace Mdhub.Text;

public readonly record struct TextEdit(int Start, int End, string Replacement, int SelectionOffset, int SelectionLength);

public static class MarkdownFormatting
{
    public static TextEdit Apply(string text, int anchor, int caret, string action)
    {
        var start = Math.Clamp(Math.Min(anchor, caret), 0, text.Length);
        var end = Math.Clamp(Math.Max(anchor, caret), 0, text.Length);
        var selected = text[start..end];
        // Item 84: with a selection the buttons act on it (VS Code / Typora style); without one they keep the old behaviour.
        if (end > start && Selection(text, start, end, action) is { } onSelection) return onSelection;
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

    static TextEdit? Selection(string text, int start, int end, string action) => action switch
    {
        "bold" or "italic" or "strike" or "code" => Inline(text, start, end, action),
        "link" => Link(text, start, end),
        "list" or "ordered" or "quote" => Lines(text, start, end, action),
        _ => null,
    };

    // ---- Line-level: bullet, ordered, quote ----

    enum Marker { None, Bullet, Ordered, Quote }

    // The marker after the indentation, and the length of the marker with its following space.
    static (Marker Kind, int Length) MarkerOf(string content)
    {
        if (content.Length > 0 && content[0] is '-' or '*' or '+' && (content.Length == 1 || content[1] == ' ')) return (Marker.Bullet, Math.Min(2, content.Length));
        var digits = 0;
        while (digits < content.Length && digits < 9 && char.IsAsciiDigit(content[digits])) digits++;
        if (digits > 0 && digits < content.Length && content[digits] is '.' or ')' && (digits + 1 == content.Length || content[digits + 1] == ' '))
            return (Marker.Ordered, Math.Min(digits + 2, content.Length));
        if (content.Length > 0 && content[0] == '>') return (Marker.Quote, content.Length > 1 && content[1] == ' ' ? 2 : 1);
        return (Marker.None, 0);
    }

    static TextEdit Lines(string text, int start, int end, string action)
    {
        // Every line the selection touches; a selection ending right at a line start leaves that line out.
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') start--;
        if (end > start && text[end - 1] is '\n' or '\r')
        {
            if (text[end - 1] == '\n') end--;
            if (end > start && text[end - 1] == '\r') end--;
        }
        else while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
        var target = action switch { "list" => Marker.Bullet, "ordered" => Marker.Ordered, _ => Marker.Quote };
        var lines = Split(text[start..end]);
        bool Blank(string line) => line.Trim().Length == 0;
        (string Indent, string Content) Parts(string line) { var i = 0; while (i < line.Length && line[i] is ' ' or '\t') i++; return (line[..i], line[i..]); }
        var off = lines.Where(l => !Blank(l.Content)).All(l => MarkerOf(Parts(l.Content).Content).Kind == target);
        var sb = new System.Text.StringBuilder();
        var number = 0;
        foreach (var (content, ending) in lines)
        {
            if (Blank(content)) { sb.Append(content).Append(ending); continue; }
            var (indent, rest) = Parts(content);
            var (kind, length) = MarkerOf(rest);
            if (off) rest = rest[length..];
            else
            {
                // Bullet and ordered replace each other (and renumber); a quote marker stacks on anything else.
                if (kind == target || target != Marker.Quote && kind is Marker.Bullet or Marker.Ordered) rest = rest[length..];
                rest = (target switch { Marker.Bullet => "- ", Marker.Ordered => $"{++number}. ", _ => "> " }) + rest;
            }
            sb.Append(indent).Append(rest).Append(ending);
        }
        var replacement = sb.ToString();
        return new(start, end, replacement, 0, replacement.Length);
    }

    // Lines with their own endings (\r\n, \n or \r), so item 68's mixed endings survive.
    static List<(string Content, string Ending)> Split(string s)
    {
        var result = new List<(string, string)>();
        var i = 0;
        while (true)
        {
            var j = i;
            while (j < s.Length && s[j] != '\n' && s[j] != '\r') j++;
            if (j >= s.Length) { result.Add((s[i..], "")); return result; }
            var len = s[j] == '\r' && j + 1 < s.Length && s[j + 1] == '\n' ? 2 : 1;
            result.Add((s[i..j], s.Substring(j, len)));
            i = j + len;
        }
    }

    // ---- Inline: bold, italic, strikethrough, code ----

    static int Run(string s, int from, int step, char c) { var n = 0; for (var i = from; i >= 0 && i < s.Length && s[i] == c; i += step) n++; return n; }

    // Does a star run of this length carry the style? Italic: an odd run (*, ***); bold: two or more (**, ***).
    static bool Carries(string delimiter, int run) => delimiter switch { "*" => run % 2 == 1, "**" => run >= 2, _ => run >= delimiter.Length };

    // The inner text when core is wrapped in this style, else null.
    static string? Unwrapped(string core, string delimiter)
    {
        if (delimiter == "`")
        {
            var fence = Run(core, 0, 1, '`');
            if (fence == 0 || fence * 2 >= core.Length || Run(core, core.Length - 1, -1, '`') != fence) return null;
            var inner = core[fence..^fence];
            return inner.Length >= 2 && inner[0] == ' ' && inner[^1] == ' ' ? inner[1..^1] : inner;
        }
        var c = delimiter[0];
        int lead = Run(core, 0, 1, c), trail = Run(core, core.Length - 1, -1, c);
        if (lead * 2 > core.Length || lead != trail || !Carries(delimiter, lead)) return null;
        return core.Substring(delimiter.Length, core.Length - 2 * delimiter.Length);
    }

    static string Wrapped(string core, string delimiter)
    {
        if (delimiter != "`") return delimiter + core + delimiter;
        var longest = 0;
        for (var i = 0; i < core.Length; i++) longest = Math.Max(longest, Run(core, i, 1, '`'));
        var fence = new string('`', longest + 1);
        var pad = core.StartsWith('`') || core.EndsWith('`') ? " " : "";
        return fence + pad + core + pad + fence;
    }

    static (string Lead, string Core, string Trail) Trim(string s)
    {
        var a = 0; while (a < s.Length && char.IsWhiteSpace(s[a])) a++;
        var b = s.Length; while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
        return (s[..a], s[a..b], s[b..]);
    }

    static TextEdit Inline(string text, int start, int end, string action)
    {
        var delimiter = action switch { "bold" => "**", "italic" => "*", "strike" => "~~", _ => "`" };
        var selected = text[start..end];
        var lines = Split(selected);
        if (lines.Count == 1)
        {
            var (lead, core, trail) = Trim(selected);
            if (core.Length == 0) return new(start, end, selected, 0, selected.Length);
            if (Unwrapped(core, delimiter) is { } inside)
                return new(start, end, lead + inside + trail, lead.Length, inside.Length);
            // Wrapped right around the selection: **[text]** unwraps too.
            if (Around(text, start + lead.Length, end - trail.Length, delimiter) is { } around)
                return new(start + lead.Length - around.Before, end - trail.Length + around.After, core, 0, core.Length);
            var wrapped = Wrapped(core, delimiter);
            var inner = wrapped.IndexOf(core, StringComparison.Ordinal);
            return new(start, end, lead + wrapped + trail, lead.Length + inner, core.Length);
        }
        // Several lines: each non-blank line's content separately (indentation stays outside); all wrapped -> unwrap all.
        var cores = lines.Select(l => (Line: l, Parts: Trim(l.Content))).ToList();
        var off = cores.Where(c => c.Parts.Core.Length > 0).All(c => Unwrapped(c.Parts.Core, delimiter) is not null);
        var sb = new System.Text.StringBuilder();
        foreach (var (line, (lead, core, trail)) in cores)
        {
            sb.Append(lead);
            if (core.Length > 0) sb.Append(off ? Unwrapped(core, delimiter) : Wrapped(core, delimiter));
            sb.Append(trail).Append(line.Ending);
        }
        var replacement = sb.ToString();
        return new(start, end, replacement, 0, replacement.Length);
    }

    static (int Before, int After)? Around(string text, int start, int end, string delimiter)
    {
        if (delimiter == "`")
        {
            int before = Run(text, start - 1, -1, '`'), after = Run(text, end, 1, '`');
            return before > 0 && before == after ? (before, after) : null;
        }
        var c = delimiter[0];
        int b = Run(text, start - 1, -1, c), a = Run(text, end, 1, c);
        return b == a && Carries(delimiter, b) ? (delimiter.Length, delimiter.Length) : null;
    }

    // ---- Link ----

    static TextEdit Link(string text, int start, int end)
    {
        var selected = text[start..end];
        var (lead, core, trail) = Trim(selected);
        if (core.Length == 0) return new(start, end, selected, 0, selected.Length);
        if (System.Text.RegularExpressions.Regex.IsMatch(core, @"^(https?://|ftp://|mailto:|www\.)\S+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return new(start, end, $"{lead}[]({core}){trail}", lead.Length + 1, 0);
        return new(start, end, $"{lead}[{core}](url){trail}", lead.Length + core.Length + 3, 3);
    }
}
