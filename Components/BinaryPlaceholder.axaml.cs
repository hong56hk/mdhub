using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mdhub.Components;

public partial class BinaryPlaceholder : UserControl
{
    public BinaryPlaceholder() => InitializeComponent();
    public event Action? OpenDefaultRequested;
    public event Action? OpenAsTextRequested;
    public void ShowFile(string? path) => FileName.Text = path is null ? "" : System.IO.Path.GetFileName(path);
    private void OpenDefault_Click(object? sender, RoutedEventArgs e) => OpenDefaultRequested?.Invoke();
    private void OpenAsText_Click(object? sender, RoutedEventArgs e) => OpenAsTextRequested?.Invoke();
}