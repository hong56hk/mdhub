namespace Mdhub.Text;

// Item 113: VS Code-like Enter / closer indent for .json files only. One CaretEdit is one undo step.
// Uses the editor's current indent unit (spaces or tab, and width) and the file's line ending.
public static class JsonAutoIndent
{
    public static bool AppliesTo(string? pathOrName)
    {
        if (string.IsNullOrEmpty(pathOrName)) return false;
        return Path.GetExtension(pathOrName).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    public static string LeadingWhitespace(string text, int index)
    {
        var start = TabEditing.LineStart(text, index);
        var i = start;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        return text[start..i];
    }

    // Enter: after { or [ indent one level; between {} or [] open a middle line and park the closer under the opener;
    // otherwise keep the current line's indent. Never treats brackets inside JSON strings as structure.
    public static CaretEdit Enter(string text, int caret, bool insertSpaces, int tabSize, string newLine)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        tabSize = TabEditing.Clamp(tabSize);
        if (string.IsNullOrEmpty(newLine)) newLine = "\n";
        var indent = LeadingWhitespace(text, caret);
        var unit = TabEditing.Unit(insertSpaces, tabSize);
        if (!InString(text, caret) && LastNonWhitespaceOnLine(text, caret) is { } prev && prev.Ch is '{' or '[')
        {
            var lineEnd = TabEditing.LineEnd(text, caret);
            if (FirstNonWhitespace(text, caret, lineEnd) is { } next
                && next.Ch == Closer(prev.Ch) && !InString(text, next.Index))
            {
                var insertion = newLine + indent + unit + newLine + indent;
                var mid = caret + newLine.Length + indent.Length + unit.Length;
                return new CaretEdit(caret, next.Index, insertion, mid, mid);
            }
            var deeper = newLine + indent + unit;
            var at = caret + deeper.Length;
            return new CaretEdit(caret, caret, deeper, at, at);
        }
        var keep = newLine + indent;
        var pos = caret + keep.Length;
        return new CaretEdit(caret, caret, keep, pos, pos);
    }

    // } or ] on a whitespace-only line: replace that line's indent with the matching opener's indent, then the closer.
    // Returns null when the line is not blank or no matching opener is found (the typist then inserts as usual).
    public static CaretEdit? Close(string text, int caret, char closer)
    {
        if (closer is not ('}' or ']')) return null;
        caret = Math.Clamp(caret, 0, text.Length);
        if (InString(text, caret)) return null;
        var start = TabEditing.LineStart(text, caret);
        var end = TabEditing.LineEnd(text, caret);
        for (var i = start; i < end; i++)
            if (text[i] is not (' ' or '\t')) return null;
        if (FindOpener(text, start, Opener(closer)) is not { } open) return null;
        var replacement = LeadingWhitespace(text, open) + closer;
        var at = start + replacement.Length;
        return new CaretEdit(start, end, replacement, at, at);
    }

    static char Closer(char opener) => opener == '{' ? '}' : ']';
    static char Opener(char closer) => closer == '}' ? '{' : '[';

    static (char Ch, int Index)? LastNonWhitespaceOnLine(string text, int caret)
    {
        var start = TabEditing.LineStart(text, caret);
        for (var i = caret - 1; i >= start; i--)
        {
            var c = text[i];
            if (c is ' ' or '\t') continue;
            return InString(text, i) ? null : (c, i);
        }
        return null;
    }

    static (char Ch, int Index)? FirstNonWhitespace(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            var c = text[i];
            if (c is ' ' or '\t') continue;
            return (c, i);
        }
        return null;
    }

    // Whether index sits inside a JSON string (after an opening quote that has not closed).
    public static bool InString(string text, int index)
    {
        var inString = false;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
        }
        return inString;
    }

    static int? FindOpener(string text, int before, char wanted)
    {
        var stack = new List<(char Ch, int Index)>();
        var inString = false;
        for (var i = 0; i < before && i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c is '{' or '[') stack.Add((c, i));
            else if (c is '}' or ']')
            {
                if (stack.Count > 0 && stack[^1].Ch == Opener(c)) stack.RemoveAt(stack.Count - 1);
            }
        }
        return stack.Count > 0 && stack[^1].Ch == wanted ? stack[^1].Index : null;
    }
}