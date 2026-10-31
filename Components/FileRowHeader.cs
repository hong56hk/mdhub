using Avalonia.Controls;
using Avalonia.Layout;

namespace Mdhub.Components;

// Item 78: an explorer file row: the type icon in the chevron's slot (item 31), then the name. Folders keep a plain name header.
public sealed class FileRowHeader : StackPanel
{
    public const double Gap = 6;
    public string FilePath { get; }
    public bool IsBinary { get; }
    public string Text { get; }
    public FileTypeIcon Icon { get; }
    public FileRowHeader(string path, bool isBinary)
    {
        FilePath = path; IsBinary = isBinary; Text = System.IO.Path.GetFileName(path);
        Orientation = Orientation.Horizontal; Spacing = Gap;
        Icon = new FileTypeIcon(path, isBinary);
        Children.Add(Icon);
        Children.Add(new TextBlock { Text = Text, VerticalAlignment = VerticalAlignment.Center });
    }
    public override string ToString() => Text;
}