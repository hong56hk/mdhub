using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Mdhub.Text;

namespace Mdhub.Components;

// Item 33: Markdown source highlighting. The tokenizer (MarkdownHighlighter) memoizes per line, so an edit only
// re-tokenizes changed lines; the spans become TextLayout style overrides (same font family and size, so line
// heights and the caret, selection, current-line, find and gutter geometry are unchanged). Block decorations
// (fenced code background, quote bar) are drawn behind the text by MarkdownBlockOverlay.
public sealed class MarkdownTextPresenter : TextPresenter
{
    public static readonly StyledProperty<bool> HighlightMarkdownProperty = AvaloniaProperty.Register<MarkdownTextPresenter, bool>(nameof(HighlightMarkdown));
    public bool HighlightMarkdown { get => GetValue(HighlightMarkdownProperty); set => SetValue(HighlightMarkdownProperty, value); }

    /// <summary>The shared theme brush for a syntax colour (null for the editor's own foreground).</summary>
    public static IBrush? BrushFor(SyntaxColor color) => color == SyntaxColor.Default ? null : Mdhub.Services.ThemeService.Brush("Syntax" + color);
    public static IBrush InlineCodeBackground => Mdhub.Services.ThemeService.Brush("SyntaxInlineCodeBackground");

    private readonly MarkdownHighlighter _highlighter = new();
    private readonly Dictionary<(SyntaxColor, SyntaxFont, bool), GenericTextRunProperties> _properties = [];
    private (FontFamily, double, FontFeatureCollection?) _propertiesKey;
    private string? _cachedText;
    private SyntaxResult _syntax = new([], []);

    /// <summary>The current tokens and block decorations (empty when highlighting is off).</summary>
    public SyntaxResult Syntax => HighlightMarkdown ? _syntax : new([], []);
    public event EventHandler? SyntaxChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HighlightMarkdownProperty) { InvalidateTextLayout(); SyntaxChanged?.Invoke(this, EventArgs.Empty); }
    }

    private GenericTextRunProperties Props(SyntaxColor color, SyntaxFont font, bool codeBackground)
    {
        var key = (FontFamily, FontSize, FontFeatures);
        if (key != _propertiesKey) { _properties.Clear(); _propertiesKey = key; }
        if (_properties.TryGetValue((color, font, codeBackground), out var p)) return p;
        var typeface = new Typeface(FontFamily, font.HasFlag(SyntaxFont.Italic) ? FontStyle.Italic : FontStyle,
            font.HasFlag(SyntaxFont.Bold) ? FontWeight.Bold : FontWeight, FontStretch);
        p = new GenericTextRunProperties(typeface, FontSize,
            textDecorations: font.HasFlag(SyntaxFont.Strike) ? TextDecorations.Strikethrough : null,
            foregroundBrush: BrushFor(color) ?? Foreground,
            backgroundBrush: codeBackground ? InlineCodeBackground : null,
            fontFeatures: FontFeatures);
        return _properties[(color, font, codeBackground)] = p;
    }

    protected override TextLayout CreateTextLayout()
    {
        var original = base.CreateTextLayout();
        if (!HighlightMarkdown || !string.IsNullOrEmpty(PreeditText)) return original;
        var text = Text ?? "";
        if (_cachedText != text) { _cachedText = text; _syntax = _highlighter.Run(text); SyntaxChanged?.Invoke(this, EventArgs.Empty); }
        if (_syntax.Spans.Count == 0) return original;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var styles = new List<ValueSpan<TextRunProperties>>(_syntax.Spans.Count + 2);
        var selStart = Math.Min(SelectionStart, SelectionEnd);
        var selEnd = Math.Max(SelectionStart, SelectionEnd);
        var selection = ShowSelectionHighlight && selEnd > selStart && SelectionForegroundBrush is { } selectionBrush
            ? new GenericTextRunProperties(typeface, FontSize, foregroundBrush: selectionBrush, fontFeatures: FontFeatures) : null;
        if (selection is null) selEnd = selStart = -1;
        var selectionAdded = false;
        foreach (var span in _syntax.Spans)
        {
            if (span.Length <= 0 || span.Start >= text.Length) continue;
            var start = span.Start; var end = Math.Min(span.End, text.Length);
            var props = Props(span.Color, span.Font, span.CodeBackground);
            // The selection colour takes precedence: cut syntax spans around it.
            if (selection is not null && start < selEnd && end > selStart)
            {
                if (start < selStart) styles.Add(new(start, selStart - start, props));
                if (!selectionAdded) { styles.Add(new(selStart, selEnd - selStart, selection)); selectionAdded = true; }
                if (end > selEnd) styles.Add(new(selEnd, end - selEnd, props));
                continue;
            }
            if (selection is not null && !selectionAdded && start >= selEnd) { styles.Add(new(selStart, selEnd - selStart, selection)); selectionAdded = true; }
            styles.Add(new(start, end - start, props));
        }
        if (selection is not null && !selectionAdded) styles.Add(new(selStart, selEnd - selStart, selection));
        try
        {
            return new TextLayout(text, typeface, FontSize, Foreground, TextAlignment, TextWrapping,
                flowDirection: FlowDirection, maxWidth: original.MaxWidth, maxHeight: original.MaxHeight,
                lineHeight: LineHeight, letterSpacing: LetterSpacing, fontFeatures: FontFeatures,
                textStyleOverrides: styles.ToArray());
        }
        finally { original.Dispose(); }
    }
}
