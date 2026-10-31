using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 78: the one file-type icon used by explorer rows, tabs, Quick Open and global search results.
public sealed class FileTypeIcon : Avalonia.Controls.Shapes.Path
{
    public static readonly StyledProperty<string?> FileNameProperty = AvaloniaProperty.Register<FileTypeIcon, string?>(nameof(FileName));
    public static readonly StyledProperty<bool> IsBinaryProperty = AvaloniaProperty.Register<FileTypeIcon, bool>(nameof(IsBinary));
    public string? FileName { get => GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public bool IsBinary { get => GetValue(IsBinaryProperty); set => SetValue(IsBinaryProperty, value); }
    public FileIcon Icon { get; private set; } = FileIcons.File;
    private IDisposable? _fill;

    public FileTypeIcon()
    {
        Width = FileIcon.Size; Height = FileIcon.Size; Stretch = Stretch.None;
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
        if (ReferenceEquals(icon, Icon) && _fill is not null) return;
        Icon = icon;
        Data = icon.Geometry;
        _fill?.Dispose();
        // Theme resource (dark/light variants), so a theme switch recolours live.
        _fill = Bind(FillProperty, this.GetResourceObservable(icon.BrushKey));
    }
}
