using System.Reflection;
using System.Reflection.Emit;
using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.Threading;
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
    public static readonly StyledProperty<string?> SourceLanguageProperty = AvaloniaProperty.Register<MarkdownTextPresenter, string?>(nameof(SourceLanguage));
    public string? SourceLanguage { get => GetValue(SourceLanguageProperty); set => SetValue(SourceLanguageProperty, value); }
    public static readonly StyledProperty<bool> StyleInlineCodeProperty = AvaloniaProperty.Register<MarkdownTextPresenter, bool>(nameof(StyleInlineCode), true);
    public bool StyleInlineCode { get => GetValue(StyleInlineCodeProperty); set => SetValue(StyleInlineCodeProperty, value); }

    /// <summary>The shared theme brush for a syntax colour (null for the editor's own foreground).</summary>
    public static IBrush? BrushFor(SyntaxColor color) => color == SyntaxColor.Default ? null : Mdhub.Services.ThemeService.Brush("Syntax" + color);
    public static IBrush InlineCodeBackground => Mdhub.Services.ThemeService.Brush("SyntaxInlineCodeBackground");

    private readonly MarkdownHighlighter _highlighter = new();
    private readonly CodeHighlighter.Tokenizer _sourceHighlighter = new();
    private readonly Dictionary<(SyntaxColor, SyntaxFont, bool), GenericTextRunProperties> _properties = [];
    private (FontFamily, double, FontFeatureCollection?) _propertiesKey;
    private string? _cachedText;
    private SyntaxResult _syntax = new([], []);

    /// <summary>The current tokens and block decorations (empty when highlighting is off).</summary>
    public SyntaxResult Syntax => HighlightMarkdown || SourceLanguage is not null ? _syntax : new([], []);
    public event EventHandler? SyntaxChanged;

    // Item 57: code spans and fenced code use the editor gear's code font (only when it differs from the body font).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Mdhub.Services.EditorFonts.Changed += OnEditorFontsChanged; Mdhub.Services.EditorMode.Changed += InvalidateVisual; Mdhub.Services.EditorBehavior.Changed += InvalidateTextLayout; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); Mdhub.Services.EditorFonts.Changed -= OnEditorFontsChanged; Mdhub.Services.EditorMode.Changed -= InvalidateVisual; Mdhub.Services.EditorBehavior.Changed -= InvalidateTextLayout; }
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
        if (SourceLanguage is not null) code.Add((0, length));
        foreach (var block in _syntax.Blocks) if (block.Kind == SyntaxBlockKind.Code) code.Add((block.Start, Math.Min(block.End, length)));
        foreach (var span in _syntax.Spans) if (span.CodeBackground && StyleInlineCode) code.Add((span.Start, Math.Min(span.End, length)));
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

    // Item 119: skip the stock TextLayout invalidation when the selection stays collapsed (caret move only).
    // TextPresenter.OnPropertyChanged always invalidates: it calls virtual InvalidateTextLayout when
    // SelectionForegroundBrush is set, otherwise the private InvalidateTextLayoutKeepCache. The editor's
    // brush is typically unset, so overriding InvalidateTextLayout cannot catch this path. For a skip we
    // call Control.OnPropertyChanged (TextPresenter's base) so listeners still run, then InvalidateVisual
    // for the caret, without disposing the layout or invalidating measure.
    // SelectionStart and SelectionEnd are often updated as a pair, so the first change can look like a
    // temporary range; the decision is finished at DispatcherPriority.Send once both have settled.
    static readonly Action<TextPresenter, AvaloniaPropertyChangedEventArgs> CallControlOnPropertyChanged = CreateControlOnPropertyChanged();

    static Action<TextPresenter, AvaloniaPropertyChangedEventArgs> CreateControlOnPropertyChanged()
    {
        var baseType = typeof(TextPresenter).BaseType
            ?? throw new InvalidOperationException("TextPresenter has no base type.");
        var method = baseType.GetMethod(nameof(OnPropertyChanged), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(AvaloniaPropertyChangedEventArgs)])
            ?? throw new InvalidOperationException(baseType.Name + ".OnPropertyChanged is missing.");
        var dm = new DynamicMethod("CallControlOnPropertyChanged", typeof(void),
            [typeof(TextPresenter), typeof(AvaloniaPropertyChangedEventArgs)], typeof(TextPresenter).Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Action<TextPresenter, AvaloniaPropertyChangedEventArgs>>();
    }

    bool _selectionBatchFromCollapsed;
    bool _selectionDecisionPosted;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SelectionStartProperty || change.Property == SelectionEndProperty)
        {
            HandleSelectionIndexChanged(change);
            return;
        }
        base.OnPropertyChanged(change);
        if (change.Property == HighlightMarkdownProperty || change.Property == SourceLanguageProperty || change.Property == StyleInlineCodeProperty)
        { _cachedText = null; InvalidateTextLayout(); SyntaxChanged?.Invoke(this, EventArgs.Empty); }
    }

    void HandleSelectionIndexChanged(AvaloniaPropertyChangedEventArgs change)
    {
        var other = change.Property == SelectionStartProperty ? SelectionEnd : SelectionStart;
        var oldVal = change.GetOldValue<int>();
        var newVal = change.GetNewValue<int>();
        var wasEmpty = oldVal == other;
        var isEmpty = newVal == other;

        if (wasEmpty && isEmpty)
        {
            SkipStockSelectionInvalidation(change);
            return;
        }

        if (wasEmpty && !isEmpty)
        {
            // First half of a collapsed caret move (Start then End), or a real selection starting from a caret.
            _selectionBatchFromCollapsed = true;
            SkipStockSelectionInvalidation(change);
            if (!_selectionDecisionPosted)
            {
                _selectionDecisionPosted = true;
                Dispatcher.UIThread.Post(FinishSelectionLayoutDecision, DispatcherPriority.Send);
            }
            return;
        }

        if (_selectionBatchFromCollapsed && isEmpty)
        {
            // Second half of a collapsed caret move: transient range, now collapsed again.
            _selectionBatchFromCollapsed = false;
            SkipStockSelectionInvalidation(change);
            return;
        }

        // Real selection appeared, changed, or cleared (including non-empty to empty).
        _selectionBatchFromCollapsed = false;
        base.OnPropertyChanged(change);
    }

    void SkipStockSelectionInvalidation(AvaloniaPropertyChangedEventArgs change)
    {
        CallControlOnPropertyChanged(this, change);
        InvalidateVisual();
    }

    void FinishSelectionLayoutDecision()
    {
        _selectionDecisionPosted = false;
        if (!_selectionBatchFromCollapsed) return;
        _selectionBatchFromCollapsed = false;
        if (SelectionStart != SelectionEnd) InvalidateTextLayout();
        else InvalidateVisual();
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

    // Avalonia 12.1.4 TextPresenter stores the measure/arrange size in a private field; maxWidth/maxHeight treat zero as infinity.
    static readonly FieldInfo ConstraintField = typeof(TextPresenter).GetField("_constraint", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TextPresenter._constraint is missing.");
    static readonly FieldInfo TextLayoutField = typeof(TextPresenter).GetField("_textLayout", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TextPresenter._textLayout is missing.");
    static readonly MethodInfo EnsureCaretBoundsMethod = typeof(TextPresenter).GetMethod("EnsureCaretBounds", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TextPresenter.EnsureCaretBounds is missing.");

    // Avalonia.Utilities.MathUtilities is internal; this is the 12.1.4 IsZero used by CreateTextLayoutInternal.
    const double DoubleEpsilon = 2.2204460492503131e-016;
    static bool IsZero(double value) => Math.Abs(value) < 10.0 * DoubleEpsilon;

    (double MaxWidth, double MaxHeight) LayoutMaxSize()
    {
        var constraint = ConstraintField.GetValue(this) is Size size ? size : default;
        return MaxSizeFromConstraint(constraint);
    }

    static (double MaxWidth, double MaxHeight) MaxSizeFromConstraint(Size constraint) =>
        (IsZero(constraint.Width) ? double.PositiveInfinity : constraint.Width,
            IsZero(constraint.Height) ? double.PositiveInfinity : constraint.Height);

    // Avalonia 12.1.4 MathUtilities.AreClose (the class is internal).
    static bool AreClose(double value1, double value2)
    {
        if (value1 == value2) return true;
        var eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * DoubleEpsilon;
        return Math.Abs(value1 - value2) < eps;
    }

    // A still-valid layout can be kept across MeasureOverride: wrap-off ignores width,
    // wrap-on needs an equivalent width, and a tighter height must not start clipping.
    bool CanReuseLayout(TextLayout layout, Size availableSize)
    {
        var (maxWidth, maxHeight) = MaxSizeFromConstraint(availableSize);
        if (TextWrapping != TextWrapping.NoWrap && !AreClose(layout.MaxWidth, maxWidth)) return false;
        if (AreClose(layout.MaxHeight, maxHeight)) return true;
        var clipped = !double.IsPositiveInfinity(layout.MaxHeight) && layout.Height >= layout.MaxHeight - 0.5;
        return !clipped && (double.IsPositiveInfinity(maxHeight) || layout.Height <= maxHeight);
    }

    // Item 121: stock MeasureOverride always disposes the layout. Reuse it when the new
    // constraint would produce the same line breaking; text/font/theme/highlighting/selection/wrap
    // still go through InvalidateTextLayout and force a rebuild. Does not touch ArrangeOverride.
    protected override Size MeasureOverride(Size availableSize)
    {
        if (TextLayoutField.GetValue(this) is TextLayout existing && !CanReuseLayout(existing, availableSize))
        {
            existing.Dispose();
            TextLayoutField.SetValue(this, null);
        }
        ConstraintField.SetValue(this, availableSize);
        InvalidateArrange();
        var layout = TextLayout;
        EnsureCaretBoundsMethod.Invoke(this, null);
        return new Size(layout.WidthIncludingTrailingWhitespace, layout.Height);
    }

    // Same arguments as Avalonia 12.1.4 CreateTextLayoutInternal, except no shared TextRunCache.
    TextLayout StockLayout(string text, IReadOnlyList<ValueSpan<TextRunProperties>>? styles)
    {
        var (maxWidth, maxHeight) = LayoutMaxSize();
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        return new TextLayout(text, typeface, FontSize, Foreground, TextAlignment, TextWrapping, null, null,
            FlowDirection, maxWidth, maxHeight, LineHeight, LetterSpacing, 0, FontFeatures, styles);
    }

    ValueSpan<TextRunProperties>[]? StockSelectionStyles()
    {
        var start = Math.Min(SelectionStart, SelectionEnd);
        var length = Math.Max(SelectionStart, SelectionEnd) - start;
        if (!ShowSelectionHighlight || length <= 0 || SelectionForegroundBrush is not { } brush) return null;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        return [new ValueSpan<TextRunProperties>(start, length,
            new GenericTextRunProperties(typeface, FontSize, foregroundBrush: brush, fontFeatures: FontFeatures))];
    }

    // Item 118: build one layout from _constraint. Preedit still uses the stock path (combined text + underline).
    protected override TextLayout CreateTextLayout()
    {
        if (!string.IsNullOrEmpty(PreeditText)) return base.CreateTextLayout();
        var text = Text ?? "";
        var tabbed = text.Contains('\t');
        if (!HighlightMarkdown && SourceLanguage is null) return tabbed ? Tabbed(text, StockSelectionStyles()) : StockLayout(text, StockSelectionStyles());
        if (_cachedText != text) { _cachedText = text; _syntax = HighlightMarkdown ? _highlighter.Run(text) : _sourceHighlighter.Run(text, SourceLanguage); SyntaxChanged?.Invoke(this, EventArgs.Empty); }
        if (_syntax.Spans.Count == 0 && (_syntax.Blocks.Count == 0 || !Mdhub.Services.EditorFonts.SeparateCodeFont)) return tabbed ? Tabbed(text, StockSelectionStyles()) : StockLayout(text, StockSelectionStyles());
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
            var color = span.Color;
            if (!Mdhub.Services.EditorBehavior.Current.CodeSyntaxHighlighting && (color is >= SyntaxColor.CodeText and <= SyntaxColor.CodeAttribute || color == SyntaxColor.InlineCode)) color = SyntaxColor.Default;
            if (!StyleInlineCode && span.CodeBackground) color = SyntaxColor.Default;
            var props = Props(color, span.Font, span.CodeBackground && StyleInlineCode);
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
        if (tabbed) return Tabbed(text, WithCodeFont(styles, text.Length));
        return StockLayout(text, WithCodeFont(styles, text.Length));
    }

    // Item 70: a tab character is TabSize spaces wide (the gear's tab size). Same text, styles and wrapping as the
    // regular layout, built through a paragraph whose incremental tab is TabSize space widths.
    public double TabWidth
    {
        get
        {
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            using var space = new TextLayout(" ", typeface, FontSize, null);
            return space.WidthIncludingTrailingWhitespace * Mdhub.Services.EditorBehavior.Current.TabSize;
        }
    }

    private TextLayout Tabbed(string text, IReadOnlyList<ValueSpan<TextRunProperties>>? styles)
    {
        var (maxWidth, maxHeight) = LayoutMaxSize();
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var props = new GenericTextRunProperties(typeface, FontSize, foregroundBrush: Foreground, fontFeatures: FontFeatures);
        var paragraph = new TabParagraph(FlowDirection, TextAlignment, TextWrapping, LineHeight, LetterSpacing, props, TabWidth);
        return new TextLayout(new TabSource(text, props, styles), paragraph, TextTrimming.None, maxWidth, maxHeight, 0);
    }

    private sealed class TabParagraph(FlowDirection flow, TextAlignment alignment, TextWrapping wrapping, double lineHeight, double letterSpacing, TextRunProperties props, double tab) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => flow;
        public override TextAlignment TextAlignment => alignment;
        public override double LineHeight => lineHeight;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => props;
        public override TextWrapping TextWrapping => wrapping;
        public override double Indent => 0;
        public override double DefaultIncrementalTab => tab;
        public override double LetterSpacing => letterSpacing;
    }

    private sealed class TabSource(string text, TextRunProperties props, IReadOnlyList<ValueSpan<TextRunProperties>>? styles) : ITextSource
    {
        public TextRun? GetTextRun(int index)
        {
            if (index > text.Length) return null;
            if (index == text.Length) return new TextEndOfParagraph();
            var run = props; var length = text.Length - index;
            if (styles is not null)
                foreach (var s in styles)
                {
                    if (s.Start <= index && index < s.Start + s.Length) { run = s.Value; length = Math.Min(length, s.Start + s.Length - index); break; }
                    if (s.Start > index) length = Math.Min(length, s.Start - index);
                }
            return new TextCharacters(text.AsMemory(index, length), run);
        }
    }
}
