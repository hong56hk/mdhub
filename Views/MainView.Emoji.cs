using Avalonia.Controls;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    private bool CanInsertEmoji(NoteDocument document) => !_busy && _active == document
        && !document.IsViewOnly && !document.IsReadOnlyText && !document.Editor.IsReadOnly
        && TextEncodings.SupportsEmoji(document.Encoding);

    private Flyout? _emojiPicker;

    private void ShowEmojiPicker(Control target)
    {
        if (_active is not { } document || !CanInsertEmoji(document)) return;
        if (_emojiPicker?.IsOpen == true) return;
        var flyout = _emojiPicker = BuildEmojiPicker(document);
        flyout.Closed += (_, _) => { if (_emojiPicker == flyout) _emojiPicker = null; Shortcuts.ReleaseAll(); };
        flyout.ShowAt(target);
    }

    internal Flyout BuildEmojiPicker(NoteDocument document)
    {
        var picker = new EmojiPicker();
        var flyout = new Flyout { Content = picker };
        picker.EmojiSelected += emoji => InsertEmojiCore(document, emoji, focusEditor: false);
        picker.EmojiCommitted += emoji =>
        {
            flyout.Hide();
            InsertEmojiCore(document, emoji, focusEditor: true);
        };
        picker.DismissRequested += () =>
        {
            flyout.Hide();
            if (CanInsertEmoji(document)) FocusEditor();
        };
        flyout.Opened += (_, _) => picker.FocusSearch();
        return flyout;
    }

    internal void InsertEmoji(NoteDocument document, string emoji) => InsertEmojiCore(document, emoji, focusEditor: true);

    private void InsertEmojiCore(NoteDocument document, string emoji, bool focusEditor)
    {
        // A flyout can outlive a tab/encoding change. Never edit the wrong or newly read-only document.
        if (!CanInsertEmoji(document)) return;
        if (!TextEditor.IsVisible)
        {
            _viewMode = ViewMode.Split;
            UpdateContentLayout();
        }
        if (focusEditor) document.Editor.Focus();
        document.RestoreSelection();
        var start = Math.Min(document.Editor.SelectionStart, document.Editor.SelectionEnd);
        document.Editor.SelectedText = emoji;
        document.Editor.SelectionStart = document.Editor.SelectionEnd = start + emoji.Length;
        document.RememberSelection();
        EmojiRecents.Add(emoji);
        if (focusEditor) document.Editor.Focus();
        UpdateStatus();
    }
}
