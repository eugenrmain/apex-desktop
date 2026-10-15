using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Apex;
public sealed partial class MainWindow
{
    private UIElement CompactPathPicker(string title, out TextBox field, bool file = false)
    {
        var picker = PathPicker(title, out field, file);
        var button = Descendants(picker).OfType<Button>().First();
        button.MinHeight = 76;
        button.Padding = new Thickness(14);
        if (button.Content is StackPanel content)
        {
            content.Spacing = 5;
            var icon = content.Children.OfType<FontIcon>().FirstOrDefault();
            if (icon is not null)
                icon.Visibility = Visibility.Collapsed;
        }
        return picker;
    }
}
