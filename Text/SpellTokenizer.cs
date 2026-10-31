using System.Text.RegularExpressions;

namespace Mdhub.Text;

// Item 74: the words of a Markdown document that spell checking looks at (prose only). UI-free and dictionary-free.
// Skipped: fenced code blocks, indented code blocks, front matter, code spans, URLs and e-mail addresses, link and
// image targets, reference definitions, autolinks, HTML tags and comments, ALL-CAPS words, words with digits,
// file names / paths / identifiers, single letters and CJK text.
public readonly record struct SpellWord(int Start, int Length, string Word)
{
    public int End => Start + Length;
}

public static class SpellTokenizer
{
    private static readonly Regex FenceOpen = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.Compiled);
    private static readonly Regex Masks = new(
        @"(`+)[\s\S]*?\1" +                                   // code spans
        @"|<!--[\s\S]*?-->" +                                 // HTML comments
        @"|<[A-Za-z][^<>\n]*>|</[A-Za-z][^<>\n]*>" +          // HTML tags and autolinks
        @"|\]\([^)\n]*\)" +                                   // link / image targets
        @"|^ {0,3}\[[^\]\n]+\]:.*$" +                         // reference definitions
        @"|\b(?:[a-zA-Z][a-zA-Z0-9+.-]*://|www\.)[^\s<>()\[\]]*" + // URLs
        @"|[\w.+-]+@[\w-]+(?:\.[\w-]+)+",                     // e-mail addresses
        RegexOptions.Compiled | RegexOptions.Multiline);
    // A run of word-like characters; apostrophes, dots, hyphens and path characters are kept inside so the rules below
    // can see file names and identifiers whole.
    private static readonly Regex Raw = new(@"[\p{L}\p{M}\p{N}_'’./\\@-]+", RegexOptions.Compiled);

    // Words in [from, to) of the document (offsets into text). The whole document is scanned for block state, which is cheap.
    public static List<SpellWord> Words(string text, int from = 0, int to = int.MaxValue)
    {
        var result = new List<SpellWord>();
        to = Math.Min(to, text.Length);
        var lineStart = 0;
        var lineNo = 0;
        var fenceChar = '\0'; var fenceLen = 0;
        var frontMatter = false;
        var previousBlank = true;
        var inList = false;
        while (lineStart <= text.Length && lineStart < to)
        {
            var nl = text.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? text.Length : nl;
            var contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = text[lineStart..contentEnd];
            var blank = line.Trim().Length == 0;
            var prose = true;
            if (frontMatter) { prose = false; if (lineNo > 0 && line.TrimEnd() is "---" or "...") frontMatter = false; }
            else if (lineNo == 0 && line.TrimEnd() == "---") { frontMatter = true; prose = false; }
            else if (fenceChar != '\0')
            {
                prose = false;
                var t = line.TrimStart(' ');
                var run = 0; while (run < t.Length && t[run] == fenceChar) run++;
                if (line.Length - t.Length <= 3 && run >= fenceLen && t[run..].Trim().Length == 0) fenceChar = '\0';
            }
            else if (FenceOpen.Match(line) is { Success: true } f) { fenceChar = f.Groups[1].Value[0]; fenceLen = f.Groups[1].Length; prose = false; }
            else if (!blank && previousBlank && !inList && (line.StartsWith("    ") || line.StartsWith('\t'))) prose = false; // indented code
            if (!blank && prose) inList = IsListItem(line) || (inList && (line.StartsWith(' ') || line.StartsWith('\t')));
            if (prose && !blank && lineEnd > from) AddLine(line, lineStart, from, to, result);
            previousBlank = blank;
            if (nl < 0) break;
            lineStart = nl + 1;
            lineNo++;
        }
        return result;
    }

    private static bool IsListItem(string line) => Regex.IsMatch(line, @"^\s*(?:[-*+]|\d{1,9}[.)])\s");

    private static void AddLine(string line, int offset, int from, int to, List<SpellWord> result)
    {
        var masked = new bool[line.Length];
        foreach (Match m in Masks.Matches(line))
        {
            var start = m.Index; var end = m.Index + m.Length;
            // "](target)": keep the closing bracket's text visible (it belongs to the link text, which is prose).
            if (m.Value.StartsWith("](")) start++;
            for (var i = start; i < end; i++) masked[i] = true;
        }
        foreach (Match m in Raw.Matches(line))
        {
            var s = m.Index; var e = m.Index + m.Length;
            if (Enumerable.Range(s, e - s).Any(i => masked[i])) { Split(line, masked, s, e, offset, from, to, result); continue; }
            Add(line, s, e, offset, from, to, result);
        }
    }

    // A raw token partly covered by a mask: only its unmasked pieces count.
    private static void Split(string line, bool[] masked, int s, int e, int offset, int from, int to, List<SpellWord> result)
    {
        var i = s;
        while (i < e)
        {
            while (i < e && masked[i]) i++;
            var j = i; while (j < e && !masked[j]) j++;
            if (j > i) Add(line, i, j, offset, from, to, result);
            i = j;
        }
    }

    private static void Add(string line, int s, int e, int offset, int from, int to, List<SpellWord> result)
    {
        // Trim surrounding punctuation that is not part of a word.
        while (s < e && line[s] is '\'' or '’' or '.' or '-' or '/' or '\\' or '@' or '_') s++;
        while (e > s && line[e - 1] is '\'' or '’' or '.' or '-' or '/' or '\\' or '@' or '_') e--;
        if (e <= s) return;
        var token = line[s..e];
        if (token.Any(char.IsDigit)) return;                                       // words with digits
        if (token.IndexOfAny(['/', '\\', '@', '_', '.']) >= 0) return;              // file names, paths, identifiers, abbreviations
        // Possessive 's is part of the word; hyphenated words are checked part by part.
        var at = s;
        foreach (var part in token.Split('-'))
        {
            var ps = at; at += part.Length + 1;
            if (part.Length == 0) continue;
            if (!Checkable(part)) continue;
            if (offset + ps + part.Length <= from || offset + ps >= to) continue;
            result.Add(new SpellWord(offset + ps, part.Length, part.Replace('’', '\'')));
        }
    }

    public static bool Checkable(string word)
    {
        var letters = word.Count(char.IsLetter);
        if (letters < 2) return false;                                              // single letters
        if (word.Any(IsCjk)) return false;                                          // CJK
        if (word.Where(char.IsLetter).All(char.IsUpper)) return false;              // ALL-CAPS
        // camelCase / PascalCase with an inner capital (identifiers like getValue or iPhone) is skipped too.
        if (word.Skip(1).Any(char.IsUpper)) return false;
        return true;
    }

    public static bool IsCjk(char c) =>
        c is >= '\u1100' and <= '\u11FF' or >= '\u2E80' and <= '\u2FDF' or >= '\u3000' and <= '\u30FF' or >= '\u3100' and <= '\u31FF'
          or >= '\u3200' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uA960' and <= '\uA97F' or >= '\uAC00' and <= '\uD7FF'
          or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF' || char.IsSurrogate(c);

    // The word at (or touching) an offset, for the context menu.
    public static SpellWord? WordAt(IReadOnlyList<SpellWord> words, int index)
    {
        foreach (var w in words) if (index >= w.Start && index <= w.End) return w;
        return null;
    }
}
