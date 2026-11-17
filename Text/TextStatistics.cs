using System.Globalization;

namespace Mdhub.Text;

public readonly record struct TextStatistics(int Words, int Characters, int Lines)
{
    // Words are whitespace-delimited; characters are Unicode text elements, including whitespace.
    // A single pass (no Regex match allocations) so the status bar can update on large files.
    public static TextStatistics Count(string text)
    {
        if (string.IsNullOrEmpty(text)) return new(0, 0, 1);
        int words = 0, characters = 0, lines = 1;
        var inWord = false;
        for (var i = 0; i < text.Length; )
        {
            var element = StringInfo.GetNextTextElementLength(text, i);
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
                if (c == '\r')
                {
                    lines++;
                    if (i + 1 < text.Length && text[i + 1] == '\n' && element == 1) { i++; characters++; }
                }
                else if (c == '\n') lines++;
            }
            else if (!inWord) { words++; inWord = true; }
            characters++;
            i += element;
        }
        return new(words, characters, lines);
    }
}