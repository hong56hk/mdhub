using System.Text;

namespace Mdhub.Rendering;

// A run of rendered preview text: SourceStart is where it is copied verbatim from the source (-1 = not editable:
// markup, escapes, soft breaks, images). Code marks inline code, whose content is not Markdown-escaped.
public readonly record struct EditPiece(string Text, int SourceStart, bool Code = false);
public readonly record struct SourceEdit(int Start, int End, string Replacement);
public readonly record struct EditPlan(SourceEdit? Edit, string? Error)
{
    public bool NoChange => Edit is null && Error is null;
}

// Turns text typed in the preview into one source edit, without creating or breaking Markdown syntax.
// Pure (the structure check is passed in), so it is unit tested.
public static class PreviewEdit
{
    public static string SpansFormatting => Mdhub.Localization.Loc.T("PreviewSpansFormatting");
    public static string NoLineBreaks => Mdhub.Localization.Loc.T("PreviewNoLineBreaks");
    public static string ChangesStructure => Mdhub.Localization.Loc.T("PreviewChangesStructure");
    public static string Backtick => Mdhub.Localization.Loc.T("PreviewBacktick");

    private const string Specials = "\\`*_[]<>!~|#&";

    public static (int Start, int Removed, string Inserted) Diff(string before, string after)
    {
        var p = 0;
        while (p < before.Length && p < after.Length && before[p] == after[p]) p++;
        var s = 0;
        while (s < before.Length - p && s < after.Length - p && before[^(s + 1)] == after[^(s + 1)]) s++;
        return (p, before.Length - p - s, after.Substring(p, after.Length - p - s));
    }

    // Backslash-escapes inline punctuation; at the start of a rendered line also list/heading starters.
    public static string EscapeInline(string text, bool atLineStart)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) { if (Specials.Contains(c)) sb.Append('\\'); sb.Append(c); }
        var r = sb.ToString();
        if (!atLineStart || r.Length == 0) return r;
        if (r[0] is '-' or '+' or '=') return "\\" + r;
        var d = 0;
        while (d < r.Length && char.IsAsciiDigit(r[d])) d++;
        return d > 0 && d < r.Length && r[d] is '.' or ')' ? r[..d] + "\\" + r[d..] : r;
    }

    public static EditPlan PlanInline(string source, IReadOnlyList<EditPiece> pieces, string newText, Func<string, bool> keepsStructure)
    {
        var old = string.Concat(pieces.Select(p => p.Text));
        newText = newText.Replace("\r\n", "\n");
        if (old == newText) return default;
        var (at, removed, inserted) = Diff(old, newText);
        if (inserted.IndexOfAny(['\r', '\n']) >= 0) return new(null, NoLineBreaks);
        // Candidates: the mapped piece holding the change; a pure insertion on a boundary may go to the left or right piece.
        var candidates = new List<(EditPiece Piece, int Offset, int Rank)>();
        var offset = 0;
        foreach (var piece in pieces)
        {
            var end = offset + piece.Text.Length;
            if (piece.SourceStart >= 0 && offset <= at && at + removed <= end)
                candidates.Add((piece, offset, removed > 0 || (at > offset && at < end) ? 0 : at == end ? 1 : 2));
            offset = end;
        }
        if (candidates.Count == 0) return new(null, SpansFormatting);
        var lineStart = at == 0 || old[at - 1] == '\n';
        string? error = null;
        foreach (var (piece, pieceOffset, _) in candidates.OrderBy(c => c.Rank))
        {
            if (piece.Code && inserted.Contains('`')) { error ??= Backtick; continue; }
            var replacement = piece.Code ? inserted : EscapeInline(inserted, lineStart);
            var start = piece.SourceStart + (at - pieceOffset);
            var edited = string.Concat(source.AsSpan(0, start), replacement, source.AsSpan(start + removed));
            if (keepsStructure(edited)) return new(new SourceEdit(start, start + removed, replacement), null);
            error = ChangesStructure;
        }
        return new(null, error);
    }

    // Plain text of a fenced code block: [start, end) is its content; line breaks follow the note's style.
    public static EditPlan PlanCode(string source, int start, int end, string newText, Func<string, bool> keepsStructure)
    {
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";
        var normalized = newText.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline);
        var current = source[start..end];
        if (current == normalized) return default;
        var (at, removed, inserted) = Diff(current, normalized);
        var edit = new SourceEdit(start + at, start + at + removed, inserted);
        var edited = string.Concat(source.AsSpan(0, edit.Start), inserted, source.AsSpan(edit.End));
        return keepsStructure(edited) ? new(edit, null) : new(null, ChangesStructure);
    }
}
