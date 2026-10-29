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
    private static readonly IBrush NumberBrush = Brush.Parse("#6B7280");
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
        if (_editor is null || _presenter?.TextLayout is not { } layout) return result;
        var text = _editor.Text ?? "";
        var number = 1;
        var scanned = 0;
        double y = 0;
        foreach (var line in layout.TextLines)
        {
            var start = line.FirstTextSourceIndex;
            for (; scanned < Math.Min(start, text.Length); scanned++)
                if (text[scanned] == '\n' || (text[scanned] == '\r' && (scanned + 1 >= text.Length || text[scanned + 1] != '\n'))) number++;
            var logicalStart = start == 0 || (start <= text.Length && start > 0 && (text[start - 1] == '\n' || text[start - 1] == '\r'));
            if (logicalStart)
            {
                var top = _presenter.TranslatePoint(new Point(0, y), this)?.Y;
                if (top is { } t && t + line.Height >= 0 && t <= Bounds.Height) result.Add((number, t));
            }
            y += line.Height;
        }
        return result;
    }

    public override void Render(DrawingContext context)
    {
        var right = Bounds.Width - RightPadding;
        using (context.PushClip(new Rect(Bounds.Size)))
            foreach (var (number, top) in VisibleNumbers())
            {
                var formatted = Format(number.ToString(CultureInfo.InvariantCulture));
                context.DrawText(formatted, new Point(right - formatted.WidthIncludingTrailingWhitespace, top));
            }
    }
}