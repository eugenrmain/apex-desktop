using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Apex;
public sealed partial class MainWindow
{
    private static readonly string[] SystemPages = ["Storage", "System Clean", "Organize", "File Actions"];
    private static readonly string[] PerformancePages = ["Performance", "Diagnostics", "Game Booster"];
    private static readonly string[] MediaPages = ["Compress", "Convert"];
    private void OpenWorkspace(string name) => ShowPage(WorkspaceRoute(name));
    private static string WorkspaceRoute(string name) => name == "Performance" ? "PerformanceHub" : name;
    private static bool IsWorkspace(string route) => route is "System" or "PerformanceHub" or "Media";
    private static string? WorkspaceFor(string route) => route switch
    {
        "System" => "System",
        "PerformanceHub" => "Performance",
        "Media" => "Media",
        _ => SystemPages.Contains(route) ? "System" : PerformancePages.Contains(route) ? "Performance" : MediaPages.Contains(route) ? "Media" : null
    };
    private static string RouteTitle(string route) => route switch
    {
        "System Clean" => "Cleanup",
        "Performance" => "Windows Settings",
        "PerformanceHub" => "Performance",
        _ => route
    };

    private StackPanel Breadcrumbs(string route, string? group)
    {
        var trail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "Breadcrumbs" };
        Automation(trail, "Location");
        var home = HomeButton();
        home.Tag = "Home";
        trail.Children.Add(home);
        void Separator() => trail.Children.Add(Text("/", Ui.Body, "#7489A8"));
        void Current(string label)
        {
            var current = Text(label, Ui.Body, "#C9D8EE");
            Automation(current, "Current page: " + label);
            trail.Children.Add(current);
        }
        if (route == "Home")
            return trail;
        if (group is null)
        {
            Separator();
            Current(RouteTitle(route));
            return trail;
        }
        Separator();
        if (IsWorkspace(route))
            Current(group);
        else
        {
            var parent = ActionButton("", group, (_, _) => OpenWorkspace(group));
            parent.Content = Text(group, Ui.Body, "#C9D8EE");
            parent.Tag = WorkspaceRoute(group);
            Automation(parent, "Back to " + group);
            trail.Children.Add(parent);
            Separator();
            Current(RouteTitle(route));
        }
        return trail;
    }

    private void WorkspaceLanding(string group)
    {
        var tools = group switch
        {
            "System" => new[] { ("Storage", "\uE8B7", "Find large files and explore your storage"), ("System Clean", "\uE74D", "Review temporary files and reclaim space"), ("Organize", "\uE8B9", "Sort files into folders with custom rules"), ("File Actions", "\uE8A5", "Manage files and resolve stuck apps") },
            "Performance" => new[] { ("Performance", "\uE713", "Review and apply Windows settings"), ("Diagnostics", "\uE9D9", "Monitor hardware and system resources"), ("Game Booster", "\uE7FC", "Prepare apps and cleanup before gaming") },
            _ => new[] { ("Compress", "\uE8C5", "Create smaller image and video copies"), ("Convert", "\uE8AB", "Extract MP3 audio from an MP4") }
        };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16, Tag = "Workspace cards" };
        foreach (var (route, glyph, subtitle) in tools)
        {
            var card = Card(RouteTitle(route), glyph, subtitle, route);
            grid.Children.Add(card);
        }
        bool arranging = false;
        void Arrange()
        {
            if (arranging)
                return;
            arranging = true;
            try
            {
                int columns = grid.ActualWidth < 620 ? 1 : 2;
                int rows = (tools.Length + columns - 1) / columns;
                if (grid.ColumnDefinitions.Count != columns || grid.RowDefinitions.Count != rows)
                {
                    grid.ColumnDefinitions.Clear();
                    grid.RowDefinitions.Clear();
                    for (int i = 0; i < columns; i++)
                        grid.ColumnDefinitions.Add(new()
                        {
                            Width = new GridLength(1, GridUnitType.Star)
                        });
                    for (int i = 0; i < rows; i++)
                        grid.RowDefinitions.Add(new()
                        {
                            Height = GridLength.Auto
                        });
                }
                for (int i = 0; i < grid.Children.Count; i++)
                {
                    var card = (FrameworkElement)grid.Children[i];
                    Grid.SetColumn(card, i % columns);
                    Grid.SetRow(card, i / columns);
                    Grid.SetColumnSpan(card, 1);
                }
            }
            finally { arranging = false; }
        }
        SizeChangedEventHandler resize = (_, _) => Arrange();
        grid.SizeChanged += resize;
        grid.Loaded += (_, _) => Arrange();
        page.Children.Add(grid);
    }
}
