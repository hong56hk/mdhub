using Avalonia.Controls;
using Avalonia.Threading;

namespace Mdhub.Views;

public partial class MainView
{
    private bool _showOutline;
    private GridLength _outlineWidth = new(260);
    private DispatcherTimer? _outlineTimer;

    private void ToggleOutline()
    {
        if (_active?.IsMarkdown != true) return;
        _showOutline = !_showOutline;
        UpdateOutlineLayout();
    }

    private void UpdateOutlineLayout()
    {
        var visible = _showOutline && _active?.IsMarkdown == true;
        if (Outline.IsVisible && !visible) _outlineWidth = Workspace.ColumnDefinitions[6].Width;
        Outline.IsVisible = OutlineSplitter.IsVisible = visible;
        Workspace.ColumnDefinitions[5].Width = new GridLength(visible ? 4 : 0);
        Workspace.ColumnDefinitions[6].Width = visible ? _outlineWidth : new GridLength(0);
        TextFormatter.SetOutlineState(_active?.IsMarkdown == true, visible);
        _outlineTimer?.Stop();
        if (visible) Outline.ShowDocument(_active, _active!.Text);
    }

    private void ScheduleOutline()
    {
        if (!Outline.IsVisible) return;
        if (_outlineTimer is null)
        {
            _outlineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _outlineTimer.Tick += (_, _) =>
            {
                _outlineTimer.Stop();
                if (Outline.IsVisible && _active?.IsMarkdown == true) Outline.ShowDocument(_active, _active.Text);
            };
        }
        _outlineTimer.Stop();
        _outlineTimer.Start();
    }

    private void NavigateOutline(int line)
    {
        if (_busy || _active?.IsMarkdown != true) return;
        if (TextEditor.IsVisible) JumpToLine(_active, line);
        else
        {
            var start = Mdhub.Text.EditorCommands.LineStart(_active.Text, line);
            MdEditor.CenterPreviewOnLine(start);
            MdEditor.FocusPreview();
        }
    }
}
