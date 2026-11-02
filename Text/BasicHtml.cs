using System.Net;
using System.Text.RegularExpressions;

namespace Mdhub.Text;

// A fragment reader for the preview's small HTML vocabulary, not a browser DOM.
// Quoted attributes may contain '>'; incomplete tags stay text while the user types.
internal static class BasicHtml
{
    internal sealed class Node(string name, string text = "")
    {
        public string Name { get; } = name;
        public string Text { get; } = text;
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Node> Children { get; } = [];
        public string? Attr(string name) => Attributes.GetValueOrDefault(name);
    }

    private static readonly Regex Tokens = new("<!--.*?(?:-->|$)|<![^>]*>|</?[a-zA-Z][a-zA-Z0-9:-]*(?:[^<>\"']|\"[^\"]*\"|'[^']*')*>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagName = new(@"^</?([\w:-]+)", RegexOptions.Compiled);
    private static readonly Regex Attributes = new("([a-zA-Z_:][a-zA-Z0-9_:.-]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.Compiled);
    internal static bool IsBlock(string name) => name is "div" or "p" or "table" or "thead" or "tbody" or "tfoot" or "tr" or "th" or "td" or "caption" or "ol" or "ul" or "li" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "hr" or "html" or "body";
    internal static bool IsInline(string name) => name is "span" or "strong" or "b" or "em" or "i" or "u" or "s" or "del" or "sup" or "sub" or "code" or "a" or "br" or "img";

    public static Node Parse(string source)
    {
        var root = new Node("");
        var stack = new List<Node> { root };
        var offset = 0;
        void Literal(string text) { if (text.Length > 0) stack[^1].Children.Add(new Node("#text", WebUtility.HtmlDecode(text))); }
        foreach (Match token in Tokens.Matches(source))
        {
            Literal(source[offset..token.Index]);
            offset = token.Index + token.Length;
            var raw = token.Value;
            if (raw.StartsWith("<!")) continue;
            var match = TagName.Match(raw);
            var name = match.Groups[1].Value.ToLowerInvariant();
            if (!IsBlock(name) && !IsInline(name)) { Literal(raw); continue; }
            if (raw.StartsWith("</"))
            {
                var index = stack.FindLastIndex(n => n.Name == name);
                if (index > 0) stack.RemoveRange(index, stack.Count - index);
                else Literal(raw);
                continue;
            }
            // Common optional end tags, bounded by their containing list/table.
            if (name is "li" or "tr" or "td" or "th" or "p")
            {
                for (var i = stack.Count - 1; i > 0; i--)
                {
                    if (stack[i].Name == name || name is "td" or "th" && stack[i].Name is "td" or "th")
                    { stack.RemoveRange(i, stack.Count - i); break; }
                    if (stack[i].Name is "ul" or "ol" or "table" || name is "td" or "th" && stack[i].Name == "tr") break;
                }
            }
            var node = new Node(name);
            foreach (Match attr in Attributes.Matches(raw[match.Length..]))
                node.Attributes[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value.TrimEnd('/'));
            stack[^1].Children.Add(node);
            // Bound nesting to keep malformed/deep documents from overflowing recursive rendering.
            if (name is not ("br" or "img" or "hr") && !raw.EndsWith("/>") && stack.Count < 128) stack.Add(node);
        }
        Literal(source[offset..]);
        return root;
    }
}
