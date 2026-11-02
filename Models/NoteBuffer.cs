using System.Text;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Models;

// File state has no editor control; panes create independent NoteDocument views over it.
public sealed class NoteBuffer
{
    public string? Path { get; set; }
    public string UntitledName { get; set; } = "Untitled.md";
    public string Text { get; private set; } = "";
    public string SavedText { get; set; } = "";
    public Encoding Encoding { get; set; } = TextFileStore.DefaultEncoding;
    public Avalonia.Media.Imaging.Bitmap? Image { get; set; }
    public bool IsBinary { get; set; }
    public bool IsReadOnlyText { get; set; }
    public LineEnding LineEnding { get; set; } = LineEndingSettings.ForNewFile;
    public LineEnding SavedLineEnding { get; set; } = LineEndingSettings.ForNewFile;
    private sealed record TextEdit(int Start, string Removed, string Inserted);
    private readonly List<TextEdit> _undo = [];
    private readonly List<TextEdit> _redo = [];
    private bool _replaying;
    internal bool HasSharedHistory { get; private set; }
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal int Views { get; private set; }
    internal void Attach() { Views++; if (Views > 1) HasSharedHistory = true; }
    internal void Detach() => Views--;
    internal void ResetHistory() { _undo.Clear(); _redo.Clear(); }
    internal void Undo()
    {
        if (!CanUndo) return;
        var edit = _undo[^1];
        _redo.Add(edit);
        _undo.RemoveAt(_undo.Count - 1);
        Replay(Text[..edit.Start] + edit.Removed + Text[(edit.Start + edit.Inserted.Length)..]);
    }
    internal void Redo()
    {
        if (!CanRedo) return;
        var edit = _redo[^1];
        _undo.Add(edit);
        _redo.RemoveAt(_redo.Count - 1);
        Replay(Text[..edit.Start] + edit.Inserted + Text[(edit.Start + edit.Removed.Length)..]);
    }
    private void Replay(string text)
    {
        _replaying = true;
        try { Text = text; Changed?.Invoke(null); }
        finally { _replaying = false; }
    }
    internal event Action<NoteDocument?>? Changed;
    internal void Notify() => Changed?.Invoke(null);
    internal void UpdateText(string text, NoteDocument source)
    {
        if (Text == text) return;
        if (!_replaying)
        {
            var prefix = 0;
            while (prefix < Text.Length && prefix < text.Length && Text[prefix] == text[prefix]) prefix++;
            var suffix = 0;
            while (suffix < Text.Length - prefix && suffix < text.Length - prefix && Text[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
            _undo.Add(new TextEdit(prefix, Text[prefix..(Text.Length - suffix)], text[prefix..(text.Length - suffix)]));
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
        }
        Text = text;
        Changed?.Invoke(source);
    }
}
