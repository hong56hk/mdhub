using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using Avalonia.Controls;
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

    // Item 57: code spans and fenced code use the editor gear's code font (only when it differs from the body font).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Mdhub.Services.EditorFonts.Changed += OnEditorFontsChanged; Mdhub.Services.EditorMode.Changed += InvalidateVisual; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); Mdhub.Services.EditorFonts.Changed -= OnEditorFontsChanged; Mdhub.Services.EditorMode.Changed -= InvalidateVisual; }
    // Item 66: in overwrite mode the caret is a block as wide as the next character (a space's width at a line end),
    // in presenter coordinates; CurrentLineOverlay paints it behind the text in the Accent color.
    public Rect? OverwriteCaret
    {
        get
        {
            if (!Mdhub.Services.EditorMode.Overwrite || SelectionStart != SelectionEnd || this.FindAncestorOfType<TextBox>() is not { IsKeyboardFocusWithin: true }) return null;
            var text = Text ?? "";
            var index = Math.Clamp(CaretIndex, 0, text.Length);
            var at = TextLayout.HitTestTextPosition(index);
            var width = index < text.Length && text[index] is not ('\r' or '\n') ? TextLayout.HitTestTextPosition(index + (char.IsSurrogatePair(text, index) ? 2 : 1)).X - at.X : 0;
            if (width <= 1 || at.Height <= 0) width = Math.Max(4, FontSize * 0.5);
            return new Rect(at.X, at.Y, width, at.Height);
        }
    }


    private void OnEditorFontsChanged() { _codeProperties.Clear(); InvalidateTextLayout(); }
    private readonly Dictionary<TextRunProperties, TextRunProperties> _codeProperties = new(ReferenceEqualityComparer.Instance);
    private FontFamily? _codeKey;
    private TextRunProperties CodeProps(TextRunProperties? p)
    {
        var family = Mdhub.Services.EditorFonts.CodeFamily;
        if (_codeKey != family) { _codeProperties.Clear(); _codeKey = family; }
        p ??= new GenericTextRunProperties(new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, foregroundBrush: Foreground, fontFeatures: FontFeatures);
        if (_codeProperties.TryGetValue(p, out var code)) return code;
        return _codeProperties[p] = new GenericTextRunProperties(new Typeface(family, p.Typeface.Style, p.Typeface.Weight, p.Typeface.Stretch), p.FontRenderingEmSize,
            p.TextDecorations, p.ForegroundBrush, p.BackgroundBrush, fontFeatures: FontFeatures);
    }
    private TextRunProperties? _plainCode; // gaps inside fenced code (no syntax span)
    // Splits the sorted, non-overlapping overrides at code boundaries; code parts (and unstyled gaps in code) get the code font.
    private ValueSpan<TextRunProperties>[] WithCodeFont(List<ValueSpan<TextRunProperties>> styles, int length)
    {
        if (!Mdhub.Services.EditorFonts.SeparateCodeFont) return styles.ToArray();
        var code = new List<(int Start, int End)>();
        foreach (var block in _syntax.Blocks) if (block.Kind == SyntaxBlockKind.Code) code.Add((block.Start, Math.Min(block.End, length)));
        foreach (var span in _syntax.Spans) if (span.CodeBackground) code.Add((span.Start, Math.Min(span.End, length)));
        if (code.Count == 0) return styles.ToArray();
        code.Sort((a, b) => a.Start.CompareTo(b.Start));
        var cuts = new SortedSet<int>();
        foreach (var s in styles) { cuts.Add(s.Start); cuts.Add(s.Start + s.Length); }
        foreach (var (a, b) in code) { cuts.Add(Math.Max(0, a)); cuts.Add(Math.Max(0, b)); }
        _plainCode = null;
        var result = new List<ValueSpan<TextRunProperties>>();
        int si = 0, ci = 0, prev = -1;
        foreach (var cut in cuts)
        {
            if (prev >= 0 && cut > prev)
            {
                while (si < styles.Count && styles[si].Start + styles[si].Length <= prev) si++;
                while (ci < code.Count && code[ci].End <= prev) ci++;
                var style = si < styles.Count && styles[si].Start <= prev ? styles[si].Value : null;
                var inCode = ci < code.Count && code[ci].Start <= prev;
                if (inCode) result.Add(new(prev, cut - prev, style is null ? (_plainCode ??= CodeProps(null)) : CodeProps(style)));
                else if (style is not null) result.Add(new(prev, cut - prev, style));
            }
            prev = cut;
        }
        return result.ToArray();
    }

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
        if (_syntax.Spans.Count == 0 && (_syntax.Blocks.Count == 0 || !Mdhub.Services.EditorFonts.SeparateCodeFont)) return original;
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
                textStyleOverrides: WithCodeFont(styles, text.Length));
        }
        finally { original.Dispose(); }
    }
}
