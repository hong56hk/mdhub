using Mdhub.Text;

namespace Mdhub.Services;

// Item 102c: the Markdown notes file that sits beside a PDF, and the block "Add to notes" appends.
public static class PdfNotes
{
    public static string PathFor(string pdfPath)
    {
        var full = Path.GetFullPath(pdfPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        return Path.Combine(dir, ImageAssets.FolderName, Path.GetFileNameWithoutExtension(full) + ".notes.md");
    }

    public static string PageLink(string pdfPath, int page)
    {
        var name = Path.GetFileName(pdfPath).Replace("%", "%25").Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
        return $"[p. {page}](../{name}#page={page})";
    }

    public static string Quote(string selected, string lineEnding = "\n")
    {
        var text = selected.Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join(lineEnding, text.Split('\n').Select(line => "> " + line));
    }

    // A blank line, then every selected line as a Markdown quote, then the page link, ending with a newline.
    public static string ApplyAppend(string existing, string selected, string pdfPath, int page, string lineEnding = "\n")
    {
        var body = Quote(selected, lineEnding) + lineEnding + PageLink(pdfPath, page) + lineEnding;
        var prefix = existing;
        if (prefix.Length > 0 && !prefix.EndsWith('\n') && !prefix.EndsWith('\r')) prefix += lineEnding;
        return prefix + lineEnding + body;
    }

    // Item 102d: a Markdown destination like ../report.pdf#page=3 (spaces may be %20), resolved from the notes file.
    public static bool TryParsePageLink(string href, string? fromDocumentPath, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? pdfPath, out int page)
    {
        pdfPath = null;
        page = 0;
        if (string.IsNullOrWhiteSpace(href)) return false;
        href = href.Trim();
        if (href.Length >= 2 && href[0] == '<' && href[^1] == '>') href = href[1..^1].Trim();
        var hash = href.IndexOf('#');
        if (hash <= 0) return false;
        var pathPart = Decode(href[..hash]);
        var fragment = href[(hash + 1)..];
        if (!fragment.StartsWith("page=", StringComparison.OrdinalIgnoreCase)) return false;
        var digits = fragment[5..];
        var n = 0;
        while (n < digits.Length && char.IsDigit(digits[n])) n++;
        if (n == 0 || !int.TryParse(digits[..n], out page)) return false;
        if (n < digits.Length && digits[n] is not '&' and not ';') return false;
        if (!pathPart.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (fromDocumentPath is { Length: > 0 } && !Path.IsPathRooted(pathPart))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(fromDocumentPath));
                if (dir is null) return false;
                pdfPath = Path.GetFullPath(Path.Combine(dir, pathPart));
            }
            else pdfPath = Path.GetFullPath(pathPart);
        }
        catch (Exception) { return false; }
        return true;
    }

    // The destination of the [text](url) or <url> that covers this source index, or null.
    public static string? MarkdownHrefAt(string text, int index)
    {
        if (index < 0 || index > text.Length) return null;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '`')
            {
                var ticks = 1;
                while (i + ticks < text.Length && text[i + ticks] == '`') ticks++;
                var close = text.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                i = close >= 0 ? close + ticks - 1 : text.Length - 1;
                continue;
            }
            if (text[i] == '[' && LinkDestination(text, i) is { } inline)
            {
                if (index >= i && index <= inline.Close) return inline.Href;
                i = inline.Close;
                continue;
            }
            if (text[i] == '<' )
            {
                var close = text.IndexOf('>', i + 1);
                if (close > i + 1 && index >= i && index <= close)
                {
                    var inner = text[(i + 1)..close];
                    if (!inner.Contains(' ')) return inner;
                }
            }
        }
        return null;
    }

    static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace("+", "%2B")); }
        catch (Exception) { return value; }
    }

    static (string Href, int Close)? LinkDestination(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']' && --depth == 0)
            {
                if (i + 1 >= text.Length || text[i + 1] != '(') return null;
                var paren = 0;
                for (var j = i + 2; j < text.Length; j++)
                {
                    if (text[j] == '\\') { j++; continue; }
                    if (text[j] == '(') paren++;
                    else if (text[j] == ')' && paren-- == 0)
                    {
                        var raw = text[(i + 2)..j].Trim();
                        if (raw.Length >= 2 && raw[0] == '<' && raw[^1] == '>') raw = raw[1..^1].Trim();
                        var space = raw.IndexOfAny([' ', '\t']);
                        if (space >= 0) raw = raw[..space].Trim();
                        return (raw, j);
                    }
                }
                return null;
            }
        }
        return null;
    }
}