using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 78: the one file-type icon used by explorer rows, tabs, Quick Open and global search results.
// Item 112: a panel of one or more paths so multi-colour icons (Java mug, Python py) can bind each layer.
public sealed class FileTypeIcon : Panel
{
    public static readonly StyledProperty<string?> FileNameProperty = AvaloniaProperty.Register<FileTypeIcon, string?>(nameof(FileName));
    public static readonly StyledProperty<bool> IsBinaryProperty = AvaloniaProperty.Register<FileTypeIcon, bool>(nameof(IsBinary));
    public string? FileName { get => GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public bool IsBinary { get => GetValue(IsBinaryProperty); set => SetValue(IsBinaryProperty, value); }
    public FileIcon Icon { get; private set; } = FileIcons.File;
    // Same names the Path-based control exposed, so tests and callers can still read the primary glyph.
    public Geometry? Data { get; private set; }
    public IBrush? Fill => (Children.Count > 0 ? Children[0] as Shape : null)?.Fill;
    private readonly List<IDisposable> _fills = [];

    public FileTypeIcon()
    {
        Width = FileIcon.Size; Height = FileIcon.Size;
        VerticalAlignment = VerticalAlignment.Center; IsHitTestVisible = false;
        Update();
    }
    public FileTypeIcon(string? fileName, bool isBinary = false) : this() { FileName = fileName; IsBinary = isBinary; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FileNameProperty || change.Property == IsBinaryProperty) Update();
    }
    private void Update()
    {
        var icon = FileIcons.For(FileName, IsBinary);
        if (ReferenceEquals(icon, Icon) && Children.Count > 0) return;
        Icon = icon;
        Data = icon.Geometry;
        foreach (var fill in _fills) fill.Dispose();
        _fills.Clear();
        Children.Clear();
        // Theme resources (dark/light variants), so a theme switch recolours live.
        foreach (var layer in icon.Layers)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Width = FileIcon.Size, Height = FileIcon.Size, Stretch = Stretch.None,
                Data = layer.Geometry, IsHitTestVisible = false
            };
            _fills.Add(path.Bind(Shape.FillProperty, this.GetResourceObservable(layer.BrushKey)));
            Children.Add(path);
        }
    }
}
