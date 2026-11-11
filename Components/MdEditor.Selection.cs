using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mdhub.Rendering;
using Mdhub.Services;

namespace Mdhub.Components;

// What a preview copy puts on the clipboard (kept for tests and diagnostics).
internal sealed record PreviewClipboard(string Plain, string Html, string CfHtml, string Rtf);

// Mouse / keyboard text selection across the rendered preview and copying it as rendered content (HTML, text, RTF).
public partial class MdEditor
{
    internal static readonly IBrush SelectionBrush = ThemeService.Brush("PreviewSelectionBackground");
    private static Cursor? _ibeam;
    internal static Cursor IBeam => _ibeam ??= new Cursor(StandardCursorType.Ibeam);
    private static readonly DataFormat<byte[]> HtmlFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "HTML Format" : OperatingSystem.IsMacOS() ? "public.html" : "text/html");
    private static readonly DataFormat<byte[]> RtfFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "Rich Text Format" : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf");

    public event Action<string>? LinkClicked;
    private PreviewSelection? _selection;
    private PreviewPosition _anchor, _caret;
    private bool _dragging;
    private Point _linkPress;
    private string? _linkHref;
    internal PreviewSelection? Selection => _selection;
    internal bool HasPreviewSelection => _selection is not null && _anchor != _caret;
    internal (PreviewPosition From, PreviewPosition To) PreviewSelectionRange => _anchor <= _caret ? (_anchor, _caret) : (_caret, _anchor);
    internal PreviewClipboard? LastCopy { get; private set; }
    internal bool PreviewHasFocus => _selection is not null && PreviewScroll.IsKeyboardFocusWithin && !IsEditingPreview;

    private void InitSelection()
    {
        PreviewScroll.Focusable = true;
        PreviewScroll.AddHandler(PointerPressedEvent, OnSelectPressed, RoutingStrategies.Bubble, true);
        PreviewScroll.AddHandler(PointerMovedEvent, OnSelectMoved, RoutingStrategies.Bubble, true);
        PreviewScroll.AddHandler(PointerReleasedEvent, OnSelectReleased, RoutingStrategies.Bubble, true);
        PreviewScroll.AddHandler(KeyDownEvent, OnSelectKey, RoutingStrategies.Bubble);
        PreviewContent.LayoutUpdated += (_, _) => PreviewSelectionLayer.InvalidateVisual();
        PreviewSelectionLayer.Owner = this;
    }

    // Called after every preview render (null render: nothing selectable).
    private void ResetSelection()
    {
        _dragging = false;
        _selection = _render is null ? null : new PreviewSelection(PreviewContent);
        _anchor = _caret = default;
        if (_selection is not null)
            foreach (var item in _selection.Items)
                if (item is TextBlock) item.Cursor = IBeam;
        PreviewSelectionLayer.InvalidateVisual();
    }

    internal void SetPreviewSelection(PreviewPosition anchor, PreviewPosition caret)
    {
        (_anchor, _caret) = (anchor, caret);
        PreviewSelectionLayer.InvalidateVisual();
    }

    internal void ClearPreviewSelection() => SetPreviewSelection(_caret, _caret);

    internal void SelectAllPreview()
    {
        if (_selection is { } selection) SetPreviewSelection(selection.Start, selection.End);
    }

    internal IReadOnlyList<Rect> SelectionRects(Visual target) =>
        HasPreviewSelection ? _selection!.Rects(PreviewSelectionRange.From, PreviewSelectionRange.To, target) : [];

    private bool Ignored(object? source)
    {
        for (var v = source as Visual; v is not null && v != PreviewScroll; v = v.GetVisualParent())
            if (v is ScrollBar || _editBox is not null && v == _editBox) return true;
        return false;
    }

    private void OnSelectPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selection is not { } selection || Ignored(e.Source)) return;
        var point = e.GetCurrentPoint(PreviewContent);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (!IsEditingPreview) PreviewScroll.Focus();
        if (e.ClickCount > 1) return; // the second click of a double-click edits the block (DoubleTapped)
        _linkHref = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? null : HrefAt(e.Source as Visual, point.Position);
        _linkPress = point.Position;
        if (selection.HitTest(point.Position) is not { } at) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selection is not null) _caret = at;
        else _anchor = _caret = at;
        _dragging = true;
        PreviewSelectionLayer.InvalidateVisual();
    }

    private void OnSelectReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragging && _anchor != _caret;
        _dragging = false;
        if (dragged || _linkHref is null || e.InitialPressMouseButton != MouseButton.Left) { _linkHref = null; return; }
        var now = e.GetPosition(PreviewContent);
        if (Math.Abs(now.X - _linkPress.X) >= 4 || Math.Abs(now.Y - _linkPress.Y) >= 4) { _linkHref = null; return; }
        if (HrefAt(e.Source as Visual, now) != _linkHref) { _linkHref = null; return; }
        var href = _linkHref;
        _linkHref = null;
        if (href.Contains(".pdf", StringComparison.OrdinalIgnoreCase) && href.Contains("#page=", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            LinkClicked?.Invoke(href);
        }
    }

    string? HrefAt(Visual? source, Point inContent)
    {
        TextBlock? block = null;
        for (var v = source; v is not null && v != PreviewScroll; v = v.GetVisualParent())
            if (v is TextBlock tb) { block = tb; break; }
        if (block is null || block.TranslatePoint(new Point(0, 0), PreviewContent) is not { } origin) return null;
        var inherited = PreviewTags.Kind(block) == "a" ? PreviewTags.Value(block) : null;
        if (block.Inlines is not { Count: > 0 } inlines || block.TextLayout is null) return inherited;
        var hit = block.TextLayout.HitTestPoint(inContent - origin);
        return HrefAtOffset(inlines, hit.TextPosition, inherited);
    }

    static string? HrefAtOffset(InlineCollection inlines, int offset, string? inherited)
    {
        string? found = null;
        var pos = 0;
        void Walk(InlineCollection items, string? href)
        {
            foreach (var inline in items)
            {
                if (found is not null) return;
                if (inline is LineBreak) { if (offset == pos) found = href; pos++; continue; }
                if (inline is Run run)
                {
                    var len = run.Text?.Length ?? 0;
                    if (href is not null && offset >= pos && offset <= pos + Math.Max(len, 0)) found = href;
                    pos += len;
                    continue;
                }
                if (inline is Span span)
                    Walk(span.Inlines, PreviewTags.Kind(span) == "a" ? PreviewTags.Value(span) : href);
                else { if (href is not null && offset == pos) found = href; pos++; }
            }
        }
        Walk(inlines, inherited);
        return found;
    }
    private void OnSelectMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _selection is not { } selection) return;
        var point = e.GetCurrentPoint(PreviewContent);
        if (!point.Properties.IsLeftButtonPressed) { _dragging = false; return; }
        // Dragging past the top or bottom edge scrolls the preview.
        var inView = e.GetPosition(PreviewScroll);
        if (inView.Y < 0 || inView.Y > PreviewScroll.Viewport.Height)
        {
            var max = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
            PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, Math.Clamp(PreviewScroll.Offset.Y + (inView.Y < 0 ? -20 : 20), 0, max));
        }
        if (selection.HitTest(point.Position) is { } at && at != _caret) { _caret = at; PreviewSelectionLayer.InvalidateVisual(); }
    }

    private async void OnSelectKey(object? sender, KeyEventArgs e)
    {
        if (IsEditingPreview || _selection is null) return;
        var id = ShortcutKeys.Match(e, s => s.Id is "edit.selectAll" or "edit.copy");
        if (id == "edit.selectAll") { e.Handled = true; SelectAllPreview(); }
        else if (id == "edit.copy" && HasPreviewSelection) { e.Handled = true; await CopyPreviewAsync(); }
    }

    internal static RichCopyStyle CopyStyle()
    {
        static string Hex(string key)
        {
            var c = ThemeService.Lookup(key, AppTheme.Light);
            var a = c.A / 255.0; // blend translucent palette colours over a white page
            byte Mix(byte v) => (byte)Math.Round(v * a + 255 * (1 - a));
            return $"#{Mix(c.R):X2}{Mix(c.G):X2}{Mix(c.B):X2}";
        }
        return new RichCopyStyle(Hex("LinkForeground"), Hex("CodeBlockBackground"), Hex("InlineCodeBackground"), Hex("TableBorder"), Hex("TableHeaderBackground"), Hex("QuoteBorder"));
    }

    // Copies the selection as rendered content; returns false when nothing is selected or the clipboard failed.
    internal async Task<bool> CopyPreviewAsync()
    {
        if (!HasPreviewSelection) return false;
        var (from, to) = PreviewSelectionRange;
        var doc = _selection!.Build(from, to);
        var style = CopyStyle();
        var html = RichCopy.Html(doc, style);
        var copy = new PreviewClipboard(RichCopy.Plain(doc), html, RichCopy.CfHtml(html), RichCopy.Rtf(doc, style));
        LastCopy = copy;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
        try
        {
            var item = new DataTransferItem();
            item.SetText(copy.Plain);
            item.Set(HtmlFormat, Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? copy.CfHtml : copy.Html));
            item.Set(RtfFormat, Encoding.ASCII.GetBytes(copy.Rtf));
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            MessageChanged?.Invoke(Mdhub.Localization.Loc.F("CopyFailed", ex.Message));
            return false;
        }
    }
}

// Paints the preview's own text selection behind the rendered text.
public sealed class PreviewSelectionOverlay : Control
{
    internal MdEditor? Owner { get; set; }
    public override void Render(DrawingContext context)
    {
        if (Owner is null) return;
        foreach (var rect in Owner.SelectionRects(this)) context.FillRectangle(MdEditor.SelectionBrush, rect);
    }
}