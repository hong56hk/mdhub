using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Composes the workspace and coordinates actions across components. No component knows this view.
public partial class MainView : UserControl
{
    private readonly ObservableCollection<NoteDocument> _documents = [];
    private NoteDocument? _active;
    private bool _busy, _allowClose, _wrap;
    private int _untitled;
    private GridLength _folderWidth = new(240), _previewWidth = new(1, GridUnitType.Star);
    private Window Host => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The main view must be hosted in a window.");
    public event Action<string>? TitleChanged;
    public event Action? CloseRequested;
    public event Action<WindowAction>? WindowActionRequested;
    public string DocumentTitle => $"{_active?.Title ?? "mdhub"} — mdhub";

    public MainView()
    {
        InitializeComponent();
        FileTabs.SetDocuments(_documents);
        FileTabs.DocumentSelected += SelectDocument;
        FileTabs.NewRequested += () => RequestAction(WorkspaceAction.New);
        FileTabs.CloseRequested += async document => await RunAsync(() => CloseDocumentAsync(document));
        TopMenu.ActionRequested += RequestAction;
        TopMenu.WindowActionRequested += action => WindowActionRequested?.Invoke(action);
        TopMenu.EditRequested += Edit;
        TextFormatter.ActionRequested += RequestAction;
        TextFormatter.EditRequested += Edit;
        TextFormatter.FormatRequested += action => { if (!_busy) TextEditor.Format(action); };
        FolderExplorer.FileRequested += async path => await RunAsync(() => OpenPathAsync(path));
        FolderExplorer.RefreshRequested += () => RequestAction(WorkspaceAction.RefreshFolder);
        FolderExplorer.CreateRequested += async isFolder => await RunAsync(() => CreateExplorerItemAsync(isFolder));
        FolderExplorer.MessageChanged += BottomStatusBar.SetMessage;
        MdEditor.StatisticsChanged += BottomStatusBar.UpdateStatistics;
        MdEditor.MessageChanged += BottomStatusBar.SetMessage;
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        AddDocument(null, "", "Untitled.md");
    }

    public void FocusEditor() => TextEditor.FocusEditor();
    public void StopPreview() => MdEditor.Stop();
    public void UpdateWindowState(WindowState state) => TopMenu.UpdateWindowState(state);

    private void SelectDocument(NoteDocument document)
    {
        _active = document;
        TextEditor.ShowDocument(document);
        MdEditor.ShowDocument(document);
        UpdateStatus();
        TextEditor.FocusEditor();
    }

    private void UpdateStatus(string? message = null)
    {
        if (_active is null) return;
        _active.Refresh();
        TitleChanged?.Invoke(DocumentTitle);
        BottomStatusBar.UpdateDocument(_active, message);
        TextEditor.UpdateFileKind(_active);
        TextFormatter.SetFormattingEnabled(_active.IsMarkdown && !_busy);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        SetBusy(true);
        try { await action(); }
        catch (Exception ex)
        {
            UpdateStatus("Operation failed; your open notes are still available.");
            await DialogService.ShowAsync(Host, "Unable to complete operation", ex.Message, "OK");
        }
        finally { SetBusy(false); TextEditor.FocusEditor(); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FileTabs.SetBusy(busy);
        TopMenu.IsEnabled = !busy;
        FolderExplorer.SetBusy(busy);
        TextFormatter.SetFormattingEnabled(!busy && _active?.IsMarkdown == true);
        foreach (var document in _documents) document.Editor.IsReadOnly = busy;
    }
    public async void HandleClosing(WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_busy) { e.Cancel = true; return; }
        if (!_documents.Any(d => d.IsDirty)) { MdEditor.Stop(); return; }
        e.Cancel = true;
        await RunAsync(async () =>
        {
            foreach (var document in _documents.ToArray())
                if (!await ConfirmCloseAsync(document)) return;
            _allowClose = true;
            MdEditor.Stop();
            CloseRequested?.Invoke();
        });
    }
}
