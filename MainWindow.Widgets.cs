using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace Apex;
public sealed partial class MainWindow
{
    private ToggleButton OptionWidget(string title, string glyph, string detail, bool enabled, Action<bool> changed, bool available = true)
    {
        var copy = new StackPanel { Spacing = 4 };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(Icon(glyph, 20, "#83DDFF"));
        label.Children.Add(Text(title, Ui.Body, "#EDF3FF", true));
        copy.Children.Add(label);
        var state = Text("", Ui.Supporting, "#A9BAD4");
        copy.Children.Add(state);
        var toggle = new ToggleButton { Content = copy, IsChecked = enabled, IsEnabled = available, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(12), MinHeight = 80, CornerRadius = new(12), BorderThickness = new(1) };
        ToolTipService.SetToolTip(toggle, detail);
        toggle.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}' Padding='{TemplateBinding Padding}'><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate>");
        toggle.Resources["ToggleButtonBackgroundChecked"] = Brush("#30467BDC");
        toggle.Resources["ToggleButtonBorderBrushChecked"] = Brush("#EF67D8FF");
        toggle.Resources["ToggleButtonForegroundChecked"] = Brush("#EDF3FF");
        foreach (string key in new[] { "ToggleButtonBorderBrushPointerOver", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushPressed", "ToggleButtonBorderBrushCheckedPressed" })
            toggle.Resources[key] = Brush("#83DDFF");
        foreach (string key in new[] { "ToggleButtonBackgroundPointerOver", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundPressed", "ToggleButtonBackgroundCheckedPressed" })
            toggle.Resources[key] = Brush("#40467BDC");
        bool hovered = false;
        void Render()
        {
            bool on = toggle.IsChecked == true;
            state.Text = available ? (on ? "Included" : "Excluded") : "Manual / unavailable";
            toggle.Background = Brush(on ? (hovered ? "#48467BDC" : "#30467BDC") : (hovered ? "#24FFFFFF" : "#10090E1C"));
            toggle.BorderBrush = Brush(on ? "#EF67D8FF" : hovered ? "#7067D8FF" : "#357388AE");
            copy.Opacity = on ? 1 : .8;
        }
        toggle.PointerEntered += (_, _) => { hovered = true; Render(); };
        toggle.PointerExited += (_, _) => { hovered = false; Render(); };
        toggle.Checked += (_, _) => { Render(); changed(true); };
        toggle.Unchecked += (_, _) => { Render(); changed(false); };
        toggle.IsEnabledChanged += (_, _) => copy.Opacity = toggle.IsEnabled ? (toggle.IsChecked == true ? 1 : .8) : .45;
        Render();
        Automation(toggle, title);
        return toggle;
    }
    private Grid WidgetGrid(IEnumerable<UIElement> items, bool compact = false)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (int i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        foreach (var item in items)
            grid.Children.Add(item);
        void Arrange()
        {
            int columns = grid.ActualWidth < 620 ? 1 : grid.ActualWidth < (compact ? 820 : 1000) ? 2 : 3;
            if (grid.ColumnDefinitions.Count != columns)
            {
                grid.ColumnDefinitions.Clear();
                for (int i = 0; i < columns; i++)
                    grid.ColumnDefinitions.Add(new()
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });
            }
            grid.RowDefinitions.Clear();
            for (int i = 0; i < (grid.Children.Count + columns - 1) / columns; i++)
                grid.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            for (int i = 0; i < grid.Children.Count; i++)
            {
                Grid.SetRow((FrameworkElement)grid.Children[i], i / columns);
                Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns);
                Grid.SetColumnSpan((FrameworkElement)grid.Children[i], 1);
            }
        }
        grid.SizeChanged += (_, _) => Arrange();
        Arrange();
        return grid;
    }
    private void ShowManualChecklist()
    {
        manualChecklist.Load();
        var content = new StackPanel { Spacing = 12 };
        RenderManualChecklist(content);
        OpenPerformancePopup("Windows settings guide", content);
    }
    private void RenderManualChecklist(StackPanel host) => RenderManualGuidance(host, manualChecklist.Steps.GroupBy(s => s.Target).Select(g => g.Last()).Select(s => new Apex.Services.PlanAction(Apex.Services.PlanActionKind.Manual, s.Target, s.Instructions, s.Label)));
    private void RenderManualGuidance(StackPanel host, IEnumerable<Apex.Services.PlanAction> actions)
    {
        foreach (var old in host.Children.OfType<FrameworkElement>().Where(e => e.Tag?.ToString() == "ManualChecklist").ToArray())
            host.Children.Remove(old);
        var steps = actions.Where(a => a.Kind == Apex.Services.PlanActionKind.Manual).ToArray();
        if (steps.Length == 0)
            return;
        var panel = Section("Finish in Windows or your game");
        panel.Tag = "ManualChecklist";
        panel.Children.Add(Text("These steps are not applied or verified by Apex.", Ui.Supporting, "#A9BAD4"));
        foreach (var step in steps)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(Text(step.Label.Replace("Manual: ", ""), Ui.Body, "#EDF3FF", true));
            if (step.Value != "Manual; unverified")
                row.Children.Add(Text(step.Value, Ui.Supporting, "#A9BAD4"));
            string? uri = step.Target switch
            {
                "Graphics" or "Windowed" or "GpuScheduling" => "ms-settings:display-advancedgraphics",
                "GameMode" => "ms-settings:gaming-gamemode",
                "Captures" => "ms-settings:gaming-gamedvr",
                "Mouse" => "ms-settings:mousetouchpad",
                _ => step.Target.StartsWith("Display:") ? "ms-settings:display-advanced" : null
            };
            if (uri is not null || step.Target == "AppearanceGuided")
                row.Children.Add(ActionButton("\uE8A7", "Open settings", async (_, _) => { try { if (uri is not null) await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)); else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); } catch (Exception e) { ShowTaskNotice("Could not open settings", e.Message, NoticeKind.Error); } }));
            panel.Children.Add(row);
        }
        host.Children.Add(panel);
    }
}
