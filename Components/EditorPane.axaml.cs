using System.Collections.ObjectModel;
using Avalonia.Controls;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class EditorPane : UserControl
{
    public ObservableCollection<NoteDocument> Documents { get; } = [];
    public NoteDocument? Active { get; set; }
    public int ViewMode { get; set; } = 1;
    public GridLength PreviewWidth { get; set; } = new(1, GridUnitType.Star);
    public EditorPane() { InitializeComponent(); FileTabs.SetDocuments(Documents); }
    public void SetActive(bool active) => FocusBorder.BorderBrush = ThemeService.Brush(active ? "AccentStrong" : "BorderSubtle");
}
