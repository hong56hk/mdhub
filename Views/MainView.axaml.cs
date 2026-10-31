using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;

using Mdhub.Localization;

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

    private void ApplyEditorFonts()
    {
        foreach (var document in _documents) document.Editor.FontFamily = Services.EditorFonts.BodyFamily;
    }
    public MainView()
    {
        InitializeComponent();
        MdEditor.ContextMenu = CreateEditorMenu(null); // item 41: Reveal in Side Bar from the preview
        FileTabs.SetDocuments(_documents);
        // Code-set status texts follow a language switch (XAML texts use DynamicResource).
        AttachedToVisualTree += (_, _) => Loc.Changed += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => Loc.Changed -= OnLanguageChanged;
        WireQuickOpen();
        FileTabs.DocumentSelected += SelectDocument;
        FileTabs.NewRequested += () => RequestAction(WorkspaceAction.New);
        FileTabs.CloseRequested += async document => await RunAsync(() => CloseDocumentAsync(document));
        FileTabs.TabMenu = CreateTabMenu(); // item 75
        TopMenu.ActionRequested += RequestAction;
        TopMenu.WindowActionRequested += action => WindowActionRequested?.Invoke(action);
        TopMenu.EditRequested += Edit;
        TopMenu.CommandRequested += RunMenuCommand;
        TopMenu.RecentRequested += (path, folder) => _ = RunAsync(() => OpenRecentAsync(path, folder));
        TextFormatter.ActionRequested += RequestAction;
        TextFormatter.EditRequested += Edit;
        TextFormatter.FormatRequested += action => { if (!_busy) TextEditor.Format(action); };
        // VS Code: selecting a file opens it but leaves focus in the explorer; Enter or double-click moves focus to the editor.
        FolderExplorer.FileRequested += async path => await RunAsync(() => OpenPathAsync(path), focusEditor: false);
        FolderExplorer.FileActivated += async path => { _focusEditorAfterBusy = true; await RunAsync(() => OpenPathAsync(path)); };
        FolderExplorer.RefreshRequested += () => RequestAction(WorkspaceAction.RefreshFolder);
        WireBinaryPlaceholder();
        WireEncodingMenu();
        WireLineEndingMenu(); // item 68
        FolderExplorer.ExplorerRequested += async request => { if (!_busy) await RunAsync(() => HandleExplorerRequestAsync(request)); };
        FolderExplorer.MoveRequested += async request => await RunAsync(() => MoveExplorerFileAsync(request));
        FolderExplorer.RenameRequested = RenameExplorerItemAsync;
        GlobalSearch.OpenBuffers = () => _documents.Where(d => d.Path is not null && !d.IsViewOnly)
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
        MdEditor.StatisticsChanged += BottomStatusBar.UpdateStatistics;
        MdEditor.MessageChanged += BottomStatusBar.SetMessage;
        TextEditor.SpellCountChanged += BottomStatusBar.SetSpellCount; // item 74
        ImageViewer.ZoomChanged += percent => { if (ImageViewer.IsVisible) BottomStatusBar.SetImageZoom(percent); };
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        // Focus moving into or out of the preview shows or hides the caret position.
        AddHandler(GotFocusEvent, (_, _) => UpdateCaretStatus(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateCaretStatus), RoutingStrategies.Bubble, handledEventsToo: true);
        AddDocument(null, "", "Untitled.md");
    }

    public void FocusEditor() => TextEditor.FocusEditor();
    public void StopPreview()
    {
        MdEditor.Stop();
        ImageViewer.ShowImage(null);
        foreach (var document in _documents) document.Image?.Dispose();
    }
    public void UpdateWindowState(WindowState state) => TopMenu.UpdateWindowState(state);

    // Shown in the status bar after a restore until the user switches tabs or edits.
    private string? _sessionNote;

    private void SelectDocument(NoteDocument document)
    {
        _sessionNote = null;
        _active = document;
        FolderExplorer.MarkActive(document.Path);
        TextEditor.ShowDocument(document);
        MdEditor.ShowDocument(document.IsMarkdown ? document : null);
        UpdateContentLayout();
        UpdateStatus();
        TextEditor.FocusEditor();
        document.RestoreSelection();
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
        if (!_active.IsMarkdown && !_active.IsViewOnly) BottomStatusBar.UpdateStatistics(Mdhub.Text.TextStatistics.Count(_active.Text));
        TextFormatter.SetFormattingEnabled(_active.IsMarkdown && !_busy);
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
            if (focusEditor || _focusEditorAfterBusy) { _focusEditorAfterBusy = false; TextEditor.FocusEditor(); }
            else if (explorerFocused && !FolderExplorer.IsKeyboardFocusWithin) FolderExplorer.FocusTree();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FileTabs.SetBusy(busy);
        TopMenu.IsEnabled = !busy;
        FolderExplorer.SetBusy(busy);
        TextFormatter.SetFormattingEnabled(!busy && _active?.IsMarkdown == true);
        foreach (var document in _documents) document.Editor.IsReadOnly = busy || document.IsViewOnly || document.IsReadOnlyText;
    }
    // Saved file tabs only: untitled buffers and unsaved text are never written to the session.
    public WindowSession CaptureSession()
    {
        var saved = _documents.Where(d => d.Path is not null).ToList();
        return new WindowSession
        {
            Folder = FolderExplorer.OpenedFolder,
            Tabs = saved.Select(d => d.Path!).ToList(),
            ActiveTab = _active is null ? -1 : saved.IndexOf(_active)
        };
    }

    public async Task RestoreSessionAsync(WindowSession session, string? note)
    {
        var failed = 0;
        var placeholder = _documents.Count == 1 && _documents[0].Path is null && !_documents[0].IsDirty && _documents[0].Text.Length == 0 ? _documents[0] : null;
        if (session.Folder is { } folder)
        {
            try { await LoadRootAsync(folder); } catch (Exception) { failed++; }
        }
        var opened = new List<NoteDocument>();
        foreach (var tab in session.Tabs)
        {
            try
            {
                _restoringTabs = true;
                try { await OpenPathAsync(tab); } finally { _restoringTabs = false; }
                if (_active is { Path: { } openedPath } document && SamePath(openedPath, Path.GetFullPath(tab)))
                {
                    if (!opened.Contains(document)) opened.Add(document);
                }
                else failed++;
            }
            catch (Exception) { failed++; }
        }
        if (opened.Count > 0 && placeholder is not null && _documents.Contains(placeholder)) await CloseDocumentAsync(placeholder);
        if (session.ActiveTab >= 0 && session.ActiveTab < session.Tabs.Count
            && _documents.FirstOrDefault(d => d.Path is { } p && SamePath(p, Path.GetFullPath(session.Tabs[session.ActiveTab]))) is { } active)
            FileTabs.SelectedDocument = active;
        if (failed > 0) note = $"{note} {Loc.Plural("ReopenFailedOne", "ReopenFailedMany", failed)}".Trim();
        _sessionNote = note;
        UpdateStatus();
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
