using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Models;
using Mdhub.Text;
using Mdhub.Rendering;

namespace Mdhub.Components;

// The existing formatted Markdown pane; source editing remains in TextEditor.
public partial class MdEditor : UserControl
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private NoteDocument? _active;
    private long _previewVersion;
    public event Action<TextStatistics>? StatisticsChanged;
    public event Action<string>? MessageChanged;
    public MdEditor()
    {
        InitializeComponent();
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshPreviewAsync(); };
        DetachedFromVisualTree += (_, _) => Stop();
        AttachedToVisualTree += (_, _) => ScheduleRefresh();
        _selectionTimer.Tick += (_, _) => { _selectionTimer.Stop(); ApplyHighlight(scroll: true); };
        PreviewContent.LayoutUpdated += (_, _) => PreviewBlockHighlights.InvalidateVisual();
        PreviewContent.DoubleTapped += OnPreviewDoubleTapped;
    }
    public void ShowDocument(NoteDocument? document)
    {
        CancelPreviewEdit();
        if (_active?.Editor is { } old) old.PropertyChanged -= OnEditorPropertyChanged;
        _active = document;
        if (document is null)
        {
            Stop();
            _selectionTimer.Stop();
            _pendingCenter = null;
            ClearHighlight();
            _render = null;
            PreviewContent.Children.Clear();
            return;
        }
        document.Editor.PropertyChanged += OnEditorPropertyChanged;
        ScheduleRefresh();
    }

    // ---- Selection highlight: the editor selection is mirrored in the preview (debounced). ----
    internal static TimeSpan SelectionDelay { get; set; } = TimeSpan.FromMilliseconds(120);
    internal static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#5C4F8FE8"));
    private readonly DispatcherTimer _selectionTimer = new();
    private PreviewRender? _render;
    private readonly List<(InlineCollection Parent, int Index, Run Original, Run[] Pieces)> _splits = [];
    internal int HighlightUpdates { get; private set; }
    internal int HighlightedRuns => _splits.Sum(s => s.Pieces.Count(p => p.Background == HighlightBrush));
    internal IReadOnlyList<Control> HighlightedBlocks => PreviewBlockHighlights.Targets;
    internal Control? CurrentLineBlock => PreviewBlockHighlights.CurrentLine;
    internal IEnumerable<Run> HighlightRuns => _splits.SelectMany(s => s.Pieces).Where(p => p.Background == HighlightBrush);

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.SelectionStartProperty && e.Property != TextBox.SelectionEndProperty && e.Property != TextBox.CaretIndexProperty) return;
        _selectionTimer.Stop();
        _selectionTimer.Interval = SelectionDelay;
        _selectionTimer.Start();
    }

    private void ClearHighlight()
    {
        for (var i = _splits.Count - 1; i >= 0; i--)
        {
            var (parent, index, original, pieces) = _splits[i];
            foreach (var piece in pieces) parent.Remove(piece);
            parent.Insert(index, original);
        }
        _splits.Clear();
        PreviewBlockHighlights.Targets = [];
    }

    private void ApplyHighlight(bool scroll)
    {
        if (IsEditingPreview) return;
        ClearHighlight();
        HighlightUpdates++;
        PreviewBlockHighlights.CurrentLine = null;
        if (_render is not { } render || _active?.Editor is not { } editor || editor.Text != render.Map.Source) return;
        if (editor.SelectionStart == editor.SelectionEnd)
        {
            var (lineStart, lineEnd) = CaretLine.At(render.Map.Source, editor.CaretIndex);
            if (render.Map.BlockForLine(lineStart, lineEnd) is var id and >= 0 && render.Blocks.TryGetValue(id, out var lineBlock))
                PreviewBlockHighlights.CurrentLine = lineBlock;
            return;
        }
        var highlights = render.Map.Map(editor.SelectionStart, editor.SelectionEnd);
        if (highlights.Count == 0) return;
        var blocks = new List<Control>();
        Control? first = null;
        foreach (var h in highlights)
        {
            if (!render.Blocks.TryGetValue(h.BlockId, out var control)) continue;
            first ??= control;
            if (h.WholeBlock) { blocks.Add(control); continue; }
            if (!render.Runs.TryGetValue(h.SegmentId, out var target) || target.Run.Text is not { } text) continue;
            var index = target.Parent.IndexOf(target.Run);
            if (index < 0) continue;
            var pieces = new List<Run>();
            Run Piece(string part, bool lit)
            {
                var run = new Run(part) { FontFamily = target.Run.FontFamily, Foreground = target.Run.Foreground, FontWeight = target.Run.FontWeight, FontStyle = target.Run.FontStyle,
                    TextDecorations = target.Run.TextDecorations };
                run.Background = lit ? HighlightBrush : target.Run.Background;
                return run;
            }
            if (h.Offset > 0) pieces.Add(Piece(text[..h.Offset], false));
            pieces.Add(Piece(text.Substring(h.Offset, h.Length), true));
            if (h.Offset + h.Length < text.Length) pieces.Add(Piece(text[(h.Offset + h.Length)..], false));
            target.Parent.Remove(target.Run);
            for (var i = 0; i < pieces.Count; i++) target.Parent.Insert(index + i, pieces[i]);
            _splits.Add((target.Parent, index, target.Run, pieces.ToArray()));
        }
        PreviewBlockHighlights.Targets = blocks;
        if (scroll && first is not null) Dispatcher.UIThread.Post(() => ScrollIntoViewIfNeeded(first), DispatcherPriority.Background);
    }

    internal int PreviewScrolls { get; private set; }
    internal int PreviewCenters { get; private set; }
    private int? _pendingCenter;

    // Centers the preview block for the source line at caret (blank lines use the nearest block above, then below).
    // If the preview is stale it runs after the next refresh.
    public void CenterPreviewOnLine(int caret)
    {
        _pendingCenter = null;
        if (_render is not { } render || _active?.Editor is not { } editor || editor.Text != render.Map.Source) { _pendingCenter = caret; ScheduleRefresh(); return; }
        var source = render.Map.Source;
        var (start, end) = CaretLine.At(source, caret);
        var id = render.Map.BlockForLine(start, end);
        for (var s = start; id < 0 && s > 0;) { (s, var e) = CaretLine.At(source, s - 1); id = render.Map.BlockForLine(s, e); }
        for (var e = end; id < 0 && e < source.Length;) { var next = e + (source[e] == '\r' && e + 1 < source.Length && source[e + 1] == '\n' ? 2 : 1); (var s, e) = CaretLine.At(source, next); id = render.Map.BlockForLine(s, e); if (next >= source.Length) break; }
        if (id < 0 || !render.Blocks.TryGetValue(id, out var block)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (block.TranslatePoint(new Point(0, 0), PreviewLayer) is not { } top) return;
            var max = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
            var y = Math.Clamp(top.Y + block.Bounds.Height / 2 - PreviewScroll.Viewport.Height / 2, 0, max);
            PreviewCenters++;
            PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, y);
        }, DispatcherPriority.Background);
    }

    // Scrolls only when the highlighted element is entirely outside the visible part of the preview.
    private void ScrollIntoViewIfNeeded(Control target)
    {
        if (target.TranslatePoint(new Point(0, 0), PreviewLayer) is not { } top) return;
        var viewTop = PreviewScroll.Offset.Y;
        var viewBottom = viewTop + PreviewScroll.Viewport.Height;
        if (top.Y + target.Bounds.Height > viewTop && top.Y < viewBottom) return;
        PreviewScrolls++;
        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, Math.Max(0, top.Y - 40));
    }

    // ---- Editing in the preview: text inside rendered blocks is edited in an overlay box and written back
    // to the matching source range (through the source editor, so undo and dirty state follow). ----
    private TextBox? _editBox;
    private Control? _editBlock;
    private List<EditPiece>? _editPieces;
    private (int Start, int End)? _editCode;
    internal TextBox? PreviewEditBox => _editBox;
    internal bool IsEditingPreview => _editBox is not null;
    internal int PreviewEdits { get; private set; }
    internal IEnumerable<Control> PreviewBlocks => _render?.Blocks.Values ?? Enumerable.Empty<Control>();

    private void OnPreviewDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_render is not { } render) return;
        for (var v = e.Source as Visual; v is not null && v != PreviewContent; v = v.GetVisualParent())
            if (v is Control c && render.Blocks.ContainsValue(c)) { if (BeginPreviewEdit(c)) e.Handled = true; return; }
    }

    internal bool BeginPreviewEdit(Control block)
    {
        if (IsEditingPreview) CommitPreviewEdit();
        if (_render is not { } render || _active?.Editor is not { } editor || editor.Text != render.Map.Source) return false;
        var id = render.Blocks.FirstOrDefault(kv => kv.Value == block, new(-1, block)).Key;
        if (id < 0) return false;
        ClearHighlight();
        string text;
        var code = render.CodeContent.TryGetValue(id, out var range);
        if (code) { text = render.Map.Source[range.Start..range.End].Replace("\r\n", "\n").Replace('\r', '\n'); _editCode = range; _editPieces = null; }
        else if (block is TextBlock { Inlines: { } inlines } and not SelectableTextBlock)
        {
            var runIds = render.Runs.ToDictionary(kv => kv.Value.Run, kv => kv.Key);
            var pieces = new List<EditPiece>();
            CollectPieces(inlines, runIds, render, pieces);
            if (!pieces.Any(p => p.SourceStart >= 0)) return false;
            text = string.Concat(pieces.Select(p => p.Text));
            _editPieces = pieces; _editCode = null;
        }
        else return false;
        var at = block.TranslatePoint(new Point(0, 0), PreviewLayer) ?? new Point(0, 0);
        var box = new TextBox
        {
            Name = "PreviewEditBox", Text = text, AcceptsReturn = code, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(at.X - 4, at.Y - 4, 0, 0), Width = block.Bounds.Width + 8, MinHeight = block.Bounds.Height + 8, Padding = new Thickness(3)
        };
        if (block is TextBlock tb) { box.FontSize = tb.FontSize; box.FontWeight = tb.FontWeight; box.FontFamily = tb.FontFamily; }
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; CancelPreviewEdit(); }
            else if (e.Key == Avalonia.Input.Key.Enter && (!code || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta)))
            { e.Handled = true; CommitPreviewEdit(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => { if (_editBox == box) CommitPreviewEdit(); };
        _editBox = box; _editBlock = block;
        block.Opacity = 0;
        PreviewLayer.Children.Add(box);
        box.Focus();
        box.CaretIndex = text.Length;
        MessageChanged?.Invoke(code ? "Editing code in the preview: Ctrl+Enter applies, Esc cancels." : "Editing in the preview: Enter applies, Esc cancels.");
        return true;
    }

    private static void CollectPieces(InlineCollection inlines, Dictionary<Run, int> runIds, PreviewRender render, List<EditPiece> pieces)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run when runIds.TryGetValue(run, out var id):
                    pieces.Add(new EditPiece(run.Text ?? "", render.Map.Segments[id].Start, render.CodeSegments.Contains(id))); break;
                case Run run: pieces.Add(new EditPiece(run.Text ?? "", -1)); break;
                case Span span: CollectPieces(span.Inlines, runIds, render, pieces); break;
                case LineBreak: pieces.Add(new EditPiece("\n", -1)); break;
                default: pieces.Add(new EditPiece("\uFFFC", -1)); break;
            }
        }
    }

    private TextBox? EndEdit()
    {
        var box = _editBox;
        _editBox = null;
        if (box is null) return null;
        if (_editBlock is { } block) block.Opacity = 1;
        _editBlock = null;
        PreviewLayer.Children.Remove(box);
        PreviewScroll.Focusable = true;
        PreviewScroll.Focus();
        return box;
    }

    internal void CancelPreviewEdit() { if (EndEdit() is not null) MessageChanged?.Invoke(""); }

    // Applies the overlay text; returns the reason when it is rejected (the source is then unchanged).
    internal string? CommitPreviewEdit()
    {
        var (pieces, code) = (_editPieces, _editCode);
        if (EndEdit() is not { } box) return null;
        if (_render is not { } render || _active?.Editor is not { } editor || editor.Text != render.Map.Source) return Report("The note changed while editing; the preview edit was not applied.");
        var source = render.Map.Source;
        var check = MarkdownStructure.SameAs(source);
        var plan = code is { } range ? PreviewEdit.PlanCode(source, range.Start, range.End, box.Text ?? "", check)
            : PreviewEdit.PlanInline(source, pieces ?? [], box.Text ?? "", check);
        if (plan.Error is { } error) return Report(error);
        MessageChanged?.Invoke("");
        if (plan.Edit is not { } edit) return null;
        editor.SelectionStart = edit.Start;
        editor.SelectionEnd = edit.End;
        editor.SelectedText = edit.Replacement;
        editor.SelectionStart = editor.SelectionEnd = edit.Start + edit.Replacement.Length;
        PreviewEdits++;
        return null;
    }
    private string Report(string message) { MessageChanged?.Invoke(message); return message; }
    public void Stop() { ++_previewVersion; _previewTimer.Stop(); }
    public void ScheduleRefresh()
    {
        ++_previewVersion;
        _previewTimer.Stop();
        _previewTimer.Start();
    }
    private async Task RefreshPreviewAsync()
    {
        if (_active is not { } document) return;
        var version = _previewVersion;
        var text = document.Text;
        try
        {
            var result = await Task.Run(() => (Stats: TextStatistics.Count(text),
                Markdown: document.IsMarkdown ? MarkdownPreview.Parse(text) : null));
            if (version != _previewVersion || document != _active) return;
            if (IsEditingPreview && text != _render?.Map.Source) CancelPreviewEdit(); // source changed elsewhere
            else if (IsEditingPreview) return; // nothing changed: keep the overlay over the current render
            StatisticsChanged?.Invoke(result.Stats);
            ClearHighlight();
            _render = null;
            PreviewContent.Children.Clear();
            if (text.Length == 0)
            {
                PreviewContent.Children.Add(new TextBlock { Text = "Your preview will appear here.", Foreground = Brush.Parse("#788393"), Margin = new Thickness(0, 12) });
            }
            else if (result.Markdown is not null)
            {
                _render = MarkdownPreview.RenderMapped(result.Markdown, text, document.Path is { } notePath ? Path.GetDirectoryName(notePath) : null);
                foreach (var control in _render.Controls) PreviewContent.Children.Add(control);
            }
            else PreviewContent.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
            ApplyHighlight(scroll: false);
            if (_pendingCenter is { } pending && _render is not null) CenterPreviewOnLine(pending);
        }
        catch (Exception ex)
        {
            if (version == _previewVersion) MessageChanged?.Invoke($"Preview unavailable: {ex.Message}");
        }
    }
}

// Draws the "whole block" selection highlight behind preview elements that cannot be mapped exactly.
public sealed class PreviewBlockHighlight : Control
{
    private IReadOnlyList<Control> _targets = [];
    public IReadOnlyList<Control> Targets { get => _targets; set { _targets = value; InvalidateVisual(); } }
    private Control? _currentLine;
    // The block for the caret's source line (shown only without a selection).
    public Control? CurrentLine { get => _currentLine; set { _currentLine = value; InvalidateVisual(); } }
    public override void Render(DrawingContext context)
    {
        if (_currentLine is { IsVisible: true } line && line.TranslatePoint(new Point(0, 0), this) is { } lineAt)
            context.FillRectangle(CurrentLineOverlay.LineBrush, new Rect(lineAt.X - 6, lineAt.Y - 3, line.Bounds.Width + 12, line.Bounds.Height + 6), 3);
        foreach (var target in _targets)
            if (target.IsVisible && target.TranslatePoint(new Point(0, 0), this) is { } at)
                context.FillRectangle(MdEditor.HighlightBrush, new Rect(at.X - 2, at.Y - 2, target.Bounds.Width + 4, target.Bounds.Height + 4), 3);
    }
}
