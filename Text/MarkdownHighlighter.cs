using System.Text.RegularExpressions;

namespace Mdhub.Text;

// Item 33: source-editor syntax highlighting. Every colour is a Theme.axaml resource named "Syntax" + the enum name.
public enum SyntaxColor : byte
{
    Default, H1, H2, H3, H4, Marker, InlineCode, Link, Url, ListMarker, Quote, Checkbox, Checked, FrontMatter, Rule,
    CodeText, CodeKeyword, CodeString, CodeComment, CodeNumber, CodeType, CodeProperty, CodeTag, CodeAttribute,
    Bold, Italic, Strike // item 44: emphasis text colours (only where no heading/link/forced colour applies)
}

[Flags] public enum SyntaxFont : byte { None = 0, Bold = 1, Italic = 2, Strike = 4 }

public enum SyntaxBlockKind { Code, Quote }

/// <summary>One styled stretch of the source: foreground, optional inline background, font flags.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxColor Color, SyntaxFont Font = SyntaxFont.None, bool CodeBackground = false)
{
    public int End => Start + Length;
}

/// <summary>A run of whole lines drawn with a block decoration (fenced code background, quote bar).</summary>
public readonly record struct SyntaxBlock(int Start, int End, SyntaxBlockKind Kind);

public sealed record SyntaxResult(IReadOnlyList<SyntaxSpan> Spans, IReadOnlyList<SyntaxBlock> Blocks);

/// <summary>
/// Line-based Markdown tokenizer. Each line is tokenized from (line text, state at its start); results are
/// memoized on that pair, so an edit only re-tokenizes the changed lines (plus any whose state changed, e.g.
/// after opening a fence). Inline constructs are line-local.
/// </summary>
public sealed class MarkdownHighlighter
{
    // Block state carried from one line to the next.
    internal readonly record struct LineState(byte Mode, string? Lang, char FenceChar, int FenceLength, bool CodeComment, bool InTable, int Index0)
    {
        public const byte Normal = 0, Fence = 1, FrontMatter = 2;
    }

    private sealed record LineResult(SyntaxSpan[] Spans, LineState Next, SyntaxBlockKind? Block);

    private readonly Dictionary<(string Line, LineState State, bool NextIsDelimiter), LineResult> _cache = [];
    public int TokenizedLines { get; private set; } // lines actually tokenized (cache misses), for tests

    public static SyntaxResult Highlight(string text) => new MarkdownHighlighter().Run(text);

    public SyntaxResult Run(string text)
    {
        if (_cache.Count > 50_000) _cache.Clear();
        var spans = new List<SyntaxSpan>();
        var blocks = new List<SyntaxBlock>();
        var state = new LineState(LineState.Normal, null, '\0', 0, false, false, 1);
        var lineStart = 0;
        var first = true;
        while (lineStart <= text.Length)
        {
            var nl = text.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? text.Length : nl;
            var contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = text.Substring(lineStart, contentEnd - lineStart);
            var nextIsDelimiter = false;
            if (nl >= 0 && line.Contains('|'))
            {
                var nextEnd = text.IndexOf('\n', nl + 1);
                var next = text.AsSpan(nl + 1, (nextEnd < 0 ? text.Length : nextEnd) - nl - 1).TrimEnd('\r');
                nextIsDelimiter = IsTableDelimiter(next);
            }
            var key = (line, state with { Index0 = first ? 1 : 0 }, nextIsDelimiter);
            if (!_cache.TryGetValue(key, out var result))
            {
                TokenizedLines++;
                result = TokenizeLine(line, key.Item2, nextIsDelimiter);
                Array.Sort(result.Spans, (a, b) => a.Start.CompareTo(b.Start));
                _cache[key] = result;
            }
            foreach (var s in result.Spans) spans.Add(s with { Start = s.Start + lineStart });
            if (result.Block is { } kind)
            {
                if (blocks.Count > 0 && blocks[^1].Kind == kind && lineStart - blocks[^1].End <= 2)
                    blocks[^1] = blocks[^1] with { End = contentEnd };
                else blocks.Add(new SyntaxBlock(lineStart, contentEnd, kind));
            }
            state = result.Next with { Index0 = 0 };
            first = false;
            if (nl < 0) break;
            lineStart = nl + 1;
        }
        return new SyntaxResult(spans, blocks);
    }

