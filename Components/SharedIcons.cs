using Avalonia.Media;

namespace Mdhub.Components;

// Icon geometry shared by several toolbars, so the same action always shows the same icon.
public static class SharedIcons
{
    // Collapse All: the explorer toolbar (item 61) and the global search results.
    public static readonly Geometry CollapseAll = Geometry.Parse("M 5,2 L 14,2 14,11 M 2,5 L 11,5 11,14 2,14 Z M 4.5,9.5 L 8.5,9.5");
}
