using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
namespace Apex;
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? flareTimer;
    private Canvas? flareCanvas;
    private void StopTaskFlare()
    {
        flareTimer?.Stop();
        flareTimer = null;
        if (flareCanvas is not null)
            root.Children.Remove(flareCanvas);
        flareCanvas = null;
    }
    private void PlayTaskFlare(Border notice)
    {
        StopTaskFlare();
        if (!uiSettings.AnimationsEnabled)
            return;
        var canvas = new Canvas { Width = 370, Height = 90, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(24, 24, 28, 52), IsHitTestVisible = false };
        flareCanvas = canvas;
        Grid.SetRowSpan(canvas, 3);
        Canvas.SetZIndex(canvas, 111);
        root.Children.Add(canvas);
        var dots = new List<Ellipse>();
        for (int i = 0; i < 8; i++)
        {
            var dot = new Ellipse { Width = 3, Height = 3, Fill = Brush(i % 2 == 0 ? "#DCF9FF" : "#A09FFF") };
            dots.Add(dot);
            canvas.Children.Add(dot);
        }
        var clock = Stopwatch.StartNew();
        flareTimer = DispatcherQueue.CreateTimer();
        flareTimer.Interval = TimeSpan.FromMilliseconds(16);
        flareTimer.Tick += (_, _) => { double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 620, 0, 1); for (int i = 0; i < dots.Count; i++) { double angle = (i - 3.5) * .35; Canvas.SetLeft(dots[i], 32 + Math.Sin(angle) * (12 + t * 38)); Canvas.SetTop(dots[i], 42 - Math.Cos(angle) * (10 + t * 55)); dots[i].Opacity = Math.Sin(t * Math.PI) * .85; } if (t >= 1 || !uiSettings.AnimationsEnabled) StopTaskFlare(); };
        flareTimer.Start();
    }
}
