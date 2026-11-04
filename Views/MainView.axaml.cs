using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

// Composes the workspace and coordinates actions across components. No component knows this view.
public partial class MainView : UserControl
{
    private ObservableCollection<NoteDocument> _documents = [];
    private NoteDocument? _active;
    private bool _busy, _allowClose, _wrap;
    private int _untitled;
    private bool _syncFromPreview;
    private GridLength _folderWidth = new(240);
    private Window Host => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The main view must be hosted in a window.");
    public event Action<string>? TitleChanged;
    public event Action? CloseRequested;
    public event Action<WindowAction>? WindowActionRequested;
    public string DocumentTitle => $"{_active?.Title ?? "mdhub"} — mdhub";

    private void ApplyEditorFonts()
    {
        foreach (var document in AllDocuments) document.Editor.FontFamily = Services.EditorFonts.BodyFamily;
    }
    public MainView()
    {
        InitializeComponent();
        InitializePanes();
        InitializeRecovery();
        // Code-set status texts follow a language switch (XAML texts use DynamicResource).
        AttachedToVisualTree += (_, _) => Loc.Changed += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => Loc.Changed -= OnLanguageChanged;
        WireQuickOpen();
        Outline.CloseRequested += ToggleOutline;
        Outline.HeadingRequested += NavigateOutline;
        TopMenu.ActionRequested += RequestAction;
        TopMenu.WindowActionRequested += action => WindowActionRequested?.Invoke(action);
        TopMenu.EditRequested += Edit;
        TopMenu.CommandRequested += RunMenuCommand;
        TopMenu.RecentRequested += (path, folder) => _ = RunAsync(() => OpenRecentAsync(path, folder));
        TextFormatter.ActionRequested += RequestAction;
        TextFormatter.EditRequested += Edit;
        TextFormatter.CommandRequested += RunMenuCommand;
        TextFormatter.FormatRequested += action => { if (!_busy) TextEditor.Format(action); };
        // VS Code: selecting a file opens it but leaves focus in the explorer; Enter or double-click moves focus to the editor.
        FolderExplorer.FileRequested += async path => await RunAsync(() => OpenPathAsync(path), focusEditor: false);
        FolderExplorer.FileActivated += async path => { _focusEditorAfterBusy = true; await RunAsync(() => OpenPathAsync(path)); };
        FolderExplorer.RefreshRequested += () => RequestAction(WorkspaceAction.RefreshFolder);
        FolderExplorer.OpenToSideRequested += async path => { if (!_busy) await RunAsync(() => OpenToSideAsync(path)); };
        FolderExplorer.DefaultAppRequested += async path => await OpenWithDefaultAppAsync(path);
        WireEncodingMenu();
        TextFormatter.EmojiRequested += ShowEmojiPicker;
        WireLineEndingMenu(); // item 68
        FolderExplorer.ExplorerRequested += async request => { if (!_busy) await RunAsync(() => HandleExplorerRequestAsync(request)); };
        FolderExplorer.MoveRequested += async request => await RunAsync(() => MoveExplorerFileAsync(request));
        FolderExplorer.ImportRequested += async request => await RunAsync(() => ImportExplorerFilesAsync(request), focusEditor: false);
        FolderExplorer.FilesActivated += async paths => await RunAsync(() => OpenManyAsync(paths));
        FolderExplorer.RenameRequested = RenameExplorerItemAsync;
        GlobalSearch.OpenBuffers = () => UniqueDocuments.Where(d => d.Path is not null && !d.IsViewOnly)
            .ToDictionary(d => d.Path!, d => d.Text, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        GlobalSearch.HitRequested += async (file, hit) => await RunAsync(() => OpenSearchHitAsync(file, hit));
        GlobalSearch.ReplaceRequested = ReplaceSearchResultsAsync;
        // Item 57: the editor gear's fonts apply live to every open editor (the presenters re-lay out code spans).
        AttachedToVisualTree += (_, _) => Services.EditorFonts.Changed += ApplyEditorFonts;
        DetachedFromVisualTree += (_, _) => Services.EditorFonts.Changed -= ApplyEditorFonts;
        FolderExplorer.ExternalChangeRefreshed += () => { if (GlobalSearch.IsVisible) GlobalSearch.Schedule(); };
        FolderExplorer.CanPaste = () => ExplorerClipboard is not null;
        FolderExplorer.OpenFolderRequested += () => RequestAction(WorkspaceAction.OpenFolder);
        FolderExplorer.CreateRequested += async isFolder => await RunAsync(() => CreateExplorerItemAsync(isFolder));
        FolderExplorer.MessageChanged += BottomStatusBar.SetMessage;
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        // Focus moving into or out of the preview shows or hides the caret position.
        AddHandler(GotFocusEvent, (_, _) => UpdateCaretStatus(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Visual visual && (visual == MdEditor || visual.GetVisualAncestors().Contains(MdEditor))) _syncFromPreview = true;
            else if (e.Source == _active?.Editor) _syncFromPreview = false;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateCaretStatus), RoutingStrategies.Bubble, handledEventsToo: true);
        _placeholderBuffer = AddDocument(null, "", "Untitled.md").Buffer;
    }