    // ---------------------------------------------------------------- block level

    private static readonly Regex FenceOpen = new(@"^ {0,3}(`{3,}|~{3,})\s*([^\s`]*)", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^( {0,3})(#{1,6})(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^(\s*)([-*+]|\d{1,9}[.)])(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex Task = new(@"\G\s+(\[[ xX]\])(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex Delimiter = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?\s*$|^\s*\|\s*:?-+:?\s*\|\s*$", RegexOptions.Compiled);

    internal static bool IsTableDelimiter(ReadOnlySpan<char> line) => line.Contains('-') && Delimiter.IsMatch(line.ToString());

    private static LineResult TokenizeLine(string line, LineState state, bool nextIsDelimiter)
    {
        var o = new List<SyntaxSpan>();
        var next = state;
        SyntaxBlockKind? block = null;

        if (state.Mode == LineState.FrontMatter)
        {
            if (line.Length > 0) o.Add(new(0, line.Length, SyntaxColor.FrontMatter));
            if (line.TrimEnd() is "---" or "...") next = next with { Mode = LineState.Normal };
            return new(o.ToArray(), next, null);
        }
        if (state.Mode == LineState.Fence)
        {
            var t = line.TrimStart(' ');
            var indent = line.Length - t.Length;
            var run = 0; while (run < t.Length && t[run] == state.FenceChar) run++;
            if (indent <= 3 && run >= state.FenceLength && t[run..].Trim().Length == 0)
            {
                if (line.Length > 0) o.Add(new(0, line.Length, SyntaxColor.Marker));
                return new(o.ToArray(), new LineState(LineState.Normal, null, '\0', 0, false, false, 0), SyntaxBlockKind.Code);
            }
            var inComment = CodeHighlighter.Line(line, state.Lang, state.CodeComment, o);
            return new(o.ToArray(), next with { CodeComment = inComment }, SyntaxBlockKind.Code);
        }
        if (state.Index0 == 1 && line.TrimEnd() == "---")
        {
            o.Add(new(0, line.Length, SyntaxColor.FrontMatter));
            return new(o.ToArray(), next with { Mode = LineState.FrontMatter }, null);
        }
        if (line.Trim().Length == 0) return new([], next with { InTable = false }, null);

        if (FenceOpen.Match(line) is { Success: true } fence && !(fence.Groups[1].Value[0] == '`' && line[(fence.Groups[1].Index + fence.Groups[1].Length)..].Contains('`')))
        {
            o.Add(new(0, line.Length, SyntaxColor.Marker));
            var lang = fence.Groups[2].Value;
            return new(o.ToArray(), new LineState(LineState.Fence, lang.Length > 0 ? lang.ToLowerInvariant() : null, fence.Groups[1].Value[0], fence.Groups[1].Length, false, false, 0), SyntaxBlockKind.Code);
        }
        if (Heading.Match(line) is { Success: true } h)
        {
            var level = h.Groups[2].Length;
            var color = level switch { 1 => SyntaxColor.H1, 2 => SyntaxColor.H2, 3 => SyntaxColor.H3, _ => SyntaxColor.H4 };
            o.Add(new(h.Groups[2].Index, level, SyntaxColor.Marker, SyntaxFont.Bold));
            var rest = h.Groups[2].Index + level;
            // Optional closing #s.
            var end = line.TrimEnd().Length;
            var close = end; while (close > rest && line[close - 1] == '#') close--;
            if (close < end && close > rest && line[close - 1] is ' ' or '\t') { o.Add(new(close, end - close, SyntaxColor.Marker, SyntaxFont.Bold)); end = close; }
            else end = line.Length;
            Inline(line, rest, end, color, SyntaxFont.Bold, o);
            return new(o.ToArray(), next with { InTable = false }, null);
        }
        if (Rule.IsMatch(line))
        {
            o.Add(new(0, line.Length, SyntaxColor.Rule));
            return new(o.ToArray(), next with { InTable = false }, null);
        }

        // Block quotes: '>' markers dimmed, content in the quote colour; the line gets the quote bar.
        var pos = 0;
        var baseColor = SyntaxColor.Default;
        while (true)
        {
            var p = pos; var spaces = 0;
            while (p < line.Length && line[p] == ' ' && spaces < 3) { p++; spaces++; }
            if (p < line.Length && line[p] == '>')
            {
                o.Add(new(p, 1, SyntaxColor.Marker));
                pos = p + 1;
                if (pos < line.Length && line[pos] == ' ') pos++;
                baseColor = SyntaxColor.Quote;
                block = SyntaxBlockKind.Quote;
            }
            else break;
        }
        var body = line[pos..];
        if (pos > 0 && Heading.Match(body) is { Success: true } qh)
        {
            o.Add(new(pos + qh.Groups[2].Index, qh.Groups[2].Length, SyntaxColor.Marker, SyntaxFont.Bold));
            var lvl = qh.Groups[2].Length;
            Inline(line, pos + qh.Groups[2].Index + lvl, line.Length, lvl switch { 1 => SyntaxColor.H1, 2 => SyntaxColor.H2, 3 => SyntaxColor.H3, _ => SyntaxColor.H4 }, SyntaxFont.Bold, o);
            return new(o.ToArray(), next, block);
        }

        if (ListItem.Match(body) is { Success: true } li)
        {
            var m = li.Groups[2];
            o.Add(new(pos + m.Index, m.Length, SyntaxColor.ListMarker));
            var after = pos + m.Index + m.Length;
            if (Task.Match(line, after) is { Success: true } task && task.Index == after)
            {
                var box = task.Groups[1];
                o.Add(new(box.Index, box.Length, SyntaxColor.Checkbox));
                if (box.Value[1] is 'x' or 'X')
                {
                    Inline(line, box.Index + box.Length, line.Length, SyntaxColor.Checked, SyntaxFont.Strike, o, forceColor: true);
                    return new(o.ToArray(), next with { InTable = false }, block);
                }
                after = box.Index + box.Length;
            }
            Inline(line, after, line.Length, baseColor, SyntaxFont.None, o);
            return new(o.ToArray(), next with { InTable = false }, block);
        }

        // Tables: header row (followed by a delimiter row), the delimiter row, and the rows after it.
        var isDelimiter = (state.InTable || line.Contains('|')) && IsTableDelimiter(line);
        if (isDelimiter && (state.InTable || line.Contains('|')))
        {
            o.Add(new(0, line.Length, SyntaxColor.Marker));
            return new(o.ToArray(), next with { InTable = true }, block);
        }
        if ((state.InTable || nextIsDelimiter) && line.Contains('|'))
        {
            var cellStart = pos;
            for (var i = pos; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] == '\\') { i++; continue; }
                if (i == line.Length || line[i] == '|')
                {
                    Inline(line, cellStart, i, baseColor, SyntaxFont.None, o);
                    if (i < line.Length) o.Add(new(i, 1, SyntaxColor.Marker));
                    cellStart = i + 1;
                }
            }
            return new(o.ToArray(), next, block);
        }

        Inline(line, pos, line.Length, baseColor, SyntaxFont.None, o);
        return new(o.ToArray(), next with { InTable = false }, block);
    }

