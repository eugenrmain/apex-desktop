using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace Apex;
public sealed partial class MainWindow
{
    // Geist (SIL OFL 1.1) static instances: Regular for body and controls, Medium for every heading.
    private static readonly FontFamily GeistRegular = new("ms-appx:///Assets/Fonts/Geist.ttf#Geist");
    private static readonly FontFamily GeistMedium = new("ms-appx:///Assets/Fonts/Geist-Medium.ttf#Geist Medium");

    // Original two-tongue flame at 82% width with a lightning core, on a 64-unit grid, matching Assets/Apex.svg and scripts/generate_assets.py.
    private const string FlameData = "M35.28,3 C35.28,17 27.08,21 25.44,32 C22.16,29 20.52,24 20.52,18 C12.32,28 9.86,37 11.5,46 C13.14,57 22.16,62 31.18,62 C45.12,62 54.14,51 52.5,37 C51.68,26 45.12,19 42.66,13 C43.48,24 39.38,29 36.92,30 C40.2,19 40.2,11 35.28,3 Z";
    private const string BoltData = "M32.82,32.4 L27.9,43.2 L31.84,43.2 L29.87,51.6 L37.74,38.4 L33.81,38.4 L35.78,32.4 Z";
    private static Grid FlameMark(double size, string name = "Apex mark")
    {
        var mark = (UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stretch='Uniform'><Canvas Width='64' Height='64'>" +
            "<Path Data='" + FlameData + "'><Path.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#7A45E6' Offset='0'/><GradientStop Color='#4F4BD8' Offset='.5'/><GradientStop Color='#2F62F0' Offset='1'/></LinearGradientBrush></Path.Fill></Path>" +
            "<Path Data='" + BoltData + "' Fill='#FFFFFF'/></Canvas></Viewbox>");
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(mark);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(host, name);
        return host;
    }
}
