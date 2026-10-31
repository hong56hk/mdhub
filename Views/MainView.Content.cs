using Avalonia.Controls;

namespace Mdhub.Views;

public partial class MainView
{
    private bool _showPreview = true;

    private void UpdateContentLayout()
    {
        var markdown = _active?.IsMarkdown == true;
        var image = _active?.IsImage == true;
        var binary = _active?.IsBinary == true;
        var preview = markdown && _showPreview;
        if (MdEditor.IsVisible && !preview) _previewWidth = Workspace.ColumnDefinitions[4].Width;
        MdEditor.IsVisible = PreviewSplitter.IsVisible = preview;
        TextFormatter.SetPreviewOn(preview);
        TextFormatter.SetSettingsVisibility(_active is not null && !image && !binary, markdown);
        Workspace.ColumnDefinitions[3].Width = new GridLength(preview ? 4 : 0);
        Workspace.ColumnDefinitions[4].Width = preview ? _previewWidth : new GridLength(0);
        Grid.SetColumnSpan(TextEditor, preview ? 1 : 3);
        TextEditor.IsVisible = !image && !binary;
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
