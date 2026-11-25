using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Mdhub.Components;

// Draws logical line numbers beside a TextBox, positioned from the TextPresenter's TextLayout,
// so numbers follow scrolling, typing and word wrap without replacing the editor.
public sealed class LineNumberGutter : Control
{
    private static readonly IBrush NumberBrush = Mdhub.Services.ThemeService.Brush("GutterForeground");
    private const double LeftPadding = 10, RightPadding = 8;
    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;
    private int _lineCount = 1;

    public TextBox? Editor => _editor;
    public int LineCount => _lineCount;

    public void Attach(TextBox? editor)
    {
        if (_editor == editor) { Hook(); return; }
        if (_editor is not null)
        {
            _editor.TextChanged -= OnTextChanged;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor.TemplateApplied -= OnTemplateApplied;
        }
        Unhook();
        _editor = editor;
        if (editor is not null)
        {
            editor.TextChanged += OnTextChanged;
            editor.PropertyChanged += OnEditorPropertyChanged;
            editor.TemplateApplied += OnTemplateApplied;
        }
        UpdateLineCount();
        Hook();
    }

    private void OnTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) { Unhook(); Hook(); }
    private void OnTextChanged(object? sender, TextChangedEventArgs e) { UpdateLineCount(); InvalidateVisual(); }
    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextWrappingProperty || e.Property == TextBox.FontSizeProperty || e.Property == TextBox.FontFamilyProperty)
        { InvalidateMeasure(); InvalidateVisual(); }
    }
    private void Hook()
    {
        if (_editor is null || _presenter is not null) return;
        _presenter = _editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_presenter is not null) _presenter.LayoutUpdated += OnLayoutChanged;
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
    }
    private void Unhook()
    {
        if (_presenter is not null) _presenter.LayoutUpdated -= OnLayoutChanged;
        if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
        _presenter = null;
        _scroll = null;
    }
    private void OnLayoutChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private void UpdateLineCount()
    {
        var text = _editor?.Text ?? "";
        var count = 1;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) count++;
        if (count.ToString(CultureInfo.InvariantCulture).Length != _lineCount.ToString(CultureInfo.InvariantCulture).Length) InvalidateMeasure();
        _lineCount = count;
    }

    private FormattedText Format(string text) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(_editor?.FontFamily ?? FontFamily.Default), _editor?.FontSize ?? 15, NumberBrush);

    protected override Size MeasureOverride(Size availableSize)
    {
        Hook();
        var digits = Math.Max(2, _lineCount.ToString(CultureInfo.InvariantCulture).Length);
        return new Size(Math.Ceiling(Format(new string('9', digits)).WidthIncludingTrailingWhitespace) + LeftPadding + RightPadding, 0);
    }

    // Visible numbered rows: logical line number and its top in gutter coordinates. Continuation rows of wrapped lines are skipped.
    public IReadOnlyList<(int Number, double Top)> VisibleNumbers()
    {
        Hook();
        var result = new List<(int, double)>();
        foreach (var row in ViewportRows(includeMargin: false))
            result.Add((row.Number, row.Top));
        return result;
    }

    // Set by Render; used by tests to prove we draw a viewport-sized set, not every document line.
    internal int LastDrawnCount { get; private set; }

    public override void Render(DrawingContext context)
    {
        var right = Bounds.Width - RightPadding;
        var drawn = 0;
        using (context.PushClip(new Rect(Bounds.Size)))
            foreach (var (number, top, _) in ViewportRows(includeMargin: true))
            {
                var formatted = Format(number.ToString(CultureInfo.InvariantCulture));
                context.DrawText(formatted, new Point(right - formatted.WidthIncludingTrailingWhitespace, top));
                drawn++;
            }
        LastDrawnCount = drawn;
    }

    readonly record struct GutterRow(int Number, double Top, double Height);

    // Tops are computed from the current TextLayout on every call and not stored on the instance
    // (item 116: do not keep line offsets that can survive an edit).
    IEnumerable<GutterRow> ViewportRows(bool includeMargin)
    {
        if (_editor is null || _presenter?.TextLayout is not { } layout) yield break;
        var lines = layout.TextLines;
        if (lines.Count == 0) yield break;
        if (_presenter.TranslatePoint(new Point(0, 0), this) is not { } origin) yield break;
        var text = _editor.Text ?? "";

        var tops = new double[lines.Count];
        var y = 0.0;
        for (var i = 0; i < lines.Count; i++)
        {
            tops[i] = y;
            y += lines[i].Height;
        }

        // Gutter Y = origin.Y + layout Y. Visible when top+height >= 0 and top <= Bounds.Height.
        var viewTop = -origin.Y;
        var viewBottom = Bounds.Height - origin.Y;
        var first = FindFirstVisible(lines, tops, viewTop);
        var last = FindLastVisible(lines, tops, viewBottom);
        if (includeMargin)
        {
            first = Math.Max(0, first - 1);
            last = Math.Min(lines.Count - 1, last + 1);
        }
        if (first >= lines.Count || first > last) yield break;

        var number = LogicalNumber(text, lines[first].FirstTextSourceIndex);
        var scanned = lines[first].FirstTextSourceIndex;
        for (var i = first; i <= last; i++)
        {
            var line = lines[i];
            var start = line.FirstTextSourceIndex;
            for (; scanned < Math.Min(start, text.Length); scanned++)
                if (IsLineBreak(text, scanned)) number++;
            if (scanned < start) scanned = start;
            if (!IsLogicalStart(text, start)) continue;
            var top = origin.Y + tops[i];
            if (includeMargin || (top + line.Height >= 0 && top <= Bounds.Height))
                yield return new GutterRow(number, top, line.Height);
        }
    }

    static int FindFirstVisible(IReadOnlyList<Avalonia.Media.TextFormatting.TextLine> lines, double[] tops, double viewTop)
    {
        var lo = 0;
        var hi = lines.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (tops[mid] + lines[mid].Height >= viewTop) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    static int FindLastVisible(IReadOnlyList<Avalonia.Media.TextFormatting.TextLine> lines, double[] tops, double viewBottom)
    {
        var lo = 0;
        var hi = lines.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (tops[mid] <= viewBottom) lo = mid + 1;
            else hi = mid;
        }
        return lo - 1;
    }

    static int LogicalNumber(string text, int start)
    {
        var number = 1;
        var end = Math.Min(Math.Max(0, start), text.Length);
        for (var i = 0; i < end; i++)
            if (IsLineBreak(text, i)) number++;
        return number;
    }

    static bool IsLogicalStart(string text, int start) =>
        start == 0 || (start > 0 && start <= text.Length && (text[start - 1] is '\n' or '\r'));

    static bool IsLineBreak(string text, int i) =>
        text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'));
}
