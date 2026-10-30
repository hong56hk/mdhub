using System.Globalization;
using System.Net;
using System.Text;

namespace Mdhub.Rendering;

// The rendered (not Markdown) content of a preview selection, as a small tree that becomes HTML, plain text and RTF.
// Block kinds: doc, h1..h6, p, pre, blockquote, ul, ol, li, table, tr, th, td, hr, img.
// Inline kinds: text, strong, em, del, code, a, br, img.
public sealed class CopyNode(string kind)
{
    public string Kind { get; } = kind;
    public string? Text { get; init; }
    public string? Href { get; init; }
    public int Start { get; init; } = 1; // ordered list start
    public int Row { get; init; } = -1; // table cell row, used while grouping cells into rows
    public List<CopyNode> Children { get; } = [];
    public bool IsInline => Kind is "text" or "strong" or "em" or "del" or "code" or "a" or "br" || Kind == "img" && Href is null;
}

// Colours written into copied HTML / RTF (from the Light palette: pasted content usually lands on a light page).
public sealed record RichCopyStyle(string Link, string CodeBackground, string InlineCodeBackground, string Border, string HeaderBackground, string Quote);

public static class RichCopy
{
    private const string Mono = "Consolas,Menlo,monospace";

    // ---- HTML with inline styles ----
    public static string Html(CopyNode doc, RichCopyStyle style)
    {
        var sb = new StringBuilder();
        foreach (var child in doc.Children) HtmlBlock(child, sb, style);
        return sb.ToString();
    }

    private static string Heading(int level) => level switch { 1 => "28px", 2 => "23px", 3 => "19px", _ => "16px" };

