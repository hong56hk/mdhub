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
