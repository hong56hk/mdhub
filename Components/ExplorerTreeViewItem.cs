using Avalonia.Controls;
using Avalonia.Input;

namespace Mdhub.Components;

internal sealed class ExplorerTreeViewItem : TreeViewItem
{
    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    // FolderExplorer handles each click; the standard header gesture would toggle again.
    protected override void OnHeaderDoubleTapped(TappedEventArgs e) => e.Handled = true;
}
