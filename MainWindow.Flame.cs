using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
namespace Apex;
public sealed partial class MainWindow
{
    private Grid FlameMark()
    {
        var host = new Grid { Width = 128, Height = 128, IsHitTestVisible = false };
        host.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/FlameMark.png")), Stretch = Stretch.Uniform });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(host, "Apex flame");
        return host;
    }
}
