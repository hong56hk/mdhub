using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 102a: a read-only PDF tab. Pages scroll vertically and are rendered lazily (the visible page and its
// neighbours stay in memory). Zoom uses the image viewer's step presets (Ctrl+= / Ctrl+- / Ctrl+0) through the
// shortcut registry while this viewer is shown. ScrollToPage is the hook for item 102d.
public partial class PdfViewer : UserControl
{
    const int Neighbours = 1;
    PdfDocument? _document;
    readonly List<PageSlot> _slots = [];
    double _scale = 1;
    int _currentPage = 1;
    bool _fitting;

    public PdfViewer()
    {
        InitializeComponent();
        Scroller.ScrollChanged += (_, _) => { UpdateCurrentPage(); RefreshVisible(); };
        AttachedToVisualTree += (_, _) => Loc.Changed += Relocalize;
        DetachedFromVisualTree += (_, _) => Loc.Changed -= Relocalize;
        LayoutUpdated += (_, _) => { if (_fitting) FitToWindow(); };
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

    public void Show(PdfDocument? document)
    {
        if (ReferenceEquals(_document, document) && _slots.Count == (document?.PageCount ?? 0)) { Relocalize(); return; }
        ClearSlots();
        _document = document;
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
                _slots.Add(slot);
                Pages.Children.Add(slot.Host);
            }
            SizeSlots();
        }
        ZoomChanged?.Invoke(ZoomPercent);
        PageChanged?.Invoke(_currentPage);
    }

    public bool StepZoom(int direction)
    {
        if (_document is not { Error: PdfError.None }) return false;
        _fitting = false;
        return SetScale(ImageZoom.StepZoom(_scale, direction));
    }

    public bool FitToWindow()
    {
        if (_document is not { Error: PdfError.None, PageCount: > 0 }) return false;
        _fitting = true;
        var viewW = Math.Max(0, Scroller.Viewport.Width - 32);
        var viewH = Math.Max(0, Scroller.Viewport.Height - 32);
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
        RefreshVisible();
    }

    bool SetScale(double scale)
    {
        scale = ImageZoom.Clamp(scale);
        var page = _currentPage;
        if (Math.Abs(scale - _scale) < 1e-6) { SizeSlots(); RefreshVisible(); return true; }
        _scale = scale;
        SizeSlots();
        UpdateZoomLabel();
        ZoomChanged?.Invoke(ZoomPercent);
        // Re-render at the new scale (old bitmaps are the wrong size).
        foreach (var slot in _slots) slot.Release();
        ScrollToPage(page);
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
        foreach (var slot in _slots) slot.Release();
        _slots.Clear();
        Pages.Children.Clear();
    }

    sealed class PageSlot
    {
        public Border Host { get; } = new()
        {
            Background = ThemeService.Brush("EditorBackground"),
            BorderBrush = ThemeService.Brush("BorderSubtle"),
            BorderThickness = new Thickness(1)
        };
        public Image Image { get; } = new() { Stretch = Stretch.Fill };
        public WriteableBitmap? Bitmap { get; set; }
        public double Scale { get; set; }
        public PageSlot() { Host.Child = Image; }
        public void Release()
        {
            Image.Source = null;
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }
}