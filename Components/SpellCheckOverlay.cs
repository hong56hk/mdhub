using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Components;

// Item 74: red wavy underlines under misspelled words of a Markdown editor. Not hit-testable.
// Checking runs in the background, debounced while typing: the visible lines first (they get underlined), then the
// rest of the document for the status-bar count and F7.
public sealed class SpellCheckOverlay : Control
{
    public static TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(300);
    private static readonly IBrush UnderlineBrush = ThemeService.Brush("Danger");
    private readonly DispatcherTimer _timer;
    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;
    private IReadOnlyList<SpellWord> _visible = [];
    private IReadOnlyList<SpellWord> _all = [];
    private string? _checkedText;
    private int _generation;

    public SpellCheckOverlay()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = Debounce };
        _timer.Tick += (_, _) => { _timer.Stop(); Run(); };
        AttachedToVisualTree += (_, _) => { EditorBehavior.Changed += Schedule; SpellCheck.Changed += Schedule; Mdhub.Localization.Loc.Changed += Schedule; };
        DetachedFromVisualTree += (_, _) => { EditorBehavior.Changed -= Schedule; SpellCheck.Changed -= Schedule; Mdhub.Localization.Loc.Changed -= Schedule; };
    }

    // The whole document's misspelling count; null when spell checking is off or the file is not Markdown.
    public int? Count { get; private set; }
    public event Action<int?>? CountChanged;
    public bool IsBusy { get; private set; }
    public IReadOnlyList<SpellWord> Visible => _visible;
    public IReadOnlyList<SpellWord> All => _all;
    private bool Active => _editor is MarkdownTextBox { HighlightMarkdown: true } && SpellCheck.Enabled;

    public void Attach(TextBox editor)
    {
        if (editor == _editor) { Schedule(); return; }
        Unhook();
        _editor = editor;
        _editor.PropertyChanged += OnEditorProperty;
        _visible = []; _all = []; _checkedText = null;
        InvalidateVisual();
        Schedule();
    }

    private void OnEditorProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty || e.Property == MarkdownTextBox.HighlightMarkdownProperty) Schedule();
    }

    private void Hook()
    {
        if (_editor is null || _presenter is not null) return;
        _presenter = _editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_presenter is not null) _presenter.LayoutUpdated += OnLayout;
        if (_scroll is not null) _scroll.ScrollChanged += OnScroll;
    }

    private void Unhook()
    {
        if (_editor is not null) _editor.PropertyChanged -= OnEditorProperty;
        if (_presenter is not null) _presenter.LayoutUpdated -= OnLayout;
        if (_scroll is not null) _scroll.ScrollChanged -= OnScroll;
        _presenter = null; _scroll = null;
    }

    private void OnLayout(object? sender, EventArgs e) => InvalidateVisual();
    private void OnScroll(object? sender, ScrollChangedEventArgs e) { InvalidateVisual(); Schedule(); }

    public void Schedule()
    {
        _timer.Stop();
        _timer.Interval = Debounce;
        _timer.Start();
    }

    // The [from, to) character range of the lines in view (the whole text when it cannot be measured yet).
    public (int From, int To) VisibleRange()
    {
        Hook();
        var text = _editor?.Text ?? "";
        if (_presenter?.TextLayout is not { } layout || _scroll?.Presenter is not Visual viewport || viewport.Bounds.Height <= 0
            || viewport.TranslatePoint(default, _presenter) is not { } top) return (0, text.Length);
        var from = layout.HitTestPoint(new Point(0, Math.Max(0, top.Y))).TextPosition;
        var to = layout.HitTestPoint(new Point(1e7, top.Y + viewport.Bounds.Height)).TextPosition;
        from = Math.Clamp(from, 0, text.Length); to = Math.Clamp(to, from, text.Length);
        while (from > 0 && text[from - 1] != '\n') from--;
        while (to < text.Length && text[to] != '\n') to++;
        return (from, to);
    }

    private void Run()
    {
        var generation = ++_generation;
        if (!Active || _editor is null)
        {
            _visible = []; _all = []; _checkedText = null; IsBusy = false;
            SetCount(null); InvalidateVisual();
            return;
        }
        var editor = _editor;
        var text = editor.Text ?? "";
        var (from, to) = VisibleRange();
        var language = SpellCheck.ActiveLanguage;
        var ignored = SpellCheck.Ignored(editor);
        IsBusy = true;
        Task.Run(() =>
        {
            var visible = SpellCheck.Misspellings(text, language, ignored, from, to);
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation || editor != _editor) return;
                _visible = visible; InvalidateVisual();
            });
            var all = SpellCheck.Misspellings(text, language, ignored);
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation || editor != _editor) return;
                _all = all; _checkedText = text; IsBusy = false;
                if ((editor.Text ?? "") != text) { Schedule(); }
                SetCount(all.Count);
            });
        });
    }

    // Tests and F7: check now, synchronously.
    public void CheckNow()
    {
        _timer.Stop();
        ++_generation;
        IsBusy = false;
        if (!Active || _editor is null) { _visible = []; _all = []; _checkedText = null; SetCount(null); InvalidateVisual(); return; }
        var text = _editor.Text ?? "";
        var (from, to) = VisibleRange();
        _all = SpellCheck.Misspellings(text, SpellCheck.ActiveLanguage, SpellCheck.Ignored(_editor));
        _visible = _all.Where(w => w.End > from && w.Start < to).ToList();
        _checkedText = text;
        SetCount(_all.Count);
        InvalidateVisual();
    }

    private void SetCount(int? count)
    {
        if (Count == count) return;
        Count = count;
        CountChanged?.Invoke(count);
    }

    // The misspelled word at a text offset (checked now, so the context menu is never stale).
    public SpellWord? MisspellingAt(int index)
    {
        if (!Active || _editor?.Text is not { } text) return null;
        index = Math.Clamp(index, 0, text.Length);
        var start = index; while (start > 0 && text[start - 1] != '\n') start--;
        var end = index; while (end < text.Length && text[end] != '\n') end++;
        var words = SpellTokenizer.Words(text, start, end);
        if (SpellTokenizer.WordAt(words, index) is not { } word) return null;
        return SpellCheck.IsCorrect(word.Word, SpellCheck.ActiveLanguage, SpellCheck.Ignored(_editor)) ? null : word;
    }

    // Replaces a misspelling with a suggestion (one undo step) and re-checks.
    public void Replace(SpellWord word, string replacement)
    {
        if (_editor is not { IsReadOnly: false } editor || editor.Text is not { } text || word.End > text.Length || text.Substring(word.Start, word.Length) != word.Word && text.Substring(word.Start, word.Length).Replace('\u2019', '\'') != word.Word) return;
        editor.SelectionStart = word.Start;
        editor.SelectionEnd = word.End;
        editor.SelectedText = replacement;
        editor.SelectionStart = editor.SelectionEnd = word.Start + replacement.Length;
        Schedule();
    }

    // F7: selects the next misspelling after the caret, wrapping to the top. False when there is none.
    public bool Next()
    {
        if (!Active || _editor is null) return false;
        if (_checkedText != (_editor.Text ?? "") || IsBusy) CheckNow();
        if (_all.Count == 0) return false;
        var caret = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
        var next = _all.FirstOrDefault(w => w.Start >= caret, _all[0]);
        _editor.SelectionStart = next.Start;
        _editor.SelectionEnd = next.End;
        _editor.Focus();
        return true;
    }

    // Underline segments in overlay coordinates, for rendering and tests.
    public IEnumerable<Rect> Underlines()
    {
        Hook();
        if (_presenter?.TextLayout is not { } layout || _editor is null) yield break;
        var length = _editor.Text?.Length ?? 0;
        foreach (var word in _visible)
        {
            if (word.End > length) continue;
            foreach (var rect in layout.HitTestTextRange(word.Start, word.Length))
                if (_presenter.TranslatePoint(rect.BottomLeft, this) is { } bottomLeft)
                    yield return new Rect(bottomLeft.X, bottomLeft.Y - 3, rect.Width, 3);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_visible.Count == 0) return;
        var clip = new Rect(Bounds.Size);
        if (_scroll?.Presenter is Visual viewport && viewport.TranslatePoint(default, this) is { } origin)
            clip = clip.Intersect(new Rect(origin, viewport.Bounds.Size));
        var pen = new Pen(UnderlineBrush, 1);
        using (context.PushClip(clip))
            foreach (var r in Underlines())
            {
                if (!r.Intersects(clip) || r.Width <= 0) continue;
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    const double step = 2;
                    var up = true;
                    g.BeginFigure(new Point(r.X, r.Bottom - 1), false);
                    for (var x = r.X + step; x <= r.Right + 0.01; x += step, up = !up)
                        g.LineTo(new Point(x, up ? r.Top + 0.5 : r.Bottom - 1));
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, pen, geometry);
            }
    }
}
