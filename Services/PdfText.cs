using System.Text;

namespace Mdhub.Services;

// Item 106: assemble selected PDF glyphs into plain text, and flatten that text for a notes quote.
// Docnet's box Top is the ink top, not the baseline - spaces have zero height and sit at the baseline -
// so comparing Tops inserted a line break around every word. Line breaks follow the lower edge of
// non-whitespace glyphs instead. CR/LF in the glyph list are real breaks and are not copied as characters.
public static class PdfText
{
    public readonly record struct Glyph(char Char, double Top, double Bottom, double FontSize);

    public static string FromGlyphs(IReadOnlyList<Glyph> glyphs) =>
        glyphs.Count == 0 ? "" : FromGlyphs(glyphs, 0, glyphs.Count - 1);

    public static string FromGlyphs(IReadOnlyList<Glyph> glyphs, int from, int to)
    {
        if (glyphs.Count == 0 || to < from) return "";
        from = Math.Clamp(from, 0, glyphs.Count - 1);
        to = Math.Clamp(to, 0, glyphs.Count - 1);
        var sb = new StringBuilder();
        double? lastBase = null;
        var atBreak = false;
        for (var i = from; i <= to; i++)
        {
            var g = glyphs[i];
            var ch = g.Char;
            if (ch == '\0') continue;
            if (IsLineSeparator(ch))
            {
                if (ch == '\n' && i > from && glyphs[i - 1].Char == '\r' && atBreak) continue;
                if (sb.Length > 0 && !atBreak) sb.Append('\n');
                atBreak = true;
                lastBase = null;
                continue;
            }
            if (!char.IsWhiteSpace(ch))
            {
                var baseline = Math.Max(g.Top, g.Bottom);
                var thresh = Math.Max(2, g.FontSize * 0.5);
                if (lastBase is { } y && Math.Abs(baseline - y) > thresh && sb.Length > 0 && !atBreak)
                    sb.Append('\n');
                lastBase = baseline;
            }
            sb.Append(ch);
            atBreak = false;
        }
        return sb.ToString();
    }

    // CR/LF, U+2028/U+2029 and other line or paragraph separators become one space; whitespace
    // runs collapse; then trim. A space between two Han, Hiragana or Katakana characters is
    // dropped so zh/ja text is not padded. Hangul keeps a space when the source had one, because
    // Korean writes spaces between words.
    public static string NormalizeForNotes(string selected)
    {
        if (string.IsNullOrEmpty(selected)) return "";
        var sb = new StringBuilder(selected.Length);
        var pendingSpace = false;
        var hasPrev = false;
        Rune prev = default;
        foreach (var rune in selected.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = hasPrev;
                continue;
            }
            if (pendingSpace)
            {
                if (!(hasPrev && IsCjkLetter(prev) && IsCjkLetter(rune)))
                    sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(rune);
            prev = rune;
            hasPrev = true;
        }
        return sb.ToString();
    }

    static bool IsLineSeparator(char ch) =>
        ch is '\n' or '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029';

    static bool IsCjkLetter(Rune rune) => ScriptOf(rune) is Script.Han or Script.Hiragana or Script.Katakana;

    // Item 108: a word is letters and digits, plus an apostrophe or hyphen between two of those.
    // CJK (no spaces) is a run of the same script; a single character if no word can be formed.
    // Whitespace is not a word (the viewer resets). Other punctuation is that one character.
    public static (int From, int To)? WordRange(IReadOnlyList<Glyph> glyphs, int index)
    {
        if (index < 0 || index >= glyphs.Count) return null;
        var ch = glyphs[index].Char;
        if (ch == '\0' || IsLineSeparator(ch) || char.IsWhiteSpace(ch)) return null;
        var script = ScriptOf(ch);
        if (script is Script.Han or Script.Hiragana or Script.Katakana or Script.Hangul)
        {
            var from = index;
            while (from > 0 && ScriptOf(glyphs[from - 1].Char) == script) from--;
            var to = index;
            while (to + 1 < glyphs.Count && ScriptOf(glyphs[to + 1].Char) == script) to++;
            return (from, to);
        }
        if (IsLatinWordChar(ch) || IsConnectorBetweenWords(glyphs, index))
        {
            var from = index;
            while (from > 0 && JoinsWord(glyphs, from, from - 1)) from--;
            var to = index;
            while (to + 1 < glyphs.Count && JoinsWord(glyphs, to, to + 1)) to++;
            return (from, to);
        }
        return (index, index);
    }

