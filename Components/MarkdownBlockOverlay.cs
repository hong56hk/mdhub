using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mdhub.Text;

namespace Mdhub.Components;

// Item 33: full-width background behind fenced code blocks and a bar left of block quotes, drawn under the
// editor text (and under the current-line band). Only blocks in the visible range are measured and drawn.
public sealed class MarkdownBlockOverlay : Control
{
    public static readonly IBrush CodeBrush = Mdhub.Services.ThemeService.Brush("SyntaxCodeBlockBackground");
    public static readonly IBrush QuoteBarBrush = Mdhub.Services.ThemeService.Brush("SyntaxQuoteBar");
    public const double QuoteBarWidth = 3;
    private TextBox? _editor;
    private MarkdownTextPresenter? _presenter;
    private ScrollViewer? _scroll;

    public MarkdownBlockOverlay() => IsHitTestVisible = false;

    public void Attach(TextBox? editor)
    {
        if (editor == _editor) { InvalidateVisual(); return; }
        Unhook();
        _editor = editor;
        InvalidateVisual();
    }

    private void Hook()
    {
        if (_editor is null || _presenter is not null) return;
        _presenter = _editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_presenter is not null) { _presenter.LayoutUpdated += OnChanged; _presenter.SyntaxChanged += OnChanged; }
        if (_scroll is not null) _scroll.ScrollChanged += OnChanged;
    }

    private void Unhook()
    {
        if (_presenter is not null) { _presenter.LayoutUpdated -= OnChanged; _presenter.SyntaxChanged -= OnChanged; }
        if (_scroll is not null) _scroll.ScrollChanged -= OnChanged;
        _presenter = null; _scroll = null; _editor = null;
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Visible block decorations in overlay coordinates (for rendering and tests).</summary>
    public IReadOnlyList<(SyntaxBlockKind Kind, Rect Rect)> VisibleBlocks()
    {
        Hook();
        var result = new List<(SyntaxBlockKind, Rect)>();
        if (_presenter is not { HighlightMarkdown: true, TextLayout: { } layout } presenter) return result;
        var blocks = presenter.Syntax.Blocks;
        if (blocks.Count == 0) return result;
        // Visible character range from the viewport.
        var first = 0; var last = int.MaxValue;
        if (_scroll is not null && _scroll.Viewport.Height > 0)
        {
            first = layout.HitTestPoint(new Point(0, Math.Max(0, _scroll.Offset.Y - 50))).TextPosition;
            last = layout.HitTestPoint(new Point(0, _scroll.Offset.Y + _scroll.Viewport.Height + 50)).TextPosition + 1;
        }
        var origin = presenter.TranslatePoint(default, this);
        if (origin is null) return result;
        foreach (var block in blocks)
        {
            if (block.End < first) continue;
            if (block.Start > last) break;
            var a = layout.HitTestTextPosition(block.Start);
            var b = layout.HitTestTextPosition(block.End);
            var y = origin.Value.Y + a.Top;
            var h = Math.Max(1, b.Bottom - a.Top);
            result.Add(block.Kind == SyntaxBlockKind.Code
                ? (block.Kind, new Rect(0, y, Bounds.Width, h))
                : (block.Kind, new Rect(Math.Max(0, origin.Value.X - 10), y, QuoteBarWidth, h)));
        }
        return result;
    }

    public override void Render(DrawingContext context)
    {
        var blocks = VisibleBlocks();
        if (blocks.Count == 0) return;
        var clip = new Rect(Bounds.Size);
        if (_scroll?.Presenter is Visual viewport && viewport.TranslatePoint(default, this) is { } o)
            clip = clip.Intersect(new Rect(0, o.Y, Bounds.Width, viewport.Bounds.Height));
        using (context.PushClip(clip))
            foreach (var (kind, rect) in blocks) context.FillRectangle(kind == SyntaxBlockKind.Code ? CodeBrush : QuoteBarBrush, rect);
    }
}
