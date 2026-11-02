using Avalonia.Controls;
using Avalonia.VisualTree;
using Mdhub.Rendering;

namespace Mdhub.Components;

public partial class MdEditor
{
    // Map the preview caret back to the current source without committing an open overlay.
    internal int? SourceCaret()
    {
        if (_render is not { } render || _active?.Editor.Text != render.Map.Source) return null;
        ClearHighlight(); // restore original runs before looking up their source segments
        Control? item = _editBlock;
        var offset = _editBox?.CaretIndex ?? _caret.Offset;
        if (item is null && _selection is { } selection && _caret.Item >= 0 && _caret.Item < selection.Items.Count)
            item = selection.Items[_caret.Item];
        if (item is null) return null;
        var matches = render.Map.Blocks.Where(b => render.Blocks.TryGetValue(b.Id, out var control)
            && (control == item || item.GetVisualAncestors().Contains(control)))
            .OrderBy(b => b.End - b.Start).ToList();
        if (matches.Count == 0) return null;
        var block = matches[0];

        if (render.CodeContent.TryGetValue(block.Id, out var range))
        {
            var raw = render.Map.Source[range.Start..range.End];
            var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            var breakLength = _editBox is null ? PreviewSelection.LineBreakLength : 1;
            offset = OriginalOffset(normalized.Replace("\n", new string('\n', breakLength)), offset);
            var at = 0;
            while (offset > 0 && at < raw.Length)
            {
                var newline = raw[at] is '\r' or '\n';
                if (raw[at] == '\r' && at + 1 < raw.Length && raw[at + 1] == '\n') at++;
                at++;
                offset -= newline ? breakLength : 1;
            }
            return range.Start + at;
        }
        if (item is TextBlock { Inlines: { } inlines })
        {
            var pieces = new List<EditPiece>();
            CollectPieces(inlines, render.Runs.ToDictionary(kv => kv.Value.Run, kv => kv.Key), render, pieces);
            if (_editBox is null)
                for (var i = 0; i < pieces.Count; i++)
                    if (pieces[i] is { SourceStart: -1, Text: "\n" } piece)
                        pieces[i] = piece with { Text = new string('\n', PreviewSelection.LineBreakLength) };
            offset = OriginalOffset(string.Concat(pieces.Select(p => p.Text)), offset);
            int? previous = null;
            foreach (var piece in pieces)
            {
                if (piece.SourceStart >= 0)
                {
                    if (offset <= piece.Text.Length) return piece.SourceStart + Math.Max(0, offset);
                    previous = piece.SourceStart + piece.Text.Length;
                }
                offset -= piece.Text.Length;
            }
            if (previous is { } end) return end;
        }
        return block.Start;
    }

    private int OriginalOffset(string original, int caret)
    {
        if (_editBox is null) return Math.Clamp(caret, 0, original.Length);
        var (at, removed, inserted) = PreviewEdit.Diff(original, _editBox.Text ?? "");
        // Uncommitted characters occupy the replaced source range until the edit is accepted.
        var mapped = caret <= at ? caret : caret <= at + inserted.Length
            ? at + Math.Min(caret - at, removed) : caret - inserted.Length + removed;
        return Math.Clamp(mapped, 0, original.Length);
    }
}
