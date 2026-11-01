using Avalonia.Controls;

namespace Mdhub.Views;

public partial class MainView
{
    private enum ViewMode { Editor, Split, Viewer }
    private ViewMode _viewMode = ViewMode.Split;

    private void UpdateContentLayout()
    {
        var hasPreview = _active?.HasPreview == true;
        var image = _active?.IsImage == true;
        var binary = _active?.IsBinary == true;
        var preview = hasPreview && _viewMode != ViewMode.Editor;
        var split = preview && _viewMode == ViewMode.Split;
        if (PreviewSplitter.IsVisible && !split) _previewWidth = Workspace.ColumnDefinitions[4].Width;
        MdEditor.IsVisible = preview;
        PreviewSplitter.IsVisible = split;
        TextFormatter.SetViewMode(hasPreview ? (int)_viewMode : 0);
        TextFormatter.SetSettingsVisibility(_active is not null && !image && !binary, hasPreview);
        Workspace.ColumnDefinitions[3].Width = new GridLength(split ? 4 : 0);
        Workspace.ColumnDefinitions[4].Width = split ? _previewWidth : new GridLength(0);
        Grid.SetColumnSpan(TextEditor, preview ? 1 : 3);
        Grid.SetColumn(MdEditor, split ? 4 : 2);
        Grid.SetColumnSpan(MdEditor, split ? 1 : 3);
        TextEditor.IsVisible = !image && !binary && (!hasPreview || _viewMode != ViewMode.Viewer);
        BinaryPlaceholder.IsVisible = binary;
        BinaryPlaceholder.ShowFile(binary ? _active!.Path : null);
        ImageViewer.IsVisible = image;
        ImageViewer.ShowImage(_active?.Image);
        if (image || binary) TextEditor.CloseFind();
        BottomStatusBar.SetImageZoom(image ? ImageViewer.ZoomPercent : null);
    }

    // Item 37: image zoom (Ctrl+wheel over the image, Reset zoom shows 100%).
    internal bool ZoomImage(Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!ImageViewer.IsVisible || !ImageViewer.IsPointerOver) return false;
        return ImageViewer.ZoomAt(e.Delta.Y, e.GetPosition(ImageViewer));
    }
    internal bool ResetImageZoom() => ImageViewer.IsVisible && ImageViewer.ResetZoom();
}
