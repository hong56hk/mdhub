using Mdhub.Components;
using Mdhub.Services;

namespace Mdhub.Views;

// Ctrl+P Quick Open: one cached, watched file index per opened folder.
public partial class MainView
{
    private QuickOpenIndex? _quickOpenIndex;
    internal QuickOpen QuickOpenControl => QuickOpenBox;
    internal QuickOpenIndex? QuickOpenIndex => _quickOpenIndex;

    private void WireQuickOpen()
    {
        QuickOpenBox.RecentFiles = () => RecentService.Files;
        QuickOpenBox.Accept = AcceptQuickOpenAsync;
        QuickOpenBox.Closed += () => { if (!_quickOpenAccepting && _active is { IsImage: false } d && QuickOpenBox.IsKeyboardFocusWithin) d.Editor.Focus(); };
        DetachedFromVisualTree += (_, _) => SetQuickOpenRoot(null);
    }
    private bool _quickOpenAccepting;

    private void SetQuickOpenRoot(string? root)
    {
        if (_quickOpenIndex is { } old && root is not null && old.Root == Path.GetFullPath(root)) return;
        _quickOpenIndex?.Dispose();
        _quickOpenIndex = root is null ? null : new QuickOpenIndex(root);
        // Warm the cache so the first Ctrl+P is instant.
        _ = _quickOpenIndex?.GetFilesAsync();
        QuickOpenBox.Index = _quickOpenIndex;
    }

    internal void OpenQuickOpen()
    {
        if (TextEditor.IsFindOpen && TextEditor.IsFindFocused) TextEditor.CloseFind();
        QuickOpenBox.Open();
    }

    private async Task AcceptQuickOpenAsync(string? path, int? line)
    {
        _quickOpenAccepting = true;
        try
        {
            if (path is not null) await OpenPathAsync(path);
            if (line is { } l && _active is { IsImage: false } document && (path is null || SamePath(document.Path, path)))
                JumpToLine(document, l);
            else _active?.Editor.Focus();
        }
        finally { _quickOpenAccepting = false; }
    }
}