    private static void HtmlBlock(CopyNode n, StringBuilder sb, RichCopyStyle s)
    {
        switch (n.Kind)
        {
            case ['h', var d] when char.IsDigit(d):
                sb.Append($"<{n.Kind} style=\"font-size:{Heading(d - '0')};font-weight:600;margin:10px 0 4px\">"); HtmlInlines(n, sb, s); sb.Append($"</{n.Kind}>"); break;
            case "p": sb.Append("<p style=\"margin:0 0 8px\">"); HtmlInlines(n, sb, s); sb.Append("</p>"); break;
            case "pre":
                sb.Append($"<pre style=\"font-family:{Mono};font-size:13px;background:{s.CodeBackground};padding:10px;border-radius:5px;white-space:pre-wrap\"><code>");
                HtmlInlines(n, sb, s); sb.Append("</code></pre>"); break;
            case "blockquote":
                sb.Append($"<blockquote style=\"margin:0 0 8px;padding:0 0 0 12px;border-left:3px solid {s.Quote}\">");
                foreach (var c in n.Children) HtmlBlock(c, sb, s); sb.Append("</blockquote>"); break;
            case "ul" or "ol":
                sb.Append($"<{n.Kind}{(n.Kind == "ol" && n.Start != 1 ? $" start=\"{n.Start}\"" : "")} style=\"margin:0 0 8px;padding-left:24px\">");
                foreach (var c in n.Children) HtmlBlock(c, sb, s); sb.Append($"</{n.Kind}>"); break;
            case "li": sb.Append("<li style=\"margin:2px 0\">"); HtmlCell(n, sb, s); sb.Append("</li>"); break;
            case "table":
                sb.Append("<table style=\"border-collapse:collapse;margin:0 0 8px\">");
                foreach (var c in n.Children) HtmlBlock(c, sb, s); sb.Append("</table>"); break;
            case "tr": sb.Append("<tr>"); foreach (var c in n.Children) HtmlBlock(c, sb, s); sb.Append("</tr>"); break;
            case "th":
                sb.Append($"<th style=\"border:1px solid {s.Border};padding:6px 10px;text-align:left;font-weight:bold;background:{s.HeaderBackground}\">"); HtmlCell(n, sb, s); sb.Append("</th>"); break;
            case "td": sb.Append($"<td style=\"border:1px solid {s.Border};padding:6px 10px;text-align:left\">"); HtmlCell(n, sb, s); sb.Append("</td>"); break;
            case "hr": sb.Append($"<hr style=\"border:none;border-top:1px solid {s.Border}\">"); break;
            case "img": sb.Append($"<img src=\"{Attr(n.Text)}\">"); break;
        }
    }

    // List items and table cells: single paragraphs render inline (no paragraph margins inside a cell).
    private static void HtmlCell(CopyNode n, StringBuilder sb, RichCopyStyle s)
    {
        for (var i = 0; i < n.Children.Count; i++)
        {
            var c = n.Children[i];
            if (c.Kind == "p") { if (i > 0 && n.Children[i - 1].Kind == "p") sb.Append("<br>"); HtmlInlines(c, sb, s); }
            else HtmlBlock(c, sb, s);
        }
    }

    private static void HtmlInlines(CopyNode n, StringBuilder sb, RichCopyStyle s)
    {
        foreach (var c in n.Children)
        {
            switch (c.Kind)
            {
                case "text": sb.Append(WebUtility.HtmlEncode(c.Text)); break;
                case "br": sb.Append(n.Kind == "pre" ? "\n" : "<br>"); break;
                case "strong": sb.Append("<b style=\"font-weight:bold\">"); HtmlInlines(c, sb, s); sb.Append("</b>"); break;
                case "em": sb.Append("<i style=\"font-style:italic\">"); HtmlInlines(c, sb, s); sb.Append("</i>"); break;
                case "del": sb.Append("<s style=\"text-decoration:line-through\">"); HtmlInlines(c, sb, s); sb.Append("</s>"); break;
                case "code": sb.Append($"<code style=\"font-family:{Mono};background:{s.InlineCodeBackground};padding:0 3px;border-radius:3px\">"); HtmlInlines(c, sb, s); sb.Append("</code>"); break;
                case "a": sb.Append($"<a href=\"{Attr(c.Href)}\" style=\"color:{s.Link};text-decoration:underline\">"); HtmlInlines(c, sb, s); sb.Append("</a>"); break;
                case "img": sb.Append($"<img src=\"{Attr(c.Text)}\">"); break;
            }
        }
    }

    private static string Attr(string? value) => WebUtility.HtmlEncode(value ?? "");

    // ---- CF_HTML (Windows "HTML Format"): a header with byte offsets into the UTF-8 payload ----
    public static string CfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<html><head><meta charset=\"utf-8\"></head><body>\r\n<!--StartFragment-->";
        const string suffix = "<!--EndFragment-->\r\n</body></html>";
        var headerLength = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(CultureInfo.InvariantCulture, header, headerLength, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
    }

    // ---- Plain text: no Markdown markers ----
    public static string Plain(CopyNode doc) => string.Join("\n\n", doc.Children.Select(c => PlainBlock(c, 0)).Where(t => t.Length > 0));

    private static string PlainBlock(CopyNode n, int depth) => n.Kind switch
    {
        "ul" or "ol" => string.Join("\n", n.Children.Select((li, i) =>
            new string(' ', depth * 2) + (n.Kind == "ol" ? $"{n.Start + i}. " : "\u2022 ") + PlainCell(li, depth + 1))),
        "table" => string.Join("\n", n.Children.Select(tr => string.Join("\t", tr.Children.Select(c => PlainCell(c, depth))))),
        "blockquote" => string.Join("\n\n", n.Children.Select(c => PlainBlock(c, depth)).Where(t => t.Length > 0)),
        "hr" or "img" => "",
        _ => PlainInlines(n),
    };

    private static string PlainCell(CopyNode n, int depth)
    {
        var sb = new StringBuilder();
        foreach (var c in n.Children)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(c.Kind == "p" ? PlainInlines(c) : PlainBlock(c, depth));
        }
        return sb.ToString();
    }

    private static string PlainInlines(CopyNode n)
    {
        var sb = new StringBuilder();
        void Walk(CopyNode x) { foreach (var c in x.Children) { if (c.Kind == "text") sb.Append(c.Text); else if (c.Kind == "br") sb.Append('\n'); else Walk(c); } }
        Walk(n);
        return sb.ToString();
    }

    // ---- RTF ----
    public static string Rtf(CopyNode doc, RichCopyStyle style)
    {
        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss Segoe UI;}{\\f1\\fmodern Consolas;}}");
        sb.Append("{\\colortbl;").Append(RtfColor(style.Link)).Append(RtfColor(style.CodeBackground)).Append(RtfColor(style.InlineCodeBackground)).Append('}');
        sb.Append("\\f0\\fs22 ");
        foreach (var c in doc.Children) RtfBlock(c, sb, 0);
        sb.Append('}');
        return sb.ToString();
    }

    private static string RtfColor(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..]; // #AARRGGBB
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) v = 0;
        return $"\\red{(v >> 16) & 255}\\green{(v >> 8) & 255}\\blue{v & 255};";
    }

    private static void RtfBlock(CopyNode n, StringBuilder sb, int depth)
    {
        var indent = depth * 360;
        switch (n.Kind)
        {
            case ['h', var d] when char.IsDigit(d):
                var size = (d - '0') switch { 1 => 56, 2 => 46, 3 => 38, _ => 32 };
                sb.Append($"\\pard\\sb200\\sa80\\li{indent}{{\\b\\fs{size} "); RtfInlines(n, sb); sb.Append("}\\par\n"); break;
            case "p": sb.Append($"\\pard\\sa120\\li{indent} "); RtfInlines(n, sb); sb.Append("\\par\n"); break;
            case "pre": sb.Append($"\\pard\\sa120\\li{indent}{{\\f1\\fs20\\cbpat2 "); RtfInlines(n, sb); sb.Append("}\\par\n"); break;
            case "blockquote": foreach (var c in n.Children) RtfBlock(c, sb, depth + 1); break;
            case "ul" or "ol":
                for (var i = 0; i < n.Children.Count; i++)
                {
                    var li = n.Children[i];
                    var marker = n.Kind == "ol" ? $"{n.Start + i}." : "\\bullet";
                    var first = true;
                    foreach (var c in li.Children)
                    {
                        if (c.Kind == "p")
                        {
                            sb.Append($"\\pard\\sa60\\li{indent + 360}\\fi-360 ").Append(first ? marker : "").Append("\\tab ");
                            RtfInlines(c, sb); sb.Append("\\par\n");
                        }
                        else RtfBlock(c, sb, depth + 1);
                        first = false;
                    }
                }
                break;
            case "table":
                foreach (var tr in n.Children)
                {
                    sb.Append("\\trowd\\trgaph108");
                    for (var i = 0; i < tr.Children.Count; i++) sb.Append($"\\clbrdrt\\brdrs\\clbrdrl\\brdrs\\clbrdrb\\brdrs\\clbrdrr\\brdrs\\cellx{(i + 1) * 2400}");
                    sb.Append('\n');
                    foreach (var cell in tr.Children)
                    {
                        sb.Append("\\pard\\intbl ").Append(cell.Kind == "th" ? "{\\b " : "{");
                        for (var j = 0; j < cell.Children.Count; j++) { if (j > 0) sb.Append("\\line "); RtfInlines(cell.Children[j], sb); }
                        sb.Append("}\\cell\n");
                    }
                    sb.Append("\\row\n");
                }
                sb.Append("\\pard\\par\n");
                break;
            case "hr": sb.Append("\\pard\\brdrb\\brdrs\\brdrw10\\brsp20 \\par\n"); break;
        }
    }

    private static void RtfInlines(CopyNode n, StringBuilder sb)
    {
        foreach (var c in n.Children)
        {
            switch (c.Kind)
            {
                case "text": RtfText(c.Text, sb); break;
                case "br": sb.Append("\\line "); break;
                case "strong": sb.Append("{\\b "); RtfInlines(c, sb); sb.Append('}'); break;
                case "em": sb.Append("{\\i "); RtfInlines(c, sb); sb.Append('}'); break;
                case "del": sb.Append("{\\strike "); RtfInlines(c, sb); sb.Append('}'); break;
                case "code": sb.Append("{\\f1\\highlight3 "); RtfInlines(c, sb); sb.Append('}'); break;
                case "a":
                    sb.Append("{\\field{\\*\\fldinst{HYPERLINK \""); RtfText(c.Href?.Replace("\"", "%22"), sb); sb.Append("\"}}{\\fldrslt{\\ul\\cf1 ");
                    RtfInlines(c, sb); sb.Append("}}}"); break;
                default: RtfInlines(c, sb); break;
            }
        }
    }

    private static void RtfText(string? text, StringBuilder sb)
    {
        foreach (var ch in text ?? "")
        {
            if (ch is '\\' or '{' or '}') sb.Append('\\').Append(ch);
            else if (ch == '\t') sb.Append("\\tab ");
            else if (ch == '\n') sb.Append("\\line ");
            else if (ch == '\r') continue;
            else if (ch < 0x80) sb.Append(ch);
            else sb.Append("\\u").Append((short)ch).Append('?');
        }
    }
}