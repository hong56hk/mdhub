using Avalonia.Controls;

namespace Mdhub.Views;

public partial class MainView
{
    private bool _showPreview = true;

    private void UpdateContentLayout()
    {
        var markdown = _active?.IsMarkdown == true;
        var image = _active?.IsImage == true;
        var preview = markdown && _showPreview;
        if (MdEditor.IsVisible && !preview) _previewWidth = Workspace.ColumnDefinitions[4].Width;
        MdEditor.IsVisible = PreviewSplitter.IsVisible = preview;
        Workspace.ColumnDefinitions[3].Width = new GridLength(preview ? 4 : 0);
        Workspace.ColumnDefinitions[4].Width = preview ? _previewWidth : new GridLength(0);
        Grid.SetColumnSpan(TextEditor, preview ? 1 : 3);
        TextEditor.IsVisible = !image;
        ImageViewer.IsVisible = image;
        ImageViewer.ShowImage(_active?.Image);
        if (image) TextEditor.CloseFind();
    }
}
