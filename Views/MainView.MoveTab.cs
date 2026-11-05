using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 89 (VS Code "Move into Right/Left Group"): a tab moves to the neighbouring split, or to a new split on that side.
// The NoteDocument itself moves (its editor control keeps text, undo history, caret, selection and scroll); nothing reloads.
public partial class MainView
{
    // Shown only when the tab's split keeps at least one tab after the move.
    internal static bool CanMoveTab(EditorPane pane) => pane.Documents.Count >= 2;

    internal void MoveTabToSide(EditorPane source, NoteDocument document, int direction)
    {
        if (_busy || !_panes.Contains(source) || !source.Documents.Contains(document) || !CanMoveTab(source)) return;
        var targetIndex = _panes.IndexOf(source) + Math.Sign(direction);
        EditorPane target;
        if (targetIndex >= 0 && targetIndex < _panes.Count) target = _panes[targetIndex];
        else
        {
            if (_panes.Count >= MaxEditorPanes) { ShowTransientStatus(Loc.F("SplitLimitReached", MaxEditorPanes)); return; }
            target = new EditorPane { ViewMode = (int)ViewMode.Editor };
            WirePane(target);
            _panes.Insert(direction < 0 ? _panes.IndexOf(source) : _panes.IndexOf(source) + 1, target);
            RebuildPaneLayout();
        }
        document.RememberSelection();
        var scroll = EditorScroll(document);
        // Same file already open there: every view of a path shares one NoteBuffer, so unsaved edits and the shared
        // undo history already live in that tab. This view just closes (no prompt, nothing to merge, nothing lost).
        var existing = target.Documents.FirstOrDefault(d => d.Buffer == document.Buffer);
        DetachDocument(source, document);
        ActivatePane(target);
        if (existing is not null)
        {
            document.Dispose();
            FileTabs.SelectedDocument = existing;
            if (_active != existing) SelectDocument(existing);
        }
        else
        {
            _documents.Add(document);
            FileTabs.SelectedDocument = document;
            if (_active != document) SelectDocument(document);
            document.RestoreSelection();
            Dispatcher.UIThread.Post(() => { if (_active == document) { document.RestoreSelection(); SetEditorScroll(document, scroll); } }, DispatcherPriority.Loaded);
        }
        FocusEditor();
        Dispatcher.UIThread.Post(() => { if (_activePane == target) FocusEditor(); }, DispatcherPriority.Loaded);
    }

    // Takes a view out of its pane without disposing it; the pane shows a neighbouring tab first.
    private void DetachDocument(EditorPane pane, NoteDocument document)
    {
        var index = pane.Documents.IndexOf(document);
        if (index < 0) return;
        if (pane.Active == document && pane.Documents.Count > 1)
        {
            ActivatePane(pane);
            FileTabs.SelectedDocument = pane.Documents[index + 1 < pane.Documents.Count ? index + 1 : index - 1];
        }
        pane.Documents.Remove(document);
    }

    private static Avalonia.Vector EditorScroll(NoteDocument document) =>
        document.Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
    private static void SetEditorScroll(NoteDocument document, Avalonia.Vector offset)
    {
        if (document.Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller) scroller.Offset = offset;
    }

    // Session layout: per split (left to right), indices into the saved Tabs list; null for a single split.
    private List<List<int>>? CapturePaneLayout(List<NoteDocument> saved) => _panes.Count < 2 ? null
        : _panes.Select(p => p.Documents.Select(d => saved.FindIndex(s => s.Buffer == d.Buffer)).Where(i => i >= 0).ToList()).ToList();

    // Tabs were all opened in the first split; spread them over the saved splits and activate the saved one.
    private void RestorePaneLayout(WindowSession session)
    {
        if (session.Panes is not { Count: > 1 } layout) return;
        var first = _activePane;
        NoteDocument? Find(int i) => i >= 0 && i < session.Tabs.Count
            ? AllDocuments.FirstOrDefault(d => d.Path is { } p && SamePath(p, Path.GetFullPath(session.Tabs[i]))) : null;
        var keep = layout[0].ToHashSet();
        var placed = new HashSet<NoteBuffer>();
        var last = first;
        foreach (var indices in layout.Skip(1))
        {
            if (_panes.Count >= MaxEditorPanes) break;
            var pane = new EditorPane { ViewMode = (int)ViewMode.Editor };
            WirePane(pane);
            _panes.Insert(_panes.IndexOf(last) + 1, pane);
            RebuildPaneLayout();
            last = pane;
            foreach (var i in indices)
            {
                if (Find(i) is not { } document || pane.Documents.Any(d => d.Buffer == document.Buffer)) continue;
                if (keep.Contains(i) || placed.Contains(document.Buffer))
                {
                    ActivatePane(pane);
                    AddDocumentView(document);
                }
                else
                {
                    DetachDocument(first, document);
                    ActivatePane(pane);
                    _documents.Add(document);
                    FileTabs.SelectedDocument = document;
                }
                placed.Add(document.Buffer);
            }
            if (pane.Documents.Count == 0) RemovePane(pane, first);
        }
        if (first.Documents.Count == 0 && _panes.Count > 1) RemovePane(first, _panes.First(p => p != first));
        ActivatePane(_panes[Math.Clamp(session.ActivePane, 0, _panes.Count - 1)]);
    }
}
