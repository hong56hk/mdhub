using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    // Layout is a collection, rather than a primary/secondary pair. The product limit is independent of it.
    internal const int MaxEditorPanes = 5;
    private readonly List<EditorPane> _panes = [];
    private EditorPane _activePane = null!;
    internal IReadOnlyList<EditorPane> EditorPanes => _panes;
    internal EditorPane ActivePane => _activePane;
    private IEnumerable<NoteDocument> AllDocuments => _panes.SelectMany(p => p.Documents);
    private IEnumerable<NoteDocument> UniqueDocuments => AllDocuments.DistinctBy(d => d.Buffer);
    private FileTabs FileTabs => _activePane.FileTabs;
    private TextEditor TextEditor => _activePane.TextEditor;
    private MdEditor MdEditor => _activePane.MdEditor;
    private ImageViewer ImageViewer => _activePane.ImageViewer;
    private BinaryPlaceholder BinaryPlaceholder => _activePane.BinaryPlaceholder;

    private void InitializePanes()
    {
        _activePane = PrimaryPane;
        _documents = PrimaryPane.Documents;
        WirePane(PrimaryPane);
        _panes.Add(PrimaryPane);
        PrimaryPane.SetActive(true);
    }

    private void WirePane(EditorPane pane)
    {
        pane.MdEditor.ContextMenu = CreateEditorMenu(null);
        pane.MdEditor.ContextMenu.Opening += (_, _) => { if (!_busy) ActivatePane(pane); };
        pane.FileTabs.SetBusy(_busy);
        pane.AddHandler(PointerPressedEvent, (_, _) => { if (!_busy) ActivatePane(pane); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        pane.AddHandler(GotFocusEvent, (_, _) => { if (!_busy) ActivatePane(pane); }, RoutingStrategies.Bubble, handledEventsToo: true);
        pane.FileTabs.DocumentSelected += document => { ActivatePane(pane); SelectDocument(document); };
        pane.FileTabs.NewRequested += () => { ActivatePane(pane); RequestAction(WorkspaceAction.New); };
        pane.FileTabs.CloseRequested += async document => { if (_busy) return; ActivatePane(pane); await RunAsync(() => CloseDocumentAsync(document)); };
        pane.FileTabs.TabMenu = CreateTabMenu(pane);
        pane.MdEditor.StatisticsChanged += statistics => { if (pane == _activePane) BottomStatusBar.UpdateStatistics(statistics); };
        pane.MdEditor.MessageChanged += message => { if (pane == _activePane) BottomStatusBar.SetMessage(message); };
        pane.TextEditor.SpellCountChanged += count => { if (pane == _activePane) BottomStatusBar.SetSpellCount(count); };
        pane.ImageViewer.ZoomChanged += percent => { if (pane == _activePane && pane.ImageViewer.IsVisible) BottomStatusBar.SetImageZoom(percent); };
        pane.ImageViewer.CursorPixelChanged += pixel => { if (pane == _activePane && pane.ImageViewer.IsVisible) BottomStatusBar.SetImageCursor(pixel); };
        pane.BinaryPlaceholder.OpenDefaultRequested += async () => { if (pane.Active is { IsBinary: true, Path: { } path }) await OpenWithDefaultAppAsync(path); };
        pane.BinaryPlaceholder.OpenAsTextRequested += async () => { if (_busy) return; ActivatePane(pane); if (_active is { IsBinary: true } document) await RunAsync(() => OpenAsTextAsync(document)); };
    }

    private void ActivatePane(EditorPane pane)
    {
        if (_activePane == pane || !_panes.Contains(pane)) return;
        _activePane.Active = _active;
        _activePane.ViewMode = (int)_viewMode;
        _activePane.SetActive(false);
        _activePane = pane;
        _documents = pane.Documents;
        _active = pane.Active;
        _viewMode = (ViewMode)pane.ViewMode;
        _syncFromPreview = false;
        _ = FlushRecoveryAsync();
        ScheduleExternalCheck();
        pane.SetActive(true);
        if (_active is not null) FolderExplorer.MarkActive(_active.Path);
        UpdateContentLayout();
        UpdateStatus();
        BottomStatusBar.SetSpellCount(pane.TextEditor.Spelling.Count);
        if (_active?.IsMarkdown == true) BottomStatusBar.UpdateStatistics(Mdhub.Text.TextStatistics.Count(_active.Text));
    }

    private void FocusAdjacentPane(int step)
    {
        var index = _panes.IndexOf(_activePane) + step;
        if (index < 0 || index >= _panes.Count) return;
        ActivatePane(_panes[index]);
        FocusEditor();
    }

    private EditorPane? CreatePaneToSide()
    {
        if (_panes.Count >= MaxEditorPanes)
        {
            ShowTransientStatus(Loc.F("SplitLimitReached", MaxEditorPanes));
            return null;
        }
        var pane = new EditorPane { ViewMode = (int)ViewMode.Editor };
        WirePane(pane);
        _panes.Insert(_panes.IndexOf(_activePane) + 1, pane);
        RebuildPaneLayout();
        return pane;
    }

    private void RebuildPaneLayout()
    {
        // Keep existing controls attached: rebuilding the children would reset preview and focus state.
        foreach (var splitter in PaneHost.Children.OfType<GridSplitter>().ToArray()) PaneHost.Children.Remove(splitter);
        foreach (var removed in PaneHost.Children.OfType<EditorPane>().Where(p => !_panes.Contains(p)).ToArray()) PaneHost.Children.Remove(removed);
        PaneHost.ColumnDefinitions.Clear();
        for (var i = 0; i < _panes.Count; i++)
        {
            if (i > 0)
            {
                PaneHost.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4)));
                var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = ThemeService.Brush("BorderSubtle") };
                Grid.SetColumn(splitter, i * 2 - 1);
                PaneHost.Children.Add(splitter);
            }
            PaneHost.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetColumn(_panes[i], i * 2);
            if (!PaneHost.Children.Contains(_panes[i])) PaneHost.Children.Add(_panes[i]);
        }
    }

    internal async Task OpenToSideAsync(string? path = null)
    {
        var sourcePane = _activePane;
        var source = path is null ? _active : AllDocuments.FirstOrDefault(d => SamePath(d.Path, Path.GetFullPath(path)));
        if (source is null && path is null) return;
        var pane = CreatePaneToSide();
        if (pane is null) return;
        ActivatePane(pane);
        try
        {
            if (source is not null) AddDocumentView(source);
            else await OpenPathAsync(path!);
            if (pane.Documents.Count == 0) { RemovePane(pane, sourcePane); return; }
            if (sourcePane.ViewMode == (int)ViewMode.Split)
            {
                sourcePane.ViewMode = (int)ViewMode.Editor;
                UpdateContentLayout(sourcePane);
            }
            FocusEditor();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_activePane == pane) FocusEditor(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch
        {
            RemovePane(pane, sourcePane);
            throw;
        }
    }

    private NoteDocument AddDocumentView(NoteDocument source)
    {
        var document = new NoteDocument(source.Buffer);
        if (!document.IsViewOnly) InitializeTextDocument(document, source.Text);
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        document.Editor.SelectionStart = source.Editor.SelectionStart;
        document.Editor.SelectionEnd = source.Editor.SelectionEnd;
        return document;
    }

    private void RemovePane(EditorPane pane, EditorPane? next = null)
    {
        pane.MdEditor.ShowDocument(null);
        pane.ImageViewer.ShowImage(null);
        var index = _panes.IndexOf(pane);
        _panes.Remove(pane);
        if (_activePane == pane) ActivatePane(next ?? _panes[Math.Clamp(index - 1, 0, _panes.Count - 1)]);
        RebuildPaneLayout();
    }

    private void ActivateDocumentPane(NoteDocument document)
    {
        var pane = _panes.FirstOrDefault(p => p.Documents.Contains(document));
        if (pane is not null) ActivatePane(pane);
    }
}
