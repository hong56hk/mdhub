using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Mdhub.Models;

namespace Mdhub.Components;

public partial class FileTabs : UserControl
{
    public event Action? NewRequested;
    public event Action<NoteDocument>? CloseRequested;
    public event Action<NoteDocument>? DocumentSelected;
    public FileTabs() => InitializeComponent();
    public NoteDocument? SelectedDocument { get => Tabs.SelectedItem as NoteDocument; set => Tabs.SelectedItem = value; }
    public void SetDocuments(ObservableCollection<NoteDocument> documents) => Tabs.ItemsSource = documents;
    public void SetBusy(bool busy) => Tabs.IsEnabled = !busy;
    private void New_Click(object? sender, RoutedEventArgs e) => NewRequested?.Invoke();
    // Double-clicking the empty strip past the last tab opens a new note (same path as + and Ctrl+N).
    private void Tabs_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        for (var v = e.Source as Avalonia.Visual; v is not null && v != Tabs; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
            if (v is ListBoxItem or Button or Avalonia.Controls.Primitives.ScrollBar) return;
        e.Handled = true;
        NewRequested?.Invoke();
    }
    private void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedDocument is { } document) DocumentSelected?.Invoke(document);
    }
    private void TabClose_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: NoteDocument document }) CloseRequested?.Invoke(document);
    }
}
