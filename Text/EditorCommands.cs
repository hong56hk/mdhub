using System.Globalization;

namespace Mdhub.Text;

// Pure helpers behind the editor shortcuts (insert time, go to line).
public static class EditorCommands
{
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    public static string Timestamp(DateTime time) => time.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    // Logical lines: \r\n, \n and \r each end one line; an empty text has one line.
    public static int LineCount(string text)
    {
        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') { count++; if (i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (text[i] == '\n') count++;
        }
        return count;
    }

    public static bool IsDigits(string? s) => !string.IsNullOrEmpty(s) && s.All(char.IsAsciiDigit);

    // The typed line number clamped to 1..lineCount, or null when the input is empty or not digits.
    public static int? ParseLine(string? input, int lineCount)
    {
        if (!IsDigits(input)) return null;
        var trimmed = input!.TrimStart('0');
        if (trimmed.Length > 9) return Math.Max(1, lineCount);
        return Math.Clamp(trimmed.Length == 0 ? 0 : int.Parse(trimmed, CultureInfo.InvariantCulture), 1, Math.Max(1, lineCount));
    }

    // Offset of the start of a 1-based logical line (clamped to the text).
    public static int LineStart(string text, int line)
    {
        var current = 1;
        for (var i = 0; i < text.Length && current < line; i++)
        {
            if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; current++; if (current == line) return i + 1; }
            else if (text[i] == '\n') { current++; if (current == line) return i + 1; }
        }
        return line <= 1 ? 0 : text.Length;
    }
}
