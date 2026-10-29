using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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
    }
    public void ShowDocument(NoteDocument document) { _active = document; ScheduleRefresh(); }
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
            StatisticsChanged?.Invoke(result.Stats);
            PreviewContent.Children.Clear();
            if (text.Length == 0)
            {
                PreviewContent.Children.Add(new TextBlock { Text = "Your preview will appear here.", Foreground = Brush.Parse("#788393"), Margin = new Thickness(0, 12) });
            }
            else if (result.Markdown is not null)
            {
                foreach (var control in MarkdownPreview.Render(result.Markdown)) PreviewContent.Children.Add(control);
            }
            else PreviewContent.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        }
        catch (Exception ex)
        {
            if (version == _previewVersion) MessageChanged?.Invoke($"Preview unavailable: {ex.Message}");
        }
    }
}
