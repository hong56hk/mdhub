using Avalonia.Controls;
using Mdhub.Services;


namespace Mdhub.Views;

public partial class MainView
{
    private enum ViewMode { Editor, Split, Viewer }
    private ViewMode _viewMode = ViewMode.Split;

    private void UpdateContentLayout() => UpdateContentLayout(_activePane);

    private void UpdateContentLayout(Mdhub.Components.EditorPane pane)
    {
        var active = pane == _activePane;
        var document = pane.Active;
        var mode = active ? _viewMode : (ViewMode)pane.ViewMode;
        var hasPreview = document?.HasPreview == true;
        var image = document?.IsImage == true;
        var pdf = document?.IsPdf == true;
        var binary = document?.IsBinary == true;
        var preview = hasPreview && mode != ViewMode.Editor;
        var split = preview && mode == ViewMode.Split;
        var grid = pane.ContentGrid;
        if (pane.PreviewSplitter.IsVisible && !split) pane.PreviewWidth = grid.ColumnDefinitions[4].Width;
        pane.MdEditor.IsVisible = preview;
        pane.PreviewSplitter.IsVisible = split;
        grid.ColumnDefinitions[3].Width = new GridLength(split ? 4 : 0);
        grid.ColumnDefinitions[4].Width = split ? pane.PreviewWidth : new GridLength(0);
        Grid.SetColumnSpan(pane.TextEditor, preview ? 1 : 3);
        Grid.SetColumn(pane.MdEditor, split ? 4 : 2);
        Grid.SetColumnSpan(pane.MdEditor, split ? 1 : 3);
        pane.TextEditor.IsVisible = !image && !pdf && !binary && (!hasPreview || mode != ViewMode.Viewer);
        pane.BinaryPlaceholder.IsVisible = binary;
        pane.BinaryPlaceholder.ShowFile(binary ? document!.Path : null);
        pane.ImageViewer.IsVisible = image;
        pane.ImageViewer.ShowImage(document?.Image);
        pane.PdfViewer.IsVisible = pdf;
        pane.PdfViewer.Show(document?.Pdf);
        if (image || pdf || binary) pane.TextEditor.CloseFind();
        if (active)
        {
            UpdateOutlineLayout();
            TextFormatter.SetSyncEnabled(split && !_busy);
            TextFormatter.SetViewMode(hasPreview ? (int)mode : 0);
            TextFormatter.SetSettingsVisibility(document is not null && !image && !pdf && !binary, hasPreview);
            BottomStatusBar.SetImageZoom(image ? pane.ImageViewer.ZoomPercent : null);
            BottomStatusBar.SetImageCursor(image ? pane.ImageViewer.CursorPixel : null);
            BottomStatusBar.SetPdfPage(pdf && document!.Pdf is { Error: PdfError.None } ? (pane.PdfViewer.CurrentPage, pane.PdfViewer.PageCount) : null);
            // Item 91: the image tab takes the keyboard (its H/Z/R/Space/Esc tools) unless focus is elsewhere, e.g. the explorer.
            if (image && ImageViewerShouldTakeFocus(pane)) pane.ImageViewer.Focus();
            if (pdf && ImageViewerShouldTakeFocus(pane)) pane.PdfViewer.Focus();
        }
    }

    private void RefreshPaneDocuments()
    {
        foreach (var pane in _panes)
        {
            pane.MdEditor.ShowDocument(pane.Active?.HasPreview == true ? pane.Active : null);
            UpdateContentLayout(pane);
        }
    }

    // Item 37: image zoom (Ctrl+wheel over the image, Reset zoom shows 100%).
    internal bool ZoomImage(Avalonia.Input.PointerWheelEventArgs e)
    {
        var viewer = _panes.Select(p => p.ImageViewer).FirstOrDefault(v => v.IsVisible && v.IsPointerOver);
        return viewer is not null && viewer.ZoomAt(e.Delta.Y, e.GetPosition(viewer));
    }
    internal bool ZoomPdf(Avalonia.Input.PointerWheelEventArgs e)
    {
        var viewer = _panes.Select(p => p.PdfViewer).FirstOrDefault(v => v.IsVisible && v.IsPointerOver);
        if (viewer is null) return false;
        viewer.StepZoom(e.Delta.Y > 0 ? 1 : -1);
        return true;
    }
    // Focus moves to the image only from nowhere or from the pane's (now hidden) editor or preview.
    private bool ImageViewerShouldTakeFocus(Mdhub.Components.EditorPane pane)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Avalonia.Visual focused || !focused.IsEffectivelyVisible) return true;
        bool Within(Avalonia.Visual host) => host == focused || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(host, focused);
        return Within(pane.TextEditor) || Within(pane.MdEditor);
    }
    internal bool ResetImageZoom() => ImageViewer.IsVisible && ImageViewer.FitToWindow();
    // Item 91: Ctrl+= / Ctrl+- step the active image's zoom, Ctrl+0 fits it to the window.
    internal bool ImageZoomKey(string id)
    {
        if (PdfViewer.IsVisible) return id switch
        {
            "zoom.in" => PdfViewer.StepZoom(1),
            "zoom.out" => PdfViewer.StepZoom(-1),
            "zoom.reset" => PdfViewer.FitToWindow(),
            _ => false
        };
        return ImageViewer.IsVisible && id switch
        {
            "zoom.in" => ImageViewer.StepZoom(1),
            "zoom.out" => ImageViewer.StepZoom(-1),
            "zoom.reset" => ImageViewer.FitToWindow(),
            _ => false
        };
    }
}
