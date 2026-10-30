using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Mdhub.Text;

namespace Mdhub.Components;

public sealed class MarkdownTextPresenter : TextPresenter
{
    public static readonly StyledProperty<bool> HighlightMarkdownProperty = AvaloniaProperty.Register<MarkdownTextPresenter, bool>(nameof(HighlightMarkdown));
    public bool HighlightMarkdown { get => GetValue(HighlightMarkdownProperty); set => SetValue(HighlightMarkdownProperty, value); }
    public static readonly IBrush HeadingBrush = Mdhub.Services.ThemeService.Brush("SyntaxHeading");
    public static readonly IBrush OrderedListBrush = Mdhub.Services.ThemeService.Brush("SyntaxOrderedList");
    public static readonly IBrush UnorderedListBrush = Mdhub.Services.ThemeService.Brush("SyntaxUnorderedList");
    private string? _cachedText;
    private IReadOnlyList<MarkdownMarker> _markers = [];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HighlightMarkdownProperty) InvalidateTextLayout();
    }

    protected override TextLayout CreateTextLayout()
    {
        var original = base.CreateTextLayout();
        if (!HighlightMarkdown || !string.IsNullOrEmpty(PreeditText)) return original;
        var text = Text ?? "";
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (_cachedText != text) { _cachedText = text; _markers = MarkdownSyntax.Markers(text); }
        if (_markers.Count == 0) return original;
        var styles = new List<ValueSpan<TextRunProperties>>();
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionLength = Math.Abs(SelectionEnd - SelectionStart);
        if (ShowSelectionHighlight && selectionLength > 0 && SelectionForegroundBrush is { } selectionBrush)
            styles.Add(new(selectionStart, selectionLength, new GenericTextRunProperties(typeface, FontSize, foregroundBrush: selectionBrush, fontFeatures: FontFeatures)));
        foreach (var marker in _markers)
        {
            // Selection/composition styles take precedence over syntax colors.
            if (styles.Any(s => s.Start < marker.Start + marker.Length && s.Start + s.Length > marker.Start)) continue;
            var brush = marker.Kind switch
            {
                MarkdownMarkerKind.Heading => HeadingBrush,
                MarkdownMarkerKind.OrderedList => OrderedListBrush,
                _ => UnorderedListBrush
            };
            styles.Add(new ValueSpan<TextRunProperties>(marker.Start, marker.Length,
                new GenericTextRunProperties(typeface, FontSize, foregroundBrush: brush, fontFeatures: FontFeatures)));
        }
        try
        {
            return new TextLayout(text, typeface, FontSize, Foreground, TextAlignment, TextWrapping,
                flowDirection: FlowDirection, maxWidth: original.MaxWidth, maxHeight: original.MaxHeight,
                lineHeight: LineHeight, letterSpacing: LetterSpacing, fontFeatures: FontFeatures,
                textStyleOverrides: styles.OrderBy(s => s.Start).ToArray());
        }
        finally { original.Dispose(); }
    }
}
