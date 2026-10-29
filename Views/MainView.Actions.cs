using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mdhub.Components;
using Mdhub.Services;

namespace Mdhub.Views;

// Routes menu/toolbar/keyboard requests; panel sizing belongs to this whole-view layout.
public partial class MainView
{
    private async void RequestAction(WorkspaceAction action)
    {
        if (_busy) return;
        switch (action)
        {
            case WorkspaceAction.Exit: CloseRequested?.Invoke(); return;
            case WorkspaceAction.ToggleFolders: ToggleFolders_Click(null, new RoutedEventArgs()); return;
            case WorkspaceAction.TogglePreview: TogglePreview_Click(null, new RoutedEventArgs()); return;
            case WorkspaceAction.WordWrap: WordWrap_Click(null, new RoutedEventArgs()); return;
            case WorkspaceAction.Guide: Guide_Click(null, new RoutedEventArgs()); return;
            case WorkspaceAction.About: About_Click(null, new RoutedEventArgs()); return;
        }
        await RunAsync(async () =>
        {
            switch (action)
            {
                case WorkspaceAction.New: AddDocument(null, ""); break;
                case WorkspaceAction.Open: await OpenAsync(); break;
                case WorkspaceAction.OpenFolder: await OpenFolderAsync(); break;
                case WorkspaceAction.Save when _active is not null: await SaveAsync(_active, false); break;
                case WorkspaceAction.SaveAs when _active is not null: await SaveAsync(_active, true); break;
                case WorkspaceAction.CloseTab when _active is not null: await CloseDocumentAsync(_active); break;
                case WorkspaceAction.RefreshFolder: await FolderExplorer.RefreshAsync(); break;
            }
        });
    }

    private void Edit(string action) { if (!_busy) TextEditor.Edit(action); }
    private Task LoadRootAsync(string path) => FolderExplorer.LoadRootAsync(path);

    private async Task OpenFolderAsync()
    {
        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open notes folder", AllowMultiple = false });
        if (folders.Count == 0) return;
        using var folder = folders[0];
        var path = folder.TryGetLocalPath() ?? throw new IOException("Choose a local folder.");
        await LoadRootAsync(path);
        if (!FolderExplorer.IsVisible) ToggleFolders_Click(null, new RoutedEventArgs());
    }

    private void ToggleFolders_Click(object? sender, RoutedEventArgs e)
    {
        var visible = !FolderExplorer.IsVisible;
        if (!visible) _folderWidth = Workspace.ColumnDefinitions[0].Width;
        FolderExplorer.IsVisible = FolderSplitter.IsVisible = visible;
        Workspace.ColumnDefinitions[0].Width = visible ? _folderWidth : new GridLength(0);
        Workspace.ColumnDefinitions[1].Width = new GridLength(visible ? 4 : 0);
    }

    private void TogglePreview_Click(object? sender, RoutedEventArgs e)
    {
        var visible = !MdEditor.IsVisible;
        if (!visible) _previewWidth = Workspace.ColumnDefinitions[4].Width;
        MdEditor.IsVisible = PreviewSplitter.IsVisible = visible;
        Workspace.ColumnDefinitions[4].Width = visible ? _previewWidth : new GridLength(0);
        Workspace.ColumnDefinitions[3].Width = new GridLength(visible ? 4 : 0);
    }

    private void WordWrap_Click(object? sender, RoutedEventArgs e)
    {
        _wrap = !_wrap;
        foreach (var doc in _documents) doc.Editor.TextWrapping = _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != modifier && e.KeyModifiers != (modifier | KeyModifiers.Shift)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Func<Task>? action = e.Key switch
        {
            Key.N => () => { AddDocument(null, ""); return Task.CompletedTask; },
            Key.O => shift ? OpenFolderAsync : OpenAsync,
            Key.S => async () => { if (_active is not null) await SaveAsync(_active, shift); },
            Key.W => async () => { if (_active is not null) await CloseDocumentAsync(_active); },
            _ => null
        };
        if (action is null) return;
        e.Handled = true;
        await RunAsync(action);
    }

    private async void Guide_Click(object? sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        AddDocument(null, "# Welcome to mdhub\n\nYour notes, on your computer.\n\n## Make it yours\n\nOpen a folder from **File → Open Folder**, then select a note. Each file opens in its own tab.\n\n- Write Markdown in the editor\n- See the formatted preview beside it\n- Save with Ctrl+S (Command+S on Mac)\n\n> The toolbar inserts Markdown into your source text.\n\n### Formatting\n\nTry **bold**, *italic*, ~~strikethrough~~, and `inline code`.\n\n| Feature | Shortcut |\n| --- | --- |\n| Open file | Ctrl+O |\n| Open folder | Ctrl+Shift+O |\n| Save | Ctrl+S |\n| Close tab | Ctrl+W |\n\n```csharp\nConsole.WriteLine(\"Hello world!\");\n```\n", "Markdown guide.md");
        return Task.CompletedTask;
    });

    private async void About_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
        await DialogService.ShowAsync(Host, "About mdhub", "mdhub · Your notes, locally.\n\nAn offline text and Markdown workspace built with C#, Avalonia, and Markdig.\n\nPreview supports common Markdown. Images are shown as labels; HTML, math, and Mermaid are not executed or rendered.", "OK"));

}
