using System.Net;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Mdhub.Rendering;

namespace Mdhub.Text;

internal static class HtmlFormatting
{
    public static TextEdit Apply(string text, int anchor, int caret, string action, bool htmlFile)
    {
        var start = Math.Clamp(Math.Min(anchor, caret), 0, text.Length);
        var end = Math.Clamp(Math.Max(anchor, caret), 0, text.Length);
        var document = !htmlFile && (text.Contains('<') || action.StartsWith("html-align-") || action == "html-div") ? MarkdownPreview.Parse(text) : null;
        var block = document?.Descendants<HtmlBlock>()
            .FirstOrDefault(b => start >= b.Span.Start && end <= b.Span.End + 1);
        if (!htmlFile && block is null && !action.StartsWith("html-")) return MarkdownFormatting.Apply(text, anchor, caret, action);
        var content = text[start..end];
        TextEdit Wrap(string open, string close, string fallback = "text")
        {
            var inner = content.Length > 0 ? content : fallback;
            return new(start, end, open + inner + close, open.Length, inner.Length);
        }
        if (action.StartsWith("html-align-") || action == "html-div")
        {
            if (block is not null) { start = block.Span.Start; end = block.Span.End + 1; }
            else if (start == end)
            {
                var paragraph = document?.Descendants<ParagraphBlock>().FirstOrDefault(p => start >= p.Span.Start && start <= p.Span.End + 1);
                if (paragraph is not null) { start = paragraph.Span.Start; end = paragraph.Span.End + 1; }
                else
                {
                    while (start > 0 && text[start - 1] is not ('\n' or '\r')) start--;
                    while (end < text.Length && text[end] is not ('\n' or '\r')) end++;
                }
            }
            content = text[start..end];
            var align = action.StartsWith("html-align-") ? action["html-align-".Length..] : "left";
            if (action.StartsWith("html-align-") && AlignContainer(content, align) is { } aligned)
                return new(start, end, aligned, 0, aligned.Length);
            var open = action == "html-div" ? "<div>" : $"<div style=\"text-align: {align};\">";
            var eol = text.Contains("\r\n") ? "\r\n" : "\n";
            var prefix = start > 0 ? eol + eol : "";
            var suffix = end < text.Length ? eol + eol : "";
            return new(start, end, prefix + open + eol + content + eol + "</div>" + suffix, prefix.Length + open.Length + eol.Length, content.Length);
        }
        if (action.StartsWith("html-color:")) return Wrap($"<span style=\"color: {WebUtility.HtmlEncode(action[11..])};\">", "</span>");
        if (action.StartsWith("html-image:"))
        {
            var parts = System.Text.Json.JsonSerializer.Deserialize<string[]>(action[11..])!;
            var image = $"<img src=\"{WebUtility.HtmlEncode(parts[0])}\" alt=\"{WebUtility.HtmlEncode(parts[1])}\" width=\"{parts[2]}\">";
            return new(start, end, image, image.Length, 0);
        }
        var tag = action switch
        {
            "bold" => "strong", "italic" => "em", "strike" => "del", "code" => "code",
            "html-underline" => "u", "html-sup" => "sup", "html-sub" => "sub",
            "h1" or "h2" or "h3" => action, "quote" => "blockquote", _ => null
        };
        if (tag is not null) return Wrap($"<{tag}>", $"</{tag}>");
        if (action == "link") return Wrap("<a href=\"https://example.com\">", "</a>", "link text");
        if (action is "list" or "ordered")
        {
            var listTag = action == "list" ? "ul" : "ol";
            var lines = (content.Length > 0 ? content : "Item").Replace("\r\n", "\n").Split('\n');
            var replacement = $"<{listTag}>\n" + string.Join("\n", lines.Select(l => $"  <li>{l}</li>")) + $"\n</{listTag}>";
            return new(start, end, replacement, 0, replacement.Length);
        }
        if (action is "table" or "html-table")
        {
            const string table = "\n\n<table>\n  <thead><tr><th>Column 1</th><th>Column 2</th></tr></thead>\n  <tbody><tr><td>Text</td><td>Text</td></tr></tbody>\n</table>\n\n";
            return new(start, end, table, table.Length, 0);
        }
        if (action == "html-break") return new(start, end, "<br>", 4, 0);
        return new(start, end, content, 0, content.Length);
    }

    // Re-align an existing container instead of nesting another div around an
    // explicit alignment, which would leave the inner alignment unchanged.
    private static string? AlignContainer(string source, string alignment)
    {
        var nodes = BasicHtml.Parse(source).Children.Where(n => n.Name != "#text" || !string.IsNullOrWhiteSpace(n.Text)).ToList();
        if (nodes.Count != 1 || !BasicHtml.IsBlock(nodes[0].Name)) return null;
        var opening = Regex.Match(source, "^\\s*<[a-zA-Z][a-zA-Z0-9]*(?:[^<>\"']|\"[^\"]*\"|'[^']*')*>");
        if (!opening.Success) return null;
        var tag = opening.Value;
        var style = Regex.Match(tag, "\\sstyle\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase);
        if (style.Success)
        {
            var declarations = WebUtility.HtmlDecode(style.Groups["value"].Value).Split(';')
                .Where(d => !string.IsNullOrWhiteSpace(d) && !d.Split(':', 2)[0].Trim().Equals("text-align", StringComparison.OrdinalIgnoreCase));
            var value = string.Join("; ", declarations.Select(d => d.Trim()).Append("text-align: " + alignment)) + ";";
            tag = tag[..style.Index] + " style=\"" + WebUtility.HtmlEncode(value) + "\"" + tag[(style.Index + style.Length)..];
        }
        else tag = tag.Insert(tag.Length - 1, $" style=\"text-align: {alignment};\"");
        return tag + source[opening.Length..];
    }
}