    // ---------------------------------------------------------------- inline level

    /// <summary>Tokenizes line[from..to) with a base colour and font; emits spans for every char that is not plain default.</summary>
    // Item 44: emphasis gets its own colour unless an enclosing heading/link/checked colour is in force.
    // Nested emphasis: strike wins over bold, bold over italic.
    private static SyntaxColor Emphasis(SyntaxColor color, SyntaxFont f, bool forceColor)
    {
        if (forceColor || color is not (SyntaxColor.Default or SyntaxColor.Bold or SyntaxColor.Italic or SyntaxColor.Strike)) return color;
        return (f & SyntaxFont.Strike) != 0 ? SyntaxColor.Strike : (f & SyntaxFont.Bold) != 0 ? SyntaxColor.Bold : SyntaxColor.Italic;
    }

    private static void Inline(string s, int from, int to, SyntaxColor color, SyntaxFont font, List<SyntaxSpan> o, bool forceColor = false)
    {
        var plainStart = from;
        void Flush(int upTo) { if (upTo > plainStart && (color != SyntaxColor.Default || font != SyntaxFont.None)) o.Add(new(plainStart, upTo - plainStart, color, font)); }
        SyntaxColor C(SyntaxColor own) => forceColor ? color : own;
        var i = from;
        while (i < to)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < to && char.IsAsciiLetterOrDigit(s[i + 1]) == false && !char.IsWhiteSpace(s[i + 1]))
            {
                Flush(i); o.Add(new(i, 1, C(SyntaxColor.Marker), font)); plainStart = i + 1; i += 2; continue;
            }
            if (c == '`')
            {
                var n = Run(s, i, to, '`');
                var close = FindRun(s, i + n, to, '`', n);
                if (close >= 0)
                {
                    Flush(i);
                    o.Add(new(i, n, C(SyntaxColor.Marker), font));
                    if (close > i + n) o.Add(new(i + n, close - i - n, C(SyntaxColor.InlineCode), font & SyntaxFont.Strike, CodeBackground: !forceColor));
                    o.Add(new(close, n, C(SyntaxColor.Marker), font));
                    i = plainStart = close + n; continue;
                }
                i += n; continue;
            }
            if (c == '[' || (c == '!' && i + 1 < to && s[i + 1] == '['))
            {
                var open = c == '!' ? i + 1 : i;
                if (LinkAt(s, open, to) is { } link)
                {
                    Flush(i);
                    o.Add(new(i, open - i + 1, C(SyntaxColor.Marker), font)); // "[" or "!["
                    Inline(s, open + 1, link.TextEnd, forceColor ? color : SyntaxColor.Link, font, o, forceColor: true);
                    o.Add(new(link.TextEnd, 2, C(SyntaxColor.Marker), font)); // "]("
                    if (link.UrlEnd > link.TextEnd + 2) o.Add(new(link.TextEnd + 2, link.UrlEnd - link.TextEnd - 2, C(SyntaxColor.Url), font));
                    o.Add(new(link.UrlEnd, 1, C(SyntaxColor.Marker), font)); // ")"
                    i = plainStart = link.UrlEnd + 1; continue;
                }
            }
            if (c == '<' && AutoLink(s, i, to) is int autoEnd)
            {
                Flush(i);
                o.Add(new(i, 1, C(SyntaxColor.Marker), font));
                o.Add(new(i + 1, autoEnd - i - 1, C(SyntaxColor.Link), font));
                o.Add(new(autoEnd, 1, C(SyntaxColor.Marker), font));
                i = plainStart = autoEnd + 1; continue;
            }
            if (c == '~' && i + 1 < to && s[i + 1] == '~')
            {
                var n = Run(s, i, to, '~');
                if (n == 2 && i + 2 < to && !char.IsWhiteSpace(s[i + 2]))
                {
                    var close = FindDelimiter(s, i + 2, to, '~', 2);
                    if (close >= 0)
                    {
                        Flush(i);
                        o.Add(new(i, 2, C(SyntaxColor.Marker), font));
                        Inline(s, i + 2, close, Emphasis(color, font | SyntaxFont.Strike, forceColor), font | SyntaxFont.Strike, o, forceColor);
                        o.Add(new(close, 2, C(SyntaxColor.Marker), font));
                        i = plainStart = close + 2; continue;
                    }
                }
                i += n; continue;
            }
            if (c is '*' or '_')
            {
                var n = Run(s, i, to, c);
                var canOpen = i + n < to && !char.IsWhiteSpace(s[i + n]) && !(c == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1]));
                if (canOpen)
                {
                    var want = Math.Min(n, 3);
                    for (var take = want; take >= 1; take--)
                    {
                        var close = FindDelimiter(s, i + take, to, c, take);
                        if (close < 0) continue;
                        Flush(i + n - take);
                        if (n > take) { /* extra delimiter chars stay plain */ }
                        var start = i + n - take;
                        var f = font | (take == 1 ? SyntaxFont.Italic : take == 2 ? SyntaxFont.Bold : SyntaxFont.Bold | SyntaxFont.Italic);
                        o.Add(new(start, take, C(SyntaxColor.Marker), font));
                        Inline(s, start + take, close, Emphasis(color, f, forceColor), f, o, forceColor);
                        o.Add(new(close, take, C(SyntaxColor.Marker), font));
                        i = plainStart = close + take;
                        goto next;
                    }
                }
                i += n; continue;
            }
            i++;
            next:;
        }
        Flush(to);
    }

    private static int Run(string s, int i, int to, char c) { var n = 0; while (i + n < to && s[i + n] == c) n++; return n; }

    // Closing code-span run of exactly n backticks.
    private static int FindRun(string s, int from, int to, char c, int n)
    {
        for (var i = from; i < to; i++)
        {
            if (s[i] != c) continue;
            var r = Run(s, i, to, c);
            if (r == n) return i;
            i += r - 1;
        }
        return -1;
    }

    // Closing emphasis delimiter: a run containing n chars, preceded by non-space; skips code spans.
    private static int FindDelimiter(string s, int from, int to, char c, int n)
    {
        for (var i = from; i < to; i++)
        {
            var ch = s[i];
            if (ch == '\\') { i++; continue; }
            if (ch == '`')
            {
                var r = Run(s, i, to, '`'); var close = FindRun(s, i + r, to, '`', r);
                i = close >= 0 ? close + r - 1 : i + r - 1; continue;
            }
            if (ch != c) continue;
            var run = Run(s, i, to, c);
            if (run >= n && i > from && !char.IsWhiteSpace(s[i - 1]))
            {
                var at = i; // take the first n chars of the run
                var after = at + n;
                if (c == '_' && after < to && char.IsLetterOrDigit(s[after])) { i += run - 1; continue; }
                return at;
            }
            i += run - 1;
        }
        return -1;
    }

    private readonly record struct LinkSpan(int TextEnd, int UrlEnd);

    // [text](url) starting at '[': returns the index of ']' (followed by '(') and of the closing ')'.
    private static LinkSpan? LinkAt(string s, int open, int to)
    {
        var depth = 0;
        for (var i = open; i < to; i++)
        {
            var ch = s[i];
            if (ch == '\\') { i++; continue; }
            if (ch == '`') { var r = Run(s, i, to, '`'); var close = FindRun(s, i + r, to, '`', r); if (close >= 0) i = close + r - 1; continue; }
            if (ch == '[') depth++;
            else if (ch == ']' && --depth == 0)
            {
                if (i + 1 >= to || s[i + 1] != '(') return null;
                var paren = 0;
                for (var j = i + 2; j < to; j++)
                {
                    if (s[j] == '\\') { j++; continue; }
                    if (s[j] == '(') paren++;
                    else if (s[j] == ')') { if (paren == 0) return new LinkSpan(i, j); paren--; }
                }
                return null;
            }
        }
        return null;
    }

    private static int? AutoLink(string s, int i, int to)
    {
        var close = s.IndexOf('>', i + 1, to - i - 1);
        if (close < 0) return null;
        var inner = s.AsSpan(i + 1, close - i - 1);
        if (inner.Contains(' ')) return null;
        return inner.StartsWith("http://") || inner.StartsWith("https://") || inner.StartsWith("mailto:") ? close : null;
    }
}