    public void FocusEditor()
    {
        if (MdEditor.IsVisible && !TextEditor.IsVisible) MdEditor.FocusPreview();
        else TextEditor.FocusEditor();
    }
    public void StopPreview()
    {
        _stopped = true;
        _recoveryTimer.Stop();
        foreach (var buffer in _recoveryBuffers) buffer.Changed -= OnRecoveryChanged;
        _recoveryBuffers.Clear();
        foreach (var pane in _panes) { pane.MdEditor.Stop(); pane.ImageViewer.ShowImage(null); }
        _outlineTimer?.Stop();
        foreach (var document in UniqueDocuments) document.Image?.Dispose();
        foreach (var document in AllDocuments) document.Dispose();
    }
    public void UpdateWindowState(WindowState state) => TopMenu.UpdateWindowState(state);

    // Shown in the status bar after a restore until the user switches tabs or edits.
    private string? _sessionNote;

    private void SelectDocument(NoteDocument document)
    {
        _ = FlushRecoveryAsync();
        _sessionNote = null;
        _active = document;
        _activePane.Active = document;
        _syncFromPreview = false;
        FolderExplorer.MarkActive(document.Path);
        TextEditor.ShowDocument(document);
        MdEditor.ShowDocument(document.HasPreview ? document : null);
        UpdateContentLayout();
        UpdateStatus();
        FocusEditor();
        document.RestoreSelection();
        ScheduleExternalCheck();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_active == document) document.RestoreSelection(); });
    }

    private Avalonia.Threading.DispatcherTimer? _transientTimer;
    internal string? TransientStatus { get; private set; }
    internal static TimeSpan TransientStatusDuration { get; set; } = TimeSpan.FromSeconds(1.5);
    // Shows a short message (e.g. "Zoom 120%") that clears itself.
    public void ShowTransientStatus(string message)
    {
        TransientStatus = message;
        _transientTimer?.Stop();
        _transientTimer = new Avalonia.Threading.DispatcherTimer { Interval = TransientStatusDuration };
        _transientTimer.Tick += (_, _) => { _transientTimer!.Stop(); TransientStatus = null; UpdateStatus(); };
        _transientTimer.Start();
        UpdateStatus();
    }

    private void OnLanguageChanged()
    {
        if (_active is null) return;
        UpdateStatus();
        UpdateCaretStatus();
        BottomStatusBar.Relocalize();
    }
    private void UpdateStatus(string? message = null)
    {
        if (_active is null) return;
        _active.Refresh();
        TitleChanged?.Invoke(DocumentTitle);
        BottomStatusBar.UpdateDocument(_active, message ?? TransientStatus ?? _sessionNote);
        TextFormatter.UpdateInsertButtons(_active);
        if (!_active.IsMarkdown && !_active.IsViewOnly) BottomStatusBar.UpdateStatistics(Mdhub.Text.TextStatistics.Count(_active.Text));
        TextFormatter.SetFormattingEnabled(_active.HasPreview && !_busy, _active.IsMarkdown);
        UpdateCaretStatus();
    }

    // "Ln 12, Col 5 (9 selected)" for the active md/txt editor; hidden for images, no editor, or when the preview has focus.
    private void UpdateCaretStatus()
    {
        var previewFocused = MdEditor.PreviewPanel.IsKeyboardFocusWithin || MdEditor.IsEditingPreview;
        if (_active is not { IsViewOnly: false } document || !_documents.Contains(document) || previewFocused) { BottomStatusBar.SetCaret(null); return; }
        BottomStatusBar.SetCaret(Mdhub.Text.CaretPosition.Format(document.Text, document.Editor.SelectionStart, document.Editor.SelectionEnd));
    }

    // An Enter/double-click that arrives while the single-click open is still running still ends with the editor focused.
    private bool _focusEditorAfterBusy;
    private async Task RunAsync(Func<Task> action, bool focusEditor = true)
    {
        if (_busy) return;
        var explorerFocused = FolderExplorer.IsKeyboardFocusWithin;
        SetBusy(true);
        try { await action(); }
        catch (Exception ex)
        {
            UpdateStatus(Loc.T("OperationFailed"));
            await DialogService.ShowAsync(Host, Loc.T("UnableToComplete"), ex.Message, "OK");
        }
        finally
        {
            SetBusy(false);
            if (focusEditor || _focusEditorAfterBusy) { _focusEditorAfterBusy = false; FocusEditor(); }
            else if (explorerFocused && !FolderExplorer.IsKeyboardFocusWithin) FolderExplorer.FocusTree();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var pane in _panes) pane.FileTabs.SetBusy(busy);
        TopMenu.IsEnabled = !busy;
        FolderExplorer.SetBusy(busy);
        TextFormatter.SetFormattingEnabled(!busy && _active?.HasPreview == true, _active?.IsMarkdown == true);
        TextFormatter.SetSyncEnabled(!busy && _active?.HasPreview == true && _viewMode == ViewMode.Split);
        foreach (var document in AllDocuments) document.Editor.IsReadOnly = busy || document.IsViewOnly || document.IsReadOnlyText;
        if (_active is not null) TextFormatter.UpdateInsertButtons(_active);
    }
    // Saved file tabs only: untitled buffers and unsaved text are never written to the session.
    public WindowSession CaptureSession()
    {
        var saved = UniqueDocuments.Where(d => d.Path is not null).ToList();
        return new WindowSession
        {
            Folder = FolderExplorer.OpenedFolder,
            Tabs = saved.Select(d => d.Path!).ToList(),
            ActiveTab = _active is null ? -1 : saved.FindIndex(d => d.Buffer == _active.Buffer)
        };
    }

    public async Task RestoreSessionAsync(WindowSession session, string? note)
    {
        _restoringRecovery = true;
        var failed = 0;
        var placeholder = _documents.Count == 1 && _documents[0].Path is null && !_documents[0].IsDirty && _documents[0].Text.Length == 0 ? _documents[0] : null;
        if (session.Folder is { } folder)
        {
            try { await LoadRootAsync(folder); } catch (Exception) { failed++; }
        }
        var opened = new List<NoteDocument>();
        var openedByKey = new Dictionary<string, NoteDocument>();
        foreach (var tab in session.Tabs)
        {
            try
            {
                _restoringTabs = true;
                try
                {
                    if (session.Recovery.FirstOrDefault(r => r.Key == tab) is { } recovery) RestoreRecoveryDocument(recovery);
                    else await OpenPathAsync(tab);
                }
                finally { _restoringTabs = false; }
                if (_active is { } document && (session.Recovery.Any(r => r.Key == tab) || (document.Path is { } openedPath && SamePath(openedPath, Path.GetFullPath(tab)))))
                {
                    if (!opened.Contains(document)) opened.Add(document);
                    openedByKey[tab] = document;
                }
                else failed++;
            }
            catch (Exception) { failed++; }
        }
        if (opened.Count > 0 && placeholder is not null && _documents.Contains(placeholder)) await CloseDocumentAsync(placeholder);
        if (session.ActiveTab >= 0 && session.ActiveTab < session.Tabs.Count
            && openedByKey.TryGetValue(session.Tabs[session.ActiveTab], out var active))
            FileTabs.SelectedDocument = active;
        _restoringRecovery = false;
        ScheduleExternalCheck();
        if (failed > 0) note = $"{note} {Loc.Plural("ReopenFailedOne", "ReopenFailedMany", failed)}".Trim();
        _sessionNote = note;
        UpdateStatus();
    }

    public async void HandleClosing(WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_busy) { e.Cancel = true; return; }
        e.Cancel = true;
        await RunAsync(async () =>
        {
            if (SessionManager.Enabled)
            {
                if (!await FlushRecoveryAsync() && UniqueDocuments.Any(d => d.IsDirty
                    || (d.Path is null && !d.IsReadOnlyText && d.Text.Length > 0))) return;
            }
            else
            {
                foreach (var document in UniqueDocuments.ToArray())
                    if (!await ConfirmCloseAsync(document)) return;
            }
            _allowClose = true;
            CloseRequested?.Invoke();
        });
    }
}
