using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Mdhub.Components;

// Add one viewport of scrollable space without adding text or stretching the content itself.
public sealed class ScrollPastEnd : Decorator
{
    private ScrollViewer? _scroll;
    private Size _contentSize;

    public ScrollPastEnd()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _scroll = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
            InvalidateMeasure();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = null;
        };
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportDelta.Y != 0) InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        _contentSize = Child?.DesiredSize ?? default;
        return new Size(_contentSize.Width, _contentSize.Height + (_scroll?.Viewport.Height ?? 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, _contentSize.Height));
        return finalSize;
    }
}
