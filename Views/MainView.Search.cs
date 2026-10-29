using Avalonia.Controls;
using Avalonia.Interactivity;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Views;

public partial class MainView
{
    private void ExplorerTab_Click(object? sender, RoutedEventArgs e) => ShowSidebar(false);
    private void SearchTab_Click(object? sender, RoutedEventArgs e) => ShowSidebar(true);
    private void ShowSidebar(bool search, bool replace = false)
    {
        if (_busy) return;
        if (!Sidebar.IsVisible) ToggleFolders_Click(null, new RoutedEventArgs());
        FolderExplorer.IsVisible = !search;
        GlobalSearch.IsVisible = search;
        ExplorerTab.Classes.Set("active", !search);
        SearchTab.Classes.Set("active", search);
        if (search)
        {
            GlobalSearch.SetFolder(FolderExplorer.OpenedFolder);
            GlobalSearch.Open(replace);
        }
        else
        {
            // The sidebar may have just become visible; focus again once it is laid out.
            FolderExplorer.FocusTree();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (FolderExplorer.IsVisible && !FolderExplorer.IsKeyboardFocusWithin) FolderExplorer.FocusTree(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private async Task OpenSearchHitAsync(SearchFile file, SearchHit hit)
    {
        await OpenPathAsync(file.Path);
        if (_active is not { } document || !SamePath(document.Path, file.Path)) return;
        if (document.Text != file.Source) { GlobalSearch.Schedule(); UpdateStatus("File changed; search results refreshed."); return; }
        document.Editor.Focus();
        document.Editor.SelectionStart = hit.Start;
        document.Editor.SelectionEnd = hit.Start + hit.Length;
        document.Editor.ScrollToLine(hit.Line - 1);
        if (document.IsMarkdown) MdEditor.CenterPreviewOnLine(hit.Start);
    }

    private async Task<string> ReplaceSearchResultsAsync(WorkspaceReplaceRequest request)
    {
        if (_busy) return "Wait for the current operation to finish.";
        var count = request.Files.Sum(f => f.Hits.Count);
        if (count == 0) return "No matches to replace.";
        SetBusy(true);
        try
        {
            var answer = await DialogService.ShowAsync(Host, "Replace in folder",
                $"Replace {count:N0} matches in {request.Files.Count:N0} files? Open files will keep changes unsaved; other files will be saved to disk.", "Replace", "Cancel");
            if (answer != "Replace") return "Replacement cancelled.";
            var replaced = 0;
            var changed = 0;
            var skipped = 0;
            foreach (var file in request.Files)
            {
                try
                {
                    // Lossy search text has U+FFFD in place of undecodable bytes; writing it back would corrupt the file.
                    if (file.Lossy) { skipped++; continue; }
                    if (FolderExplorer.OpenedFolder is not { } root || !ExplorerFileOperations.Contains(root, file.Path)) { skipped++; continue; }
                    var document = _documents.FirstOrDefault(d => d.Path is { } path && SamePath(path, file.Path));
                    var current = document?.Text ?? (await TextFileStore.ReadAsync(file.Path)).Text;
                    if (current != file.Source) { skipped++; continue; }
                    var text = current;
                    foreach (var hit in file.Hits.OrderByDescending(h => h.Start))
                    {
                        var replacement = FindReplace.Replacement(current, new FindMatch(hit.Start, hit.Length), request.Query, request.Replacement, request.Options);
                        text = text[..hit.Start] + replacement + text[(hit.Start + hit.Length)..];
                    }
                    if (document is not null)
                    {
                        // The workspace is busy/read-only; allow this one synchronous undoable edit.
                        var readOnly = document.Editor.IsReadOnly;
                        try
                        {
                            document.Editor.IsReadOnly = false;
                            document.Editor.SelectionStart = 0;
                            document.Editor.SelectionEnd = document.Text.Length;
                            document.Editor.SelectedText = text;
                        }
                        finally { document.Editor.IsReadOnly = readOnly; }
                        document.Refresh();
                    }
                    else await TextFileStore.WriteAsync(file.Path, text, file.Encoding);
                    replaced += file.Hits.Count;
                    changed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.DecoderFallbackException) { skipped++; }
            }
            UpdateStatus();
            return $"Replaced {replaced:N0} matches in {changed:N0} files." + (skipped > 0 ? $" {skipped} changed or unavailable files skipped." : "");
        }
        finally { SetBusy(false); }
    }
}
