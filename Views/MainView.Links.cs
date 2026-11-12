using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    // Item 111: Ctrl+click (Cmd+click on macOS) any Markdown link in the source editor; the pointer is a hand while
    // the modifier is held over a link. Preview follows on a plain click (MdEditor.LinkClicked).
    private void AttachLinkFollow(NoteDocument document)
    {
        var editor = document.Editor;
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var hand = false;
        Point last = default;

        void SetHand(bool on)
        {
            if (on == hand) return;
            hand = on;
            editor.Cursor = on ? new Cursor(StandardCursorType.Hand) : null;
        }

        bool OverLink(Point inPresenter, TextPresenter? presenter)
        {
            if (presenter?.TextLayout is null) return false;
            var index = presenter.TextLayout.HitTestPoint(inPresenter).TextPosition;
            return PdfNotes.MarkdownHrefAt(document.Editor.Text ?? "", index) is not null;
        }

        TextPresenter? Presenter() => editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

        editor.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(command) || !e.GetCurrentPoint(editor).Properties.IsLeftButtonPressed) return;
            if (Presenter() is not { } presenter) return;
            var index = presenter.TextLayout.HitTestPoint(e.GetPosition(presenter)).TextPosition;
            if (PdfNotes.MarkdownHrefAt(document.Editor.Text ?? "", index) is not { } href) return;
            e.Handled = true;
            if (!_busy) _ = RunAsync(() => FollowLinkAsync(href, document.Path), focusEditor: false);
        }, RoutingStrategies.Tunnel);

        editor.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            last = e.GetPosition(Presenter());
            SetHand(e.KeyModifiers.HasFlag(command) && OverLink(last, Presenter()));
        }, RoutingStrategies.Tunnel);

        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin)
                SetHand(OverLink(last, Presenter()));
        }, RoutingStrategies.Tunnel);

        editor.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin) SetHand(false);
        }, RoutingStrategies.Tunnel);

        editor.AddHandler(InputElement.PointerExitedEvent, (_, _) => SetHand(false), RoutingStrategies.Bubble);
    }

    internal Task FollowPdfPageLinkAsync(string href, string? fromDocumentPath) => FollowLinkAsync(href, fromDocumentPath);

    internal async Task FollowLinkAsync(string href, string? fromDocumentPath)
    {
        var link = MarkdownLinks.Resolve(href, fromDocumentPath, FolderExplorer.OpenedFolder);
        switch (link.Kind)
        {
            case MarkdownLinkKind.Blocked:
                ShowTransientStatus(Loc.T("LinkBlocked"));
                return;
            case MarkdownLinkKind.Missing:
                ShowTransientStatus(Loc.T("LinkTargetMissing"));
                return;
            case MarkdownLinkKind.Web:
            case MarkdownLinkKind.Mail:
                if (link.Uri is { } uri) await DialogService.LaunchAsync(Host, uri);
                return;
            case MarkdownLinkKind.LocalFolder:
                await RevealLinkedFolderAsync(link.Path!);
                return;
            case MarkdownLinkKind.OpenInApp:
                await OpenWithDefaultAppAsync(link.Path!);
                return;
            case MarkdownLinkKind.Heading:
                await OpenAndScrollAsync(link.Path ?? fromDocumentPath, link.Fragment, alreadyCurrent: true);
                return;
            case MarkdownLinkKind.LocalFile:
                if (link.Page is { } page && link.Path is { } pdf)
                {
                    await OpenPdfPageAsync(pdf, page);
                    return;
                }
                await OpenAndScrollAsync(link.Path!, link.Fragment, alreadyCurrent: SamePath(fromDocumentPath, link.Path!));
                return;
        }
    }

    async Task RevealLinkedFolderAsync(string path)
    {
        if (!Sidebar.IsVisible) ToggleFolders_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
        if (FolderExplorer.Contains(path) && await FolderExplorer.RevealAsync(path, quiet: true, focus: true)) return;
        ShowTransientStatus(Loc.T("LinkTargetMissing"));
    }

    async Task OpenAndScrollAsync(string? path, string? fragment, bool alreadyCurrent)
    {
        NoteDocument? document = null;
        if (path is { Length: > 0 })
        {
            if (!alreadyCurrent || !SamePath(_active?.Path, path))
                await OpenPathAsync(path);
            document = AllDocuments.FirstOrDefault(d => SamePath(d.Path, path));
        }
        else document = _active;
        if (document is null) { ShowTransientStatus(Loc.T("LinkTargetMissing")); return; }

        if (fragment is not null && document.IsMarkdown && MarkdownLinks.FindHeadingLine(document.Text, fragment) is { } line)
        {
            var pane = _panes.First(p => p.Documents.Contains(document));
            ActivatePane(pane);
            pane.FileTabs.SelectedDocument = document;
            JumpToLine(document, line);
        }
    }
}