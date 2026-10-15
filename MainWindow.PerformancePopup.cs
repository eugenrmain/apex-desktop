using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

namespace Apex;
public sealed partial class MainWindow
{
    private Grid? performanceOverlay;
    private Border? performancePopup;
    private Button? performancePopupOwner;
    private readonly List<(Control Control, bool Enabled)> popupDisabled = [];
    private SizeChangedEventHandler? popupResize;
    private void OpenPerformancePopup(string title, UIElement content, Button? owner = null, UIElement? footer = null)
    {
        if (working)
        {
            ShowTaskNotice("Task in progress", "Finish or cancel the current task before editing performance settings.", NoticeKind.Information);
            return;
        }
        ClosePerformancePopup(false);
        DismissInformation(false);
        performancePopupOwner = owner;
        var overlay = new Grid { Background = Brush("#28000000") };
        performanceOverlay = overlay;
        var layout = new Grid { RowSpacing = 12 };
        layout.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        layout.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        layout.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var heading = new Grid { Margin = new(24, 20, 20, 0), ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        heading.Children.Add(Text(title, Ui.SectionTitle, "#EDF3FF", true));
        var close = ActionButton("\uE711", "Close", (_, _) => ClosePerformancePopup());
        close.Content = Icon("\uE711", 16, "#DEEAFF");
        close.Width = 40;
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        layout.Children.Add(heading);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(24, 8, 24, 16) };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        if (footer is FrameworkElement action)
        {
            action.HorizontalAlignment = HorizontalAlignment.Right;
            action.Margin = new(24, 0, 24, 20);
            Grid.SetRow(action, 2);
            layout.Children.Add(action);
        }
        bool glass = uiSettings.AdvancedEffectsEnabled && !preferences.SolidBackground;
        performancePopup = new Border
        {
            Child = layout,
            CornerRadius = new(24),
            BorderBrush = Brush("#5872BBEE"),
            BorderThickness = new(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = glass ? new AcrylicBrush { TintColor = ColorOf("#101B33"), TintOpacity = .50, TintLuminosityOpacity = .60, FallbackColor = ColorOf("#101B33") } : Brush("#FF101B33")
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(performancePopup, title + " settings");
        overlay.Children.Add(performancePopup);
        Grid.SetRowSpan(overlay, 3);
        Canvas.SetZIndex(overlay, 120);
        popupResize = (_, _) => SizePopup();
        root.SizeChanged += popupResize;
        SizePopup();
        foreach (var control in Descendants(page).OfType<Control>().Concat(navigation.Values))
        {
            popupDisabled.Add((control, control.IsEnabled));
            control.IsEnabled = false;
        }
        overlay.PointerPressed += (_, e) => { if (!Within(e.OriginalSource as DependencyObject, performancePopup)) { ClosePerformancePopup(); e.Handled = true; } };
        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ClosePerformancePopup();
                e.Handled = true;
            }
            if (e.Key != Windows.System.VirtualKey.Tab || performancePopup is null)
                return;
            var items = Descendants(performancePopup).OfType<Control>().Where(c => c.IsEnabled && c.IsTabStop && c.ActualWidth > 0 && c.ActualHeight > 0).ToList();
            if (items.Count == 0)
                return;
            var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as Control;
            int index = items.IndexOf(current!), direction = PopupKeyState(0x10) < 0 ? -1 : 1;
            items[(index + direction + items.Count) % items.Count].Focus(FocusState.Keyboard);
            e.Handled = true;
        };
        close.Loaded += (_, _) => close.Focus(FocusState.Keyboard);
        root.Children.Add(overlay);
    }
    private void SizePopup()
    {
        if (performancePopup is null)
            return;
        performancePopup.Width = Math.Max(200, Math.Min(900, root.ActualWidth - 48));
        performancePopup.Height = double.NaN;
        performancePopup.MaxHeight = Math.Max(200, Math.Min(720, root.ActualHeight - 96));
    }
    private void ClosePerformancePopup(bool restoreFocus = true)
    {
        if (working && performancePopup is not null)
            return;
        if (performanceOverlay is not null)
            root.Children.Remove(performanceOverlay);
        if (popupResize is not null)
            root.SizeChanged -= popupResize;
        popupResize = null;
        performancePopup = null;
        performanceOverlay = null;
        foreach (var item in popupDisabled)
            item.Control.IsEnabled = item.Enabled;
        popupDisabled.Clear();
        if (restoreFocus)
            refreshPerformance?.Invoke();
        if (restoreFocus)
            performancePopupOwner?.Focus(FocusState.Keyboard);
        performancePopupOwner = null;
    }
    [DllImport("user32.dll", EntryPoint = "GetKeyState")] private static extern short PopupKeyState(int key);
}
