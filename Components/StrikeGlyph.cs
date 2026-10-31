using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mdhub.Components;

/// <summary>
/// The format bar's Strikethrough icon: an "S" drawn like the other text icons, with a
/// horizontal line through the vertical middle of the letter's ink, extending a little past
/// both sides. (The old "S" + combining stroke drew the line at the baseline in most fonts.)
/// </summary>
public sealed class StrikeGlyph : Panel
{
    private readonly TextBlock letter = new() { Text = "S", Padding = new Thickness(2, 0) };
    private readonly Line line;

    public StrikeGlyph()
    {
        line = new Line(this);
        Children.Add(letter);
        Children.Add(line);
    }

    public string? Text => letter.Text;
    public IBrush? Foreground => letter.Foreground;
    public double FontSize => letter.FontSize;
    public FontFamily FontFamily => letter.FontFamily;

    /// <summary>The letter's ink bounds and the line, in this control's coordinates (for tests).</summary>
    public Rect GlyphBounds { get; private set; }
    public Point LineStart { get; private set; }
    public Point LineEnd { get; private set; }
    public double LineThickness { get; private set; }

    private sealed class Line : Control
    {
        private readonly StrikeGlyph owner;
        public Line(StrikeGlyph owner) { this.owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            var t = owner.letter;
            var brush = t.Foreground;
            if (brush is null || t.TextLayout is null) return;
            var origin = new Point(t.Padding.Left, t.Padding.Top);
            var ft = new FormattedText(t.Text ?? "S", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, brush);
            var ink = ft.BuildGeometry(origin)?.Bounds ?? default;
            if (ink.Width <= 0 || ink.Height <= 0)
            {
                // No outline available: estimate the ink (cap height about 0.7 em above the baseline).
                var baseline = origin.Y + t.TextLayout.Baseline;
                ink = new Rect(origin.X, baseline - t.FontSize * 0.7, t.TextLayout.WidthIncludingTrailingWhitespace, t.FontSize * 0.7);
            }
            owner.GlyphBounds = ink;
            var overhang = Math.Max(1.5, t.FontSize * 0.15);
            owner.LineThickness = Math.Max(1, Math.Round(t.FontSize / 12.0 * 2) / 2);
            var y = ink.Center.Y;
            owner.LineStart = new Point(ink.Left - overhang, y);
            owner.LineEnd = new Point(ink.Right + overhang, y);
            context.DrawLine(new Pen(brush, owner.LineThickness), owner.LineStart, owner.LineEnd);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Repaint the line when the inherited colour or font changes (theme switch).
        letter.PropertyChanged += (_, a) => { if (a.Property == TextBlock.ForegroundProperty || a.Property == TextBlock.FontSizeProperty) line.InvalidateVisual(); };
    }
}
