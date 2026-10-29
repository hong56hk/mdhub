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
            case WorkspaceAction.Shortcuts: await RunAsync(() => DialogService.ShowShortcutsAsync(Host)); return;
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

    private async void Edit(string action)
    {
        if (_busy || _active?.IsImage == true) return;
        if (action == "paste") await PasteAsync();
        else TextEditor.Edit(action);
    }
    private async Task LoadRootAsync(string path)
    {
        await FolderExplorer.LoadRootAsync(path);
        GlobalSearch.SetFolder(path);
    }

    private async Task OpenFolderAsync()
    {
        var path = await (FolderPickerOverride ?? PickFolderAsync)();
        if (path is null) return;
        if (FolderExplorer.OpenedFolder is { } current)
        {
            if (SameFolder(current, path)) { ActivationCount++; Host.Activate(); return; }
            // A folder is already open here: open the new one in its own window and leave this one as it is.
            var window = (WindowFactory ?? (() => new MainWindow()))();
            window.Show();
            await window.MainView.OpenFolderPathAsync(path);
            return;
        }
        await OpenFolderPathAsync(path);
    }

    public async Task OpenFolderPathAsync(string path)
    {
        await LoadRootAsync(path);
        if (!Sidebar.IsVisible) ToggleFolders_Click(null, new RoutedEventArgs());
    }

    private static bool SameFolder(string a, string b)
    {
        static string Normalize(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        return string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    // Test seams: window creation for Open Folder, and how often the same folder re-activated this window.
    internal static Func<MainWindow>? WindowFactory { get; set; }
    internal int ActivationCount { get; private set; }

    // Test seam: headless UI tests replace the system folder picker.
    internal Func<Task<string?>>? FolderPickerOverride { get; set; }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open notes folder", AllowMultiple = false });
        if (folders.Count == 0) return null;
        using var folder = folders[0];
        return folder.TryGetLocalPath() ?? throw new IOException("Choose a local folder.");
    }

    private void ToggleFolders_Click(object? sender, RoutedEventArgs e)
    {
        var visible = !Sidebar.IsVisible;
        if (!visible) _folderWidth = Workspace.ColumnDefinitions[0].Width;
        Sidebar.IsVisible = FolderSplitter.IsVisible = visible;
        Workspace.ColumnDefinitions[0].Width = visible ? _folderWidth : new GridLength(0);
        Workspace.ColumnDefinitions[1].Width = new GridLength(visible ? 4 : 0);
    }

    private void TogglePreview_Click(object? sender, RoutedEventArgs e)
    {
        if (_active?.IsMarkdown != true) return;
        _showPreview = !_showPreview;
        UpdateContentLayout();
    }

    private void WordWrap_Click(object? sender, RoutedEventArgs e)
    {
        _wrap = !_wrap;
        foreach (var doc in _documents) doc.Editor.TextWrapping = _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    // Ctrl+Shift+E/F/H. MainWindow calls this from a window-level tunnelled KeyDown, so it works whatever has
    // focus, including nothing (focus on the window itself, outside MainView); OnShortcut calls it as a fallback.
    internal bool HandleSidebarShortcut(KeyEventArgs e)
    {
        if (e.Handled || _busy || FolderExplorer.IsRenaming) return false;
        if (ShortcutKeys.Match(e, s => s.Id is "sidebar.explorer" or "sidebar.search" or "sidebar.replace") is not { } sidebar) return false;
        e.Handled = true;
        ShowSidebar(sidebar != "sidebar.explorer", sidebar == "sidebar.replace");
        return true;
    }

    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (FolderExplorer.IsRenaming) return;
        if (HandleSidebarShortcut(e)) return;
        if (!_busy && ShortcutKeys.Match(e, s => s.Id == "explorer.rename") is not null)
        {
            e.Handled = true;
            FolderExplorer.BeginRename();
            return;
        }
        if (TextEditor.IsFindOpen && ShortcutKeys.Match(e, s => s.Id is "find.next" or "find.previous" or "find.close") is { } findId)
        {
            e.Handled = true;
            if (findId == "find.close") TextEditor.CloseFind();
            else if (findId == "find.previous") TextEditor.FindPrevious();
            else TextEditor.FindNext();
            return;
        }
        // Focus in the preview (not its edit box): undo/redo act on the note's source.
        if (MdEditor.IsKeyboardFocusWithin && !MdEditor.IsEditingPreview && _active is { } undoDoc && ShortcutKeys.Match(e, s => s.Id is "edit.undo" or "edit.redo") is { } undoId)
        {
            e.Handled = true;
            ShortcutFired?.Invoke(undoId);
            if (undoId == "edit.undo") undoDoc.Editor.Undo(); else undoDoc.Editor.Redo();
            return;
        }
        // Built-in TextBox keys and zoom (MainWindow) are listed in the table but handled elsewhere.
        var id = ShortcutKeys.Match(e, s => !s.HandledByTextBox && s.Area != ShortcutArea.Zoom && s.Id is not ("find.next" or "find.previous" or "find.close"));
        Func<Task>? action = id switch
        {
            "file.new" => () => { AddDocument(null, ""); return Task.CompletedTask; },
            "file.open" => OpenAsync,
            "file.openFolder" => OpenFolderAsync,
            "file.save" => async () => { if (_active is not null) await SaveAsync(_active, false); },
            "file.saveAs" => async () => { if (_active is not null) await SaveAsync(_active, true); },
            "file.closeTab" => async () => { if (_active is not null) await CloseDocumentAsync(_active); },
            "find.find" when _active?.IsImage != true => () => { TextEditor.OpenFind(false); return Task.CompletedTask; },
            "find.replace" when _active?.IsImage != true => () => { TextEditor.OpenFind(true); return Task.CompletedTask; },
            "edit.paste" when _active?.Editor.IsKeyboardFocusWithin == true => PasteAsync,
            "view.wordWrap" => () => { RequestAction(WorkspaceAction.WordWrap); return Task.CompletedTask; },
            "view.togglePreview" => () => { RequestAction(WorkspaceAction.TogglePreview); return Task.CompletedTask; },
            // Formatting and insert time act on the focused editor only; go to line / preview work anywhere but the find fields.
            "editor.bold" or "editor.italic" or "editor.link" or "editor.quote" when EditorFocused && _active!.IsMarkdown => EditorCommand(id),
            "editor.insertTime" when EditorFocused => EditorCommand(id),
            "editor.goToLine" or "view.previewCaret" when _active is { IsImage: false } && !TextEditor.IsFindFocused => EditorCommand(id),
            _ => null
        };
        if (action is null) return;
        e.Handled = true;
        ShortcutFired?.Invoke(id!);
        if (id == "edit.paste") await PasteAsync(); // runs busy only while saving an image
        else if (id is "find.find" or "find.replace" or "view.wordWrap" or "view.togglePreview" || id!.StartsWith("editor.") && id != "editor.goToLine" || id == "view.previewCaret") await action(); // find: focus must land in the find bar
        else await RunAsync(action);
    }

    private bool EditorFocused => _active?.Editor.IsKeyboardFocusWithin == true;

    // Clock for Ctrl+; (tests replace it).
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    // Menu items carrying one of these ids (not focus-scoped: the menu has focus when clicked).
    private async void RunMenuCommand(string id)
    {
        if (_busy || _active is null || _active.IsImage || EditorCommand(id) is not { } action) return;
        if (id == "editor.goToLine") await RunAsync(action); else await action();
    }

    private Func<Task>? EditorCommand(string id) => id switch
    {
        "editor.bold" => Sync(() => TextEditor.Format("bold")),
        "editor.italic" => Sync(() => TextEditor.Format("italic")),
        "editor.link" => Sync(() => TextEditor.Format("link")),
        "editor.quote" => Sync(() => TextEditor.Format("quote")),
        "editor.insertTime" => Sync(InsertTime),
        "editor.goToLine" => GoToLineAsync,
        "view.previewCaret" => Sync(() => { if (_active is { } d) MdEditor.CenterPreviewOnLine(d.Editor.CaretIndex); }),
        _ => null
    };
    private static Func<Task> Sync(Action a) => () => { a(); return Task.CompletedTask; };

    // Replaces the selection with the local time as a single edit (one undo step).
    private void InsertTime()
    {
        if (_active is not { } document) return;
        var editor = document.Editor;
        editor.SelectedText = Mdhub.Text.EditorCommands.Timestamp(Clock());
        editor.Focus();
    }

    private async Task GoToLineAsync()
    {
        if (_active is not { } document) return;
        var editor = document.Editor;
        var text = editor.Text ?? "";
        var count = Mdhub.Text.EditorCommands.LineCount(text);
        var current = Mdhub.Text.EditorCommands.LineCount(text[..Math.Clamp(editor.CaretIndex, 0, text.Length)]);
        if (await DialogService.PromptLineAsync(Host, count, current) is not { } line || document != _active) return;
        var start = Mdhub.Text.EditorCommands.LineStart(text, line);
        editor.Focus();
        editor.SelectionStart = editor.SelectionEnd = start;
        editor.CaretIndex = start;
        if (editor.TextWrapping == TextWrapping.NoWrap) editor.ScrollToLine(line - 1);
        GoToLineCompleted?.Invoke(line);
        MdEditor.CenterPreviewOnLine(start);
    }
    internal event Action<int>? GoToLineCompleted;

    // Raised once per handled shortcut (tests count it to prove nothing double-fires).
    internal event Action<string>? ShortcutFired;

    private async void Guide_Click(object? sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        AddDocument(null, "# Welcome to mdhub\n\nYour notes, on your computer.\n\n## Make it yours\n\nOpen a folder from **File → Open Folder**, then select a note. Each file opens in its own tab.\n\n- Write Markdown in the editor\n- See the formatted preview beside it\n- Save with Ctrl+S (Command+S on Mac)\n\n> The toolbar inserts Markdown into your source text.\n\n### Formatting\n\nTry **bold**, *italic*, ~~strikethrough~~, and `inline code`.\n\n| Feature | Shortcut |\n| --- | --- |\n| Open file | Ctrl+O |\n| Open folder | Ctrl+Shift+O |\n| Save | Ctrl+S |\n| Close tab | Ctrl+W |\n\n```csharp\nConsole.WriteLine(\"Hello world!\");\n```\n", "Markdown guide.md");
        return Task.CompletedTask;
    });

    private async void About_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
        await DialogService.ShowAsync(Host, "About mdhub", "mdhub · Your notes, locally.\n\nAn offline text and Markdown workspace built with C#, Avalonia, and Markdig.\n\nPreview supports common Markdown. Images are shown as labels; HTML, math, and Mermaid are not executed or rendered.", "OK"));

}
