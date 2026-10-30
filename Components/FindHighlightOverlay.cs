using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mdhub.Text;

namespace Mdhub.Components;

// Paints find matches over the editor text (all matches faint, the current one strong). Not hit-testable.
public sealed class FindHighlightOverlay : Control
{
    private static readonly IBrush MatchBrush = Mdhub.Services.ThemeService.Brush("FindMatchBackground");
    private static readonly IBrush CurrentBrush = Mdhub.Services.ThemeService.Brush("FindCurrentBackground");
    private static readonly IPen CurrentPen = new Pen(Mdhub.Services.ThemeService.Brush("FindCurrentBorder"), 1);
    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;
    private IReadOnlyList<FindMatch> _matches = [];
    private int _current = -1;

    public FindHighlightOverlay() => IsHitTestVisible = false;
    public int MatchCount => _matches.Count;
    public int Current => _current;

    public void Update(TextBox? editor, IReadOnlyList<FindMatch> matches, int current)
    {
        if (editor != _editor) { Unhook(); _editor = editor; }
        _matches = editor is null ? [] : matches;
        _current = current;
        Hook();
        InvalidateVisual();
    }

    private void Hook()
    {
        if (_editor is null || _presenter is not null) return;
        _presenter = _editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_presenter is not null) _presenter.LayoutUpdated += OnChanged;
        if (_scroll is not null) _scroll.ScrollChanged += OnChanged;
    }

    private void Unhook()
    {
        if (_presenter is not null) _presenter.LayoutUpdated -= OnChanged;
        if (_scroll is not null) _scroll.ScrollChanged -= OnChanged;
        _presenter = null;
        _scroll = null;
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    // Highlight rectangles in overlay coordinates, for rendering and tests.
    public IEnumerable<(Rect Rect, bool IsCurrent)> Rectangles()
    {
        Hook();
        if (_presenter?.TextLayout is not { } layout || _editor is null) yield break;
        var length = _editor.Text?.Length ?? 0;
        for (var i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            if (match.End > length) continue;
            foreach (var rect in layout.HitTestTextRange(match.Start, match.Length))
                if (_presenter.TranslatePoint(rect.TopLeft, this) is { } topLeft)
                    yield return (new Rect(topLeft, rect.Size), i == _current);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_matches.Count == 0) return;
        var clip = new Rect(Bounds.Size);
        if (_scroll?.Presenter is Visual viewport && viewport.TranslatePoint(default, this) is { } origin)
            clip = clip.Intersect(new Rect(origin, viewport.Bounds.Size));
        using (context.PushClip(clip))
            foreach (var (rect, isCurrent) in Rectangles())
            {
                if (!rect.Intersects(clip)) continue;
                if (isCurrent) context.DrawRectangle(CurrentBrush, CurrentPen, rect, 2, 2);
                else context.DrawRectangle(MatchBrush, null, rect, 2, 2);
            }
    }
}