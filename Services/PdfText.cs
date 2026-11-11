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

    static bool IsCjkLetter(Rune rune)
    {
        var v = rune.Value;
        if (v is >= 0x3040 and <= 0x309F) return true; // Hiragana
        if (v is >= 0x30A0 and <= 0x30FF) return true; // Katakana
        if (v is >= 0x31F0 and <= 0x31FF) return true; // Katakana Phonetic Extensions
        if (v is >= 0xFF66 and <= 0xFF9D) return true; // Halfwidth Katakana letters
        if (v is >= 0x3400 and <= 0x4DBF) return true; // CJK Ext A
        if (v is >= 0x4E00 and <= 0x9FFF) return true; // CJK Unified Ideographs
        if (v is >= 0xF900 and <= 0xFAFF) return true; // CJK Compatibility Ideographs
        if (v is >= 0x20000 and <= 0x2A6DF) return true;
        if (v is >= 0x2A700 and <= 0x2B73F) return true;
        if (v is >= 0x2B740 and <= 0x2B81F) return true;
        if (v is >= 0x2B820 and <= 0x2CEAF) return true;
        if (v is >= 0x2CEB0 and <= 0x2EBEF) return true;
        if (v is >= 0x30000 and <= 0x3134F) return true;
        if (v is >= 0x31350 and <= 0x323AF) return true;
        return false;
    }
}