    public static (int From, int To)? LineRange(IReadOnlyList<Glyph> glyphs, int index)
    {
        if (index < 0 || index >= glyphs.Count) return null;
        var ch = glyphs[index].Char;
        if (ch == '\0' || IsLineSeparator(ch)) return null;
        double? baseLine = Baseline(glyphs[index]);
        if (baseLine is null)
        {
            for (var j = index - 1; j >= 0 && baseLine is null; j--) baseLine = Baseline(glyphs[j]);
            for (var j = index + 1; j < glyphs.Count && baseLine is null; j++) baseLine = Baseline(glyphs[j]);
        }
        if (baseLine is null) return (index, index);
        var from = index;
        while (from > 0 && OnSameLine(glyphs[from - 1], baseLine.Value, glyphs[index].FontSize)) from--;
        var to = index;
        while (to + 1 < glyphs.Count && OnSameLine(glyphs[to + 1], baseLine.Value, glyphs[index].FontSize)) to++;
        return (from, to);
    }

    static bool JoinsWord(IReadOnlyList<Glyph> glyphs, int from, int toward)
    {
        var other = glyphs[toward].Char;
        if (IsLatinWordChar(other)) return true;
        if (!IsConnector(other)) return false;
        var far = toward + Math.Sign(toward - from);
        return far >= 0 && far < glyphs.Count && IsLatinWordChar(glyphs[far].Char);
    }

    static bool IsConnectorBetweenWords(IReadOnlyList<Glyph> glyphs, int index) =>
        IsConnector(glyphs[index].Char)
        && index > 0 && IsLatinWordChar(glyphs[index - 1].Char)
        && index + 1 < glyphs.Count && IsLatinWordChar(glyphs[index + 1].Char);

    static bool IsLatinWordChar(char ch) => char.IsLetterOrDigit(ch) && ScriptOf(ch) == Script.Other;

    static bool IsConnector(char ch) => ch is '\'' or '\u2019' or '\u02BC' or '-' or '\u2010';

    static double? Baseline(Glyph g) =>
        g.Char == '\0' || IsLineSeparator(g.Char) || char.IsWhiteSpace(g.Char) ? null : Math.Max(g.Top, g.Bottom);

    static bool OnSameLine(Glyph g, double baseline, double font)
    {
        if (IsLineSeparator(g.Char) || g.Char == '\0') return false;
        var y = Baseline(g) ?? Math.Max(g.Top, g.Bottom);
        var thresh = Math.Max(2, Math.Max(font, g.FontSize) * 0.5);
        return Math.Abs(y - baseline) <= thresh;
    }

    enum Script { Other, Han, Hiragana, Katakana, Hangul }

    static Script ScriptOf(char ch) => ScriptOf(new Rune(ch));

    static Script ScriptOf(Rune rune)
    {
        var v = rune.Value;
        if (v is >= 0x3040 and <= 0x309F) return Script.Hiragana;
        if (v is >= 0x30A0 and <= 0x30FF) return Script.Katakana;
        if (v is >= 0x31F0 and <= 0x31FF) return Script.Katakana;
        if (v is >= 0xFF66 and <= 0xFF9D) return Script.Katakana;
        if (v is (>= 0x1100 and <= 0x11FF) or (>= 0x3130 and <= 0x318F) or (>= 0xA960 and <= 0xA97F)
            or (>= 0xAC00 and <= 0xD7AF) or (>= 0xD7B0 and <= 0xD7FF)) return Script.Hangul;
        if (v is >= 0x3400 and <= 0x4DBF) return Script.Han;
        if (v is >= 0x4E00 and <= 0x9FFF) return Script.Han;
        if (v is >= 0xF900 and <= 0xFAFF) return Script.Han;
        if (v is >= 0x20000 and <= 0x2A6DF) return Script.Han;
        if (v is >= 0x2A700 and <= 0x2B73F) return Script.Han;
        if (v is >= 0x2B740 and <= 0x2B81F) return Script.Han;
        if (v is >= 0x2B820 and <= 0x2CEAF) return Script.Han;
        if (v is >= 0x2CEB0 and <= 0x2EBEF) return Script.Han;
        if (v is >= 0x30000 and <= 0x3134F) return Script.Han;
        if (v is >= 0x31350 and <= 0x323AF) return Script.Han;
        return Script.Other;
    }
}
