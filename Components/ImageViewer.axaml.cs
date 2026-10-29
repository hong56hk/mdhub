using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Mdhub.Components;

public partial class ImageViewer : UserControl
{
    public ImageViewer() => InitializeComponent();
    public void ShowImage(Bitmap? image) => Picture.Source = image;
}
