namespace Mdhub.Text;

// The logical line around a caret: Start..End excludes the line break (\n, \r\n or \r).
public static class CaretLine
{
    public static (int Start, int End) At(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;
        while (start > 0 && text[start - 1] is not ('\n' or '\r')) start--;
        var end = caret;
        while (end < text.Length && text[end] is not ('\n' or '\r')) end++;
        return (start, end);
    }
}