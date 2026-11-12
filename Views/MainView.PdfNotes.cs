using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Views;

public partial class MainView
{
    // Item 102c: the Notes button toggles <pdf-stem>.notes.md in a right split, creating .assets and the file only on click.
    private async Task TogglePdfNotesAsync(EditorPane pdfPane)
    {
        if (pdfPane.Active is not { Path: { Length: > 0 } pdfPath }) return;
        var notesPath = PdfNotes.PathFor(pdfPath);
        var existing = AllDocuments.FirstOrDefault(d => SamePath(d.Path, notesPath));
        if (existing is not null)
        {
            await CloseDocumentAsync(existing);
            if (_panes.Contains(pdfPane)) ActivatePane(pdfPane);
            pdfPane.PdfViewer.Focus();
            RefreshPdfNotesChrome();
            return;
        }
        if (!await EnsurePdfNotesFileAsync(notesPath)) return;
        if (!await OpenPdfNotesSplitAsync(pdfPane, notesPath)) return;
        ActivatePane(pdfPane);
        pdfPane.PdfViewer.Focus();
        RefreshPdfNotesChrome();
    }

    // Right-click "Add to notes": open the split if needed, append the quoted selection and page link, keep the PDF focused.
    private async Task AddToPdfNotesAsync(EditorPane pdfPane)
    {
        var viewer = pdfPane.PdfViewer;
        if (pdfPane.Active is not { Path: { Length: > 0 } pdfPath } || !viewer.HasSelection) return;
        var notesPath = PdfNotes.PathFor(pdfPath);
        if (!await EnsurePdfNotesFileAsync(notesPath)) return;
        var notes = AllDocuments.FirstOrDefault(d => SamePath(d.Path, notesPath));
        if (notes is null)
        {
            if (!await OpenPdfNotesSplitAsync(pdfPane, notesPath)) return;
            notes = AllDocuments.FirstOrDefault(d => SamePath(d.Path, notesPath));
            if (notes is null) return;
        }
        var ending = LineEndings.Sequence(notes.LineEnding);
        notes.Editor.Text = PdfNotes.ApplyAppend(notes.Text, viewer.SelectedText, pdfPath, viewer.SelectionPage, ending);
        await SaveAsync(notes, false);
        ActivatePane(pdfPane);
        pdfPane.PdfViewer.Focus();
        RefreshPdfNotesChrome();
    }

    private async Task<bool> EnsurePdfNotesFileAsync(string notesPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(notesPath)!);
            if (!File.Exists(notesPath)) await File.WriteAllTextAsync(notesPath, "");
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus(Loc.F("PdfNotesFailed", ex.Message));
            return false;
        }
    }

    private async Task<bool> OpenPdfNotesSplitAsync(EditorPane pdfPane, string notesPath)
    {
        ActivatePane(pdfPane);
        var pane = CreatePaneToSide();
        if (pane is null) return false;
        pane.ViewMode = (int)ViewMode.Split;
        ActivatePane(pane);
        try
        {
            await OpenPathAsync(notesPath);
            if (pane.Documents.Count == 0) { RemovePane(pane, pdfPane); return false; }
            UpdateContentLayout(pane);
            return true;
        }
        catch
        {
            if (_panes.Contains(pane)) RemovePane(pane, pdfPane);
            throw;
        }
    }

    // Keep the Notes button's on-state in sync when the split is opened or closed (toggle, tab close, Ctrl+W).
    private void RefreshPdfNotesChrome()
    {
        foreach (var pane in _panes)
        {
            var open = pane.Active is { IsPdf: true, Path: { Length: > 0 } pdfPath }
                && AllDocuments.Any(d => SamePath(d.Path, PdfNotes.PathFor(pdfPath)));
            pane.PdfViewer.SetNotesOpen(open);
        }
    }

    private async Task OpenPdfPageAsync(string pdfPath, int page)
    {
        var existing = AllDocuments.FirstOrDefault(d => SamePath(d.Path, pdfPath));
        if (existing is not null)
        {
            var pane = _panes.First(p => p.Documents.Contains(existing));
            ActivatePane(pane);
            pane.FileTabs.SelectedDocument = existing;
            ScrollPdfToPage(pane, page);
            return;
        }
        await OpenPathAsync(pdfPath);
        if (_active is { IsPdf: true } && SamePath(_active.Path, pdfPath))
            ScrollPdfToPage(_activePane, page);
    }

    static void ScrollPdfToPage(EditorPane pane, int page)
    {
        pane.PdfViewer.ScrollToPage(page);
        pane.PdfViewer.Focus();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (pane.PdfViewer.IsVisible) { pane.PdfViewer.ScrollToPage(page); pane.PdfViewer.Focus(); }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}