using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Text;

namespace Mdhub.Views;

// Item 68 (VS Code): the status bar shows the document's line ending (CRLF/LF/CR); a click switches it, converting
// every line break as one undo step and marking the file modified. A file that mixes endings shows a warning that
// normalizes them to the majority. Saving writes the text as it is (TextFileStore does not touch line breaks).
public partial class MainView
{
    private void WireLineEndingMenu()
    {
        BottomStatusBar.LineEndingRequested += target => { if (_active is { IsViewOnly: false } && !_busy) BuildLineEndingMenu(_active).ShowAt(target); };
        BottomStatusBar.NormalizeLineEndingsRequested += () => { if (_active is { } document && !_busy) NormalizeLineEndings(document); };
    }

    internal MenuFlyout BuildLineEndingMenu(NoteDocument document)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Name = "LineEndingMenuHeader", Header = Loc.T("LineEndingMenuTip"), IsEnabled = false });
        foreach (var ending in new[] { LineEnding.CrLf, LineEnding.Lf, LineEnding.Cr })
        {
            var item = new MenuItem
            {
                Name = "LineEnding" + LineEndings.Label(ending), Header = LineEndings.Label(ending), Tag = ending,
                ToggleType = MenuItemToggleType.Radio, IsChecked = ending == document.LineEnding,
                IsEnabled = !document.IsReadOnlyText && !document.Editor.IsReadOnly
            };
            item.Click += (_, _) => SetLineEnding(document, ending);
            flyout.Items.Add(item);
        }
        return flyout;
    }

    // Converts every line break to the ending (one undo step; the selection follows its text) and makes it the
    // document's ending for Enter and paste. Returns false for view-only or read-only documents.
    internal bool SetLineEnding(NoteDocument document, LineEnding ending, string? message = null)
    {
        if (document.IsViewOnly || document.IsReadOnlyText || document.Editor.IsReadOnly) return false;
        var text = document.Text;
        var normalized = LineEndings.Normalize(text, ending);
        document.LineEnding = ending;
        message ??= Loc.F("LineEndingChanged", LineEndings.Label(ending));
        if (normalized != text && document.Editor is MarkdownTextBox editor)
        {
            int Map(int offset) => LineEndings.Normalize(text[..Math.Clamp(offset, 0, text.Length)], ending).Length;
            editor.ApplyEdit(new CaretEdit(0, text.Length, normalized, Map(editor.SelectionStart), Map(editor.SelectionEnd)));
        }
        document.Refresh();
        if (document.IsDirty) _sessionNote = null;
        if (_active == document) { UpdateStatus(message); MdEditor.ScheduleRefresh(); }
        return true;
    }

    // The mixed-endings warning: every line break becomes the majority ending (the document's).
    internal bool NormalizeLineEndings(NoteDocument document) =>
        document.LineEndingCounts.IsMixed && SetLineEnding(document, document.LineEnding, Loc.F("LineEndingsNormalized", LineEndings.Label(document.LineEnding)));
}