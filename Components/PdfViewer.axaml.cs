using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;


public enum PdfTool { Hand, Select }

// Item 102a: a read-only PDF tab. Pages scroll vertically and are rendered lazily (the visible page and its
// neighbours stay in memory). Zoom uses the image viewer's step presets (Ctrl+= / Ctrl+- / Ctrl+0) through the
// shortcut registry while this viewer is shown. ScrollToPage is the hook for item 102d.
// Item 102b: drag to select text; Ctrl+C (Cmd+C) copies it as plain text via pdf.copy (ShortcutArea.Pdf).
public partial class PdfViewer : UserControl
{
    const int Neighbours = 1;
    PdfDocument? _document;
    readonly List<PageSlot> _slots = [];
    IReadOnlyList<PdfDocument.PdfChar>[]? _glyphs;
    double _scale = 1;
    int _currentPage = 1;
    bool _fitting;
    bool _fitQueued;
    bool _dragging;
    bool _panning;
    Point _panStart;
    Vector _panOffset;
    PdfTool _tool = PdfTool.Hand;
    int _anchorPage, _anchorIndex, _endPage, _endIndex;

    public PdfViewer()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScroll;
        Scroller.SizeChanged += (_, _) => QueueFit();
        AttachedToVisualTree += (_, _) => Loc.Changed += Relocalize;
        DetachedFromVisualTree += (_, _) => Loc.Changed -= Relocalize;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        HandButton.Click += (_, _) => Tool = PdfTool.Hand;
        SelectButton.Click += (_, _) => Tool = PdfTool.Select;
        ZoomInButton.Click += (_, _) => StepZoom(1);
        ZoomOutButton.Click += (_, _) => StepZoom(-1);
        OpenButton.Click += (_, _) =>
        {
            if (_document?.FilePath is { Length: > 0 } path) OpenDefaultRequested?.Invoke(path);
        };
        NotesButton.Click += (_, _) => NotesRequested?.Invoke();
        Scroller.ContextMenu = CreateNotesMenu();
        PageInput.KeyDown += OnPageKey;
        PageInput.LostFocus += (_, _) => CommitPageInput();
        AttachedToVisualTree += (_, _) => { Shortcuts.Changed += UpdateZoomTips; UpdateZoomTips(); };
        DetachedFromVisualTree += (_, _) => Shortcuts.Changed -= UpdateZoomTips;
        UpdateToolChrome();
        UpdateTools();
    }

    public PdfDocument? Document => _document;
    public double Scale => _scale;
    public int ZoomPercent => ImageZoom.Percent(_scale);
    public int PageCount => _document?.PageCount ?? 0;
    public int CurrentPage => _currentPage;
    // Pages that currently hold a bitmap (0-based). Exposed for the lazy-render test.
    public IReadOnlyList<int> CachedPages => _slots.Select((s, i) => (s, i)).Where(x => x.s.Bitmap is not null).Select(x => x.i).ToList();
    public event Action<int>? ZoomChanged;
    public event Action<int>? PageChanged;
    public event Action<string>? OpenDefaultRequested;
    public event Action? NotesRequested;
    public event Action? AddToNotesRequested;
    bool _notesOpen;
    public bool NotesOpen => _notesOpen;
    public void SetNotesOpen(bool open)
    {
        if (_notesOpen == open) return;
        _notesOpen = open;
        UpdateToolChrome();
    }

    public PdfTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            UpdateToolChrome();
        }
    }

    // Item 102b / 102c: the current selection as plain text, and the 1-based page where it starts.
    public string SelectedText { get; private set; } = "";
    public int SelectionPage { get; private set; }
    public bool HasSelection => SelectedText.Length > 0;
    public string? LastCopiedText { get; private set; }
    public event Action? SelectionChanged;

    public void Show(PdfDocument? document)
    {
        if (ReferenceEquals(_document, document) && _slots.Count == (document?.PageCount ?? 0)) { Relocalize(); return; }
        ClearSelection(notify: false);
        ClearSlots();
        _document = document;
        _glyphs = null;
        _currentPage = 1;
        _scale = 1;
        _fitting = document is { Error: PdfError.None, PageCount: > 0 };
        ErrorText.IsVisible = document is { Error: not PdfError.None };
        Scroller.IsVisible = document is { Error: PdfError.None };
        Toolbar.IsVisible = document is not null;
        Relocalize();
        if (document is { Error: PdfError.None })
        {
            for (var i = 0; i < document.PageCount; i++)
            {
                var slot = new PageSlot();
                var page = i;
                slot.Host.PointerPressed += (s, e) => OnPagePressed(page, e);
                slot.Host.PointerMoved += (s, e) => OnPageMoved(page, e);
                slot.Host.PointerReleased += (s, e) => OnPageReleased(e);
                _slots.Add(slot);
                Pages.Children.Add(slot.Host);
            }
            SizeSlots();
        }
        UpdateTools();
        UpdatePageField();
        UpdateZoomTips();
        ZoomChanged?.Invoke(ZoomPercent);
        PageChanged?.Invoke(_currentPage);
        if (_fitting) QueueFit();
    }

    public bool StepZoom(int direction)
    {
        if (_document is not { Error: PdfError.None }) return false;
        _fitting = false;
        return SetScale(ImageZoom.StepZoom(_scale, direction));
    }

    // Pages.Margin is 16 per side (32) and each page Border is 1 per side (2). A 1 DIP slack
    // keeps the fitted page strictly inside the viewport so an Auto scrollbar cannot appear,
    // shrink the viewport, change the fit scale, and disappear again -- that oscillation is
    // what Avalonia reports as "Infinite layout loop detected" (153 render-callback passes).
    const double FitChromeX = 32 + 2 + 1;
    const double FitChromeY = 32 + 2 + 16 + 1; // also the gap under each page slot

    void OnScroll(object? sender, ScrollChangedEventArgs e)
    {
        UpdateCurrentPage();
        RefreshVisible();
        if (e.ViewportDelta.X != 0 || e.ViewportDelta.Y != 0) QueueFit();
    }

    // Defer off the current render/layout callback so a size change cannot re-enter
    // MediaContext.FireInvokeOnRenderCallbacks in the same tick.
    void QueueFit()
    {
        if (!_fitting || _fitQueued) return;
        _fitQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitQueued = false;
            if (_fitting) FitToWindow();
        }, DispatcherPriority.Loaded);
    }

    public bool FitToWindow()
    {
        if (_document is not { Error: PdfError.None, PageCount: > 0 }) return false;
        _fitting = true;
        var viewW = Math.Max(0, Scroller.Viewport.Width - FitChromeX);
        var viewH = Math.Max(0, Scroller.Viewport.Height - FitChromeY);
        if (viewW <= 0 || viewH <= 0) return false;
        var (w, h) = _document.Pages[Math.Clamp(_currentPage - 1, 0, _document.PageCount - 1)];
        var next = ImageZoom.Fit(w, h, viewW, viewH);
        return SetScale(next);
    }

    // 1-based. Centres the page in the viewport as far as the scroll extent allows.
    public void ScrollToPage(int pageNumber)
    {
        if (_document is not { Error: PdfError.None } || _slots.Count == 0) return;
        var index = Math.Clamp(pageNumber, 1, PageCount) - 1;
        var slot = _slots[index].Host;
        slot.InvalidateMeasure();
        var y = 0.0;
        for (var i = 0; i < index; i++) y += _slots[i].Host.Height + _slots[i].Host.Margin.Top + _slots[i].Host.Margin.Bottom;
        Scroller.Offset = new Vector(Scroller.Offset.X, y);
        _currentPage = index + 1;
        PageChanged?.Invoke(_currentPage);
        UpdatePageField();
        RefreshVisible();
    }

    public async Task CopyAsync()
    {
        if (!HasSelection) return;
        LastCopiedText = SelectedText;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(SelectedText);
    }

    bool SetScale(double scale)
    {
        scale = ImageZoom.Clamp(scale);
        var page = _currentPage;
        // Unchanged scale must be a true no-op: rewriting Width/Height or rebuilding the
        // selection overlay from a layout callback is enough to queue another layout pass.
        if (Math.Abs(scale - _scale) < 1e-6) return true;
        _scale = scale;
        SizeSlots();
        UpdateZoomLabel();
        ZoomChanged?.Invoke(ZoomPercent);
        UpdateZoomButtons();
        // Re-render at the new scale (old bitmaps are the wrong size).
        foreach (var slot in _slots) slot.Release();
        ScrollToPage(page);
        PaintSelection();
        return true;
    }

    void SizeSlots()
    {
        if (_document is null) return;
        for (var i = 0; i < _slots.Count; i++)
        {
            var (w, h) = _document.Pages[i];
            _slots[i].Host.Width = Math.Max(1, w * _scale);
            _slots[i].Host.Height = Math.Max(1, h * _scale);
            _slots[i].Host.Margin = new Thickness(0, 0, 0, 16);
        }
    }

    void RefreshVisible()
    {
        if (_document is not { Error: PdfError.None } || _slots.Count == 0) return;
        var viewport = new Rect(0, Scroller.Offset.Y, Math.Max(1, Scroller.Viewport.Width), Math.Max(1, Scroller.Viewport.Height));
        var first = 0; var last = _slots.Count - 1;
        var found = false;
        var y = 0.0;
        for (var i = 0; i < _slots.Count; i++)
        {
            var h = _slots[i].Host.Height + _slots[i].Host.Margin.Bottom;
            var rect = new Rect(0, y, 1, h);
            if (rect.Intersects(viewport))
            {
                if (!found) { first = last = i; found = true; }
                else last = i;
            }
            y += h;
        }
        if (!found) return;
        first = Math.Max(0, first - Neighbours);
        last = Math.Min(_slots.Count - 1, last + Neighbours);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (i >= first && i <= last) EnsureBitmap(i);
            else _slots[i].Release();
        }
    }

    void EnsureBitmap(int index)
    {
        var slot = _slots[index];
        if (slot.Bitmap is not null && Math.Abs(slot.Scale - _scale) < 1e-6) return;
        slot.Release();
        if (_document?.Render(index, _scale) is not { } frame) return;
        var bmp = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock()) Marshal.Copy(frame.Bgra, 0, fb.Address, frame.Bgra.Length);
        slot.Bitmap = bmp;
        slot.Scale = _scale;
        slot.Image.Source = bmp;
    }

    void UpdateCurrentPage()
    {
        if (_slots.Count == 0) return;
        var mid = Scroller.Offset.Y + Math.Max(1, Scroller.Viewport.Height) * 0.15;
        var y = 0.0;
        var page = 1;
        for (var i = 0; i < _slots.Count; i++)
        {
            var h = _slots[i].Host.Height + _slots[i].Host.Margin.Bottom;
            if (mid >= y) page = i + 1;
            y += h;
        }
        if (page == _currentPage) return;
        _currentPage = page;
        PageChanged?.Invoke(_currentPage);
        UpdatePageField();
    }

    void Relocalize()
    {
        UpdateZoomLabel();
        if (_document is { Error: PdfError.Encrypted }) ErrorText.Text = Loc.T("PdfEncrypted");
        else if (_document is { Error: PdfError.Corrupt }) ErrorText.Text = Loc.T("PdfCorrupt");
        else ErrorText.Text = "";
    }

    void UpdateZoomLabel() => ZoomLabel.Text = _document is { Error: PdfError.None } ? Loc.F("ImageZoomPercent", ZoomPercent) : "";

    void ClearSlots()
    {
        foreach (var slot in _slots) { slot.ClearOverlay(); slot.Release(); }
        _slots.Clear();
        Pages.Children.Clear();
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (ShortcutKeys.Match(e, s => s.PdfOnly) != "pdf.copy") return;
        e.Handled = true;
        _ = CopyAsync();
    }

    void OnPagePressed(int page, PointerPressedEventArgs e)
    {
        Focus();
        if (!e.GetCurrentPoint(_slots[page].Host).Properties.IsLeftButtonPressed) return;
        if (Tool == PdfTool.Hand)
        {
            _panning = true;
            _panStart = e.GetPosition(Scroller);
            _panOffset = Scroller.Offset;
            e.Pointer.Capture(_slots[page].Host);
            e.Handled = true;
            return;
        }
        if (Tool != PdfTool.Select) return;
        if (Hit(page, e.GetPosition(_slots[page].Image), nearOnly: true) is { } hit)
        {
            _dragging = true;
            e.Pointer.Capture(_slots[page].Host);
            _anchorPage = _endPage = hit.Page;
            _anchorIndex = _endIndex = hit.Index;
            ApplySelection();
        }
        else ClearSelection();
        e.Handled = true;
    }

    void OnPageMoved(int page, PointerEventArgs e)
    {
        if (_panning)
        {
            var now = e.GetPosition(Scroller);
            var delta = now - _panStart;
            Scroller.Offset = new Vector(Math.Max(0, _panOffset.X - delta.X), Math.Max(0, _panOffset.Y - delta.Y));
            e.Handled = true;
            return;
        }
        if (!_dragging || Tool != PdfTool.Select) return;
        var pos = e.GetPosition(_slots[page].Image);
        if (Hit(page, pos, nearOnly: false) is { } hit)
        {
            _endPage = hit.Page;
            _endIndex = hit.Index;
            ApplySelection();
        }
        e.Handled = true;
    }

    void OnPageReleased(PointerReleasedEventArgs e)
    {
        if (_panning || _dragging)
        {
            _panning = false;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    (int Page, int Index)? Hit(int page, Point imagePoint, bool nearOnly)
    {
        var glyphs = Glyphs(page);
        if (glyphs.Count == 0 || _document is null) return null;
        var height = _document.Pages[page].Height;
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < glyphs.Count; i++)
        {
            var r = ImageRect(glyphs[i], height);
            if (r.Width <= 0 || r.Height <= 0) continue;
            if (r.Contains(imagePoint)) return (page, i);
            var cx = Math.Clamp(imagePoint.X, r.X, r.X + r.Width);
            var cy = Math.Clamp(imagePoint.Y, r.Y, r.Y + r.Height);
            var dist = Math.Sqrt((imagePoint.X - cx) * (imagePoint.X - cx) + (imagePoint.Y - cy) * (imagePoint.Y - cy));
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        if (best < 0) return null;
        if (nearOnly)
        {
            var font = Math.Max(8, glyphs[best].FontSize) * _scale;
            if (bestDist > Math.Max(12, font * 0.8)) return null;
        }
        return (page, best);
    }

    IReadOnlyList<PdfDocument.PdfChar> Glyphs(int page)
    {
        _glyphs ??= new IReadOnlyList<PdfDocument.PdfChar>[_document!.PageCount];
        return _glyphs[page] ??= _document!.Characters(page);
    }

    Rect ImageRect(PdfDocument.PdfChar c, double pageHeight)
    {
        var left = Math.Min(c.Left, c.Right);
        var right = Math.Max(c.Left, c.Right);
        double top, bottom;
        if (c.Top <= c.Bottom)
        {
            top = c.Top;
            bottom = c.Bottom;
        }
        else
        {
            top = pageHeight - c.Top;
            bottom = pageHeight - c.Bottom;
            if (top > bottom) (top, bottom) = (bottom, top);
        }
        return new Rect(left * _scale, top * _scale, Math.Max(0.5, (right - left) * _scale), Math.Max(0.5, (bottom - top) * _scale));
    }

    void ApplySelection()
    {
        var start = (_anchorPage, _anchorIndex);
        var end = (_endPage, _endIndex);
        if (start.CompareTo(end) > 0) (start, end) = (end, start);
        var text = BuildText(start, end);
        SelectedText = text;
        SelectionPage = start.Item1 + 1;
        PaintSelection();
        SelectionChanged?.Invoke();
    }

    string BuildText((int Page, int Index) start, (int Page, int Index) end)
    {
        var sb = new StringBuilder();
        for (var p = start.Page; p <= end.Page; p++)
        {
            var glyphs = Glyphs(p);
            if (glyphs.Count == 0) continue;
            var from = p == start.Page ? start.Index : 0;
            var to = p == end.Page ? end.Index : glyphs.Count - 1;
            from = Math.Clamp(from, 0, glyphs.Count - 1);
            to = Math.Clamp(to, 0, glyphs.Count - 1);
            if (p > start.Page && sb.Length > 0) sb.Append('\n');
            double? lastTop = null;
            for (var i = from; i <= to; i++)
            {
                var ch = glyphs[i].Char;
                if (ch == '\0') continue;
                if (lastTop is { } y && Math.Abs(glyphs[i].Top - y) > Math.Max(2, glyphs[i].FontSize * 0.4))
                    sb.Append('\n');
                lastTop = glyphs[i].Top;
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    void PaintSelection()
    {
        // Draw in Render only (InvalidateVisual). Adding a Rectangle per glyph to a Canvas would
        // invalidate layout on every pointer move; with the default fit-to-window hook that used
        // to run from LayoutUpdated, that re-entered MediaContext.FireInvokeOnRenderCallbacks
        // until Avalonia threw "Infinite layout loop detected" (Hong's crash when selecting text).
        if (_document is null || _anchorIndex < 0 || _endIndex < 0)
        {
            foreach (var slot in _slots) slot.ClearOverlay();
            return;
        }
        var start = (Page: _anchorPage, Index: _anchorIndex);
        var end = (Page: _endPage, Index: _endIndex);
        if (start.CompareTo(end) > 0) (start, end) = (end, start);
        var brush = ThemeService.Brush("PreviewSelectionBackground");
        for (var p = 0; p < _slots.Count; p++)
        {
            if (p < start.Page || p > end.Page)
            {
                _slots[p].ClearOverlay();
                continue;
            }
            var glyphs = Glyphs(p);
            var height = _document.Pages[p].Height;
            var from = p == start.Page ? start.Index : 0;
            var to = p == end.Page ? end.Index : glyphs.Count - 1;
            from = Math.Clamp(from, 0, Math.Max(0, glyphs.Count - 1));
            to = Math.Clamp(to, 0, Math.Max(0, glyphs.Count - 1));
            var rects = new List<Rect>();
            if (glyphs.Count > 0)
            {
                for (var i = from; i <= to; i++)
                {
                    var r = ImageRect(glyphs[i], height);
                    if (r.Width > 0 && r.Height > 0) rects.Add(r);
                }
            }
            _slots[p].Overlay.Set(rects, brush);
        }
    }

    void ClearSelection(bool notify = true)
    {
        _dragging = false;
        _anchorPage = _endPage = 0;
        _anchorIndex = _endIndex = -1;
        var changed = HasSelection || SelectionPage != 0;
        SelectedText = "";
        SelectionPage = 0;
        foreach (var slot in _slots) slot.ClearOverlay();
        if (notify && changed) SelectionChanged?.Invoke();
    }

    ContextMenu CreateNotesMenu()
    {
        var item = LocResources.Item("PdfAddToNotes");
        item.Name = "AddToNotesMenuItem";
        item.Click += (_, _) => AddToNotesRequested?.Invoke();
        var menu = new ContextMenu();
        menu.Items.Add(item);
        menu.Localized();
        menu.Opening += (_, e) =>
        {
            item.IsEnabled = HasSelection;
            if (!HasSelection) e.Cancel = true;
        };
        return menu;
    }

    bool HasUsableDocument => _document is { Error: PdfError.None, PageCount: > 0 };

    void UpdateTools()
    {
        var usable = HasUsableDocument;
        HandButton.IsEnabled = SelectButton.IsEnabled = PageInput.IsEnabled = usable;
        OpenButton.IsEnabled = NotesButton.IsEnabled = _document is { FilePath.Length: > 0 };
        UpdateZoomButtons();
        UpdateToolChrome();
        if (!usable)
        {
            PageInput.Text = "";
            PageCountLabel.Text = "";
        }
    }

    void UpdateToolChrome()
    {
        HandButton.Classes.Set("on", Tool == PdfTool.Hand);
        SelectButton.Classes.Set("on", Tool == PdfTool.Select);
        NotesButton.Classes.Set("on", _notesOpen);
        var cursor = Tool == PdfTool.Hand ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Ibeam);
        foreach (var slot in _slots) slot.Host.Cursor = cursor;
        Scroller.Cursor = Tool == PdfTool.Hand ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    void UpdateZoomButtons()
    {
        var usable = HasUsableDocument;
        ZoomInButton.IsEnabled = usable && ImageZoom.StepZoom(_scale, 1) > _scale + 1e-9;
        ZoomOutButton.IsEnabled = usable && ImageZoom.StepZoom(_scale, -1) < _scale - 1e-9;
    }

    static string Hint(string id) => Shortcuts.Format(id, ShortcutKeys.IsMac).Split(" / ")[0];

    void UpdateZoomTips()
    {
        ToolTip.SetTip(ZoomInButton, Loc.F("ImageZoomInTip", Hint("zoom.in")));
        ToolTip.SetTip(ZoomOutButton, Loc.F("ImageZoomOutTip", Hint("zoom.out")));
    }

    void UpdatePageField()
    {
        if (!HasUsableDocument) { if (!PageInput.IsFocused) { PageInput.Text = ""; PageCountLabel.Text = ""; } return; }
        PageCountLabel.Text = PageCount.ToString();
        if (!PageInput.IsFocused) PageInput.Text = CurrentPage.ToString();
    }

    void OnPageKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitPageInput(); e.Handled = true; }
        else if (e.Key == Key.Escape) { UpdatePageFieldForce(); e.Handled = true; }
    }

    void CommitPageInput()
    {
        if (!HasUsableDocument) { UpdatePageFieldForce(); return; }
        if (!int.TryParse(PageInput.Text, out var n)) { UpdatePageFieldForce(); return; }
        ScrollToPage(Math.Clamp(n, 1, PageCount));
        UpdatePageFieldForce();
    }

    void UpdatePageFieldForce()
    {
        PageCountLabel.Text = HasUsableDocument ? PageCount.ToString() : "";
        PageInput.Text = HasUsableDocument ? CurrentPage.ToString() : "";
    }

    sealed class PageSlot
    {
        public Border Host { get; } = new()
        {
            Background = ThemeService.Brush("PdfPageBackground"),
            BorderBrush = ThemeService.Brush("BorderSubtle"),
            BorderThickness = new Thickness(1)
        };
        public Image Image { get; } = new() { Stretch = Stretch.Fill };
        public SelectionLayer Overlay { get; } = new() { IsHitTestVisible = false };
        public WriteableBitmap? Bitmap { get; set; }
        public double Scale { get; set; }
        public PageSlot()
        {
            var grid = new Grid();
            grid.Children.Add(Image);
            grid.Children.Add(Overlay);
            Host.Child = grid;
        }
        public void ClearOverlay() => Overlay.Clear();
        public void Release()
        {
            Image.Source = null;
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }
    // A non-layout overlay: highlights are drawn in Render so a selection change never
    // invalidates measure/arrange (unlike adding Shape children to a Canvas).
    sealed class SelectionLayer : Control
    {
        readonly List<Rect> _rects = [];
        IBrush? _brush;
        public void Set(IReadOnlyList<Rect> rects, IBrush? brush)
        {
            _rects.Clear();
            _rects.AddRange(rects);
            _brush = brush;
            InvalidateVisual();
        }
        public void Clear()
        {
            if (_rects.Count == 0) return;
            _rects.Clear();
            _brush = null;
            InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            if (_brush is null) return;
            foreach (var r in _rects) context.FillRectangle(_brush, r);
        }
    }
}
