using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mdhub.Text;

namespace Mdhub.Components;

// Faint band behind the caret's logical line (all wrapped rows). Sits under the editor text; hidden with a selection.
public sealed class CurrentLineOverlay : Control
{
    public static readonly IBrush LineBrush = Mdhub.Services.ThemeService.Brush("CurrentLineBackground");
    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;

    public CurrentLineOverlay() => IsHitTestVisible = false;

    public void Attach(TextBox? editor)
    {
        if (editor == _editor) { InvalidateVisual(); return; }
        Unhook();
        _editor = editor;
        if (_editor is not null) _editor.PropertyChanged += OnEditorChanged;
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
        if (_editor is not null) _editor.PropertyChanged -= OnEditorChanged;
        if (_presenter is not null) _presenter.LayoutUpdated -= OnChanged;
        if (_scroll is not null) _scroll.ScrollChanged -= OnChanged;
        _presenter = null; _scroll = null; _editor = null;
    }

    private void OnEditorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty
            || e.Property == TextBox.TextProperty || e.Property == TextBox.TextWrappingProperty) InvalidateVisual();
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    // The band in overlay coordinates, or null when hidden (selection, no editor, not laid out).
    public Rect? LineRect()
    {
        Hook();
        if (_editor is null || _presenter?.TextLayout is not { } layout || _editor.SelectionStart != _editor.SelectionEnd) return null;
        var text = _editor.Text ?? "";
        var (start, end) = CaretLine.At(text, _editor.CaretIndex);
        var top = layout.HitTestTextPosition(start);
        var bottom = layout.HitTestTextPosition(end);
        if (_presenter.TranslatePoint(new Point(0, top.Top), this) is not { } a || _presenter.TranslatePoint(new Point(0, bottom.Bottom), this) is not { } b) return null;
        return new Rect(0, a.Y, Bounds.Width, Math.Max(1, b.Y - a.Y));
    }

    public override void Render(DrawingContext context)
    {
        if (LineRect() is not { } rect) return;
        var clip = new Rect(Bounds.Size);
        if (_scroll?.Presenter is Visual viewport && viewport.TranslatePoint(default, this) is { } origin)
            clip = clip.Intersect(new Rect(origin.X - 1000, origin.Y, viewport.Bounds.Width + 2000, viewport.Bounds.Height));
        using (context.PushClip(clip)) context.FillRectangle(LineBrush, rect);
    }
}
