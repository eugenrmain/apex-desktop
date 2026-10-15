using Apex.Platform;
using Apex.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Apex;

public sealed partial class MainWindow : Window
{
    private static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
    private readonly Grid root = new();
    private readonly StackPanel page = new() { Spacing = 22, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = Text("Ready", 12, "#A9BAD4");
    private readonly Preferences preferences = Preferences.Load();
    private readonly UISettings uiSettings = new();
    private readonly NativePickers pickers;
    private readonly FileToolkit toolkit = new();
    private bool working;
    private bool allowWorkingNavigation;
    private readonly Button stopRecording = new() { Content = "Stop recording", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(10) };
    private readonly Dictionary<string, Button> navigation = [];
    private CancellationTokenSource? simulation;
    private string currentPage = "Home";
    private nint subclassProc;
    private readonly WindowProc windowProc;
    private readonly nint hwnd;
    private Border? informationPanel;
    private Button? informationOwner;

    public MainWindow()
    {
        Title = "Apex Desktop";
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Apex.ico"));
        pickers = new(this);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        windowProc = WindowMessage;
        subclassProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(windowProc));
        AppWindow.Resize(new SizeInt32(1100, 760));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + Math.Max(0, (area.Width - 1100) / 2), area.Y + Math.Max(0, (area.Height - 760) / 2)));
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = ColorOf("#E3ECFF");
        root.RequestedTheme = ElementTheme.Dark;
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(52)
        });
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(36)
        });
        Content = root;
        ApplyBackdrop();
        var glow = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Backdrop.png")), Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = .65 };
        Grid.SetRowSpan(glow, 3);
        root.Children.Add(glow);
        uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyBackdrop);
        Closed += (_, _) => { boostPageLifetime?.Cancel(); diagnosticLifetime?.Cancel(); sensorSession.Dispose(); simulation?.Cancel(); DismissTaskNotice(); PlayCompletionSound(null, 0, 0); };
        BuildChrome();
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && informationPanel is not null)
            {
                DismissInformation();
                e.Handled = true;
            }
        };
        root.PointerPressed += (_, e) =>
        {
            if (informationPanel is not null && !Within(e.OriginalSource as DependencyObject, informationPanel) && !Within(e.OriginalSource as DependencyObject, informationOwner))
                DismissInformation(false);
        };
        ShowPage("Home");
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--verify") || arguments.Contains("--verify-layout") || arguments.Contains("--verify-bounded"))
        {
            root.Loaded += async (_, _) => await VerifyAsync();
        }
    }

    private void ApplyBackdrop()
    {
        bool glass = uiSettings.AdvancedEffectsEnabled && !preferences.SolidBackground && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        SystemBackdrop = glass ? new GlassBackdrop() : null;
        root.Background = glass ? Brush("#A6070B18") : Gradient("#FF070B16", "#FF0B142A", "#FF17132F");
    }

    private void BuildChrome()
    {
        var title = new Grid { Margin = new Thickness(22, 0, 150, 0) };
        title.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        title.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Flame(22));
        brand.Children.Add(Text("Apex Desktop", 18, "#EDF3FF", true));
        title.Children.Add(brand);
        var badge = Text("APX STUDIOS  /  " + AppVersion, 10, "#899BB8");
        badge.CharacterSpacing = 160;
        badge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(badge, 1);
        title.Children.Add(badge);
        root.Children.Add(title);
        SetTitleBar(title);

        var body = new Grid();
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(94)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var rail = new Grid { Margin = new Thickness(12, 14, 8, 12) };
        rail.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        rail.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var nav = new StackPanel { Spacing = 10 };
        foreach (var (name, glyph) in new[] { ("Home", "\uE80F"), ("System", "\uE7F4"), ("Performance", "\uE945"), ("Media", "\uE714") })
        {
            var navCopy = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            navCopy.Children.Add(Icon(glyph, 20, "#AEC1DC"));
            navCopy.Children.Add(Text(name, 11, "#AEC1DC"));
            var button = new Button { Content = navCopy, Width = 72, Height = 64, Padding = new Thickness(3), CornerRadius = new CornerRadius(14), Background = Brush("#00000000"), BorderThickness = new Thickness(0) };
            Automation(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => OpenWorkspace(name);
            navigation[name] = button;
            nav.Children.Add(button);
        }
        var railScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Left };
        rail.Children.Add(railScroll);


        var settings = new Button { Content = Icon("\uE713", 18, "#AEC1DC"), Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Background = Brush("#00000000"), BorderThickness = new Thickness(0) };
        Automation(settings, "Settings");
        ToolTipService.SetToolTip(settings, "Settings");
        settings.Click += (_, _) => ShowPage("Settings");
        navigation["Settings"] = settings;
        Grid.SetRow(settings, 1);
        rail.Children.Add(settings);
        body.Children.Add(rail);
        var scroll = new ScrollViewer { Content = page, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 20, 36, 28) };
        // Fill the available width beside the rail with consistent page margins.
        scroll.SizeChanged += (_, _) => page.Width = Math.Max(0, scroll.ActualWidth - scroll.Padding.Left - scroll.Padding.Right);
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(118, 0, 36, 0), BorderBrush = Brush("#16FFFFFF"), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        footer.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        status.MaxLines = 1;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.Margin = new Thickness(0, 0, 12, 0);
        footer.Children.Add(status);
        stopRecording.Click += (_, _) => simulation?.Cancel();
        Grid.SetColumn(stopRecording, 1);
        footer.Children.Add(stopRecording);
        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
    }

    private void ShowPage(string name)
    {
        if (working && !allowWorkingNavigation)
            return;
        ClosePerformancePopup(false);
        StopTaskFlare();
        boostPageLifetime?.Cancel();
        boostPageLifetime?.Dispose();
        boostPageLifetime = null;
        renderBoostFixture = null;
        DismissInformation(false);
        if (!allowWorkingNavigation)
            simulation?.Cancel();
        currentPage = name;
        page.Children.Clear();
        Descendants(root).OfType<ScrollViewer>().FirstOrDefault(s => ReferenceEquals(s.Content, page))?.ChangeView(null, 0, null, true);
        page.Spacing = 16;
        string? group = WorkspaceFor(name);
        foreach (var entry in navigation)
            entry.Value.Background = Brush(entry.Key == (group ?? name) ? "#2867D8FF" : "#00000000");
        status.Text = "Ready";
        page.Children.Add(Breadcrumbs(name, group));
        if (name == "Home")
        {
            Home();
            return;
        }
        page.Children.Add(PageHeading(RouteTitle(name), informationRoute: name));
        if (IsWorkspace(name))
        {
            WorkspaceLanding(group!);
            return;
        }
        switch (name)
        {
            case "Storage":
                Storage();
                break;
            case "Organize":
                Organize();
                break;
            case "Compress":
                Compress();
                break;
            case "Convert":
                ConvertMedia();
                break;
            case "Diagnostics":
                Diagnostics();
                break;
            case "Performance":
                Performance();
                break;
            case "Game Booster":
                GameBoosterPage();
                break;
            case "System Clean":
                SystemCleanPage();
                break;
            case "Settings":
                Settings();
                break;
            default:
                FileActions();
                break;
        }
    }

    private void Home()
    {
        page.Spacing = 18;
        var hero = new Grid { ColumnSpacing = 20 };
        hero.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        hero.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var introduction = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var studio = Text("APX STUDIOS", 11, "#83DDFF", true);
        studio.CharacterSpacing = 200;
        introduction.Children.Add(studio);
        introduction.Children.Add(Text("Apex Desktop", 40, "#F2F6FF", true));
        introduction.Children.Add(Text("Windows tweaks · Gaming preparation · System cleanup", Ui.Body, "#A9BAD4"));
        hero.Children.Add(introduction);
        var emblem = FlameMark();
        Grid.SetColumn(emblem, 1);
        hero.Children.Add(emblem);
        page.Children.Add(hero);

        page.Children.Add(WidgetGrid(new[] { Card("System", "\uE7F4", "Storage, cleanup and file tools"), Card("Performance", "\uE945", "Settings, diagnostics and gaming"), Card("Media", "\uE714", "Compress media or extract audio") }, true));
    }

    private Button Card(string name, string glyph, string subtitle, string? route = null)
    {
        var content = new Grid { RowSpacing = 12, MinHeight = 74 };
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        heading.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(12), Background = Gradient("#5663CFFF", "#228680EE", "#0CBB71E9"), BorderBrush = Brush("#687DDCFF"), BorderThickness = new Thickness(1), Child = Icon(glyph, 18, "#C9EFFF") });
        var title = Text(name, 20, "#EDF3FF", true);
        Grid.SetColumn(title, 1);
        heading.Children.Add(title);
        var arrow = Icon("\uE76C", 12, "#899BB8");
        Grid.SetColumn(arrow, 2);
        heading.Children.Add(arrow);
        content.Children.Add(heading);
        var detail = Text(subtitle, 13, "#A9BAD4");
        Grid.SetRow(detail, 1);
        content.Children.Add(detail);
        var button = new Button { Content = content, MinHeight = route is null ? 160 : 144, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(20), CornerRadius = new CornerRadius(20), Background = Gradient("#25FFFFFF", "#100B142A", "#158173EF"), BorderBrush = Gradient("#889EDFFF", "#28FFFFFF", "#528982FF"), BorderThickness = new Thickness(1) };
        ToolTipService.SetToolTip(button, name);
        Automation(button, name);
        button.Tag = route ?? name;
        button.Click += (_, _) => { if (route is null) OpenWorkspace(name); else ShowPage(route); };
        return button;
    }

    private void Settings()
    {
        var about = Section("Apex Desktop");
        about.Children.Add(Text("APX Studios · Version " + AppVersion, Ui.Body, "#83DDFF", true));
        about.Children.Add(Text("All tasks run locally. You choose what to apply, close or clean.", Ui.Body, "#A9BAD4"));
        page.Children.Add(Glass(about));
        var content = Section("Tool defaults");
        var recursive = new CheckBox { Content = "Include subdirectories when organizing", IsChecked = preferences.IncludeSubdirectories };
        var age = new NumberBox { Header = "Older-file cleanup threshold (days)", Value = preferences.CleanupAgeDays, Minimum = 1, Maximum = 36500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var sounds = new CheckBox { Content = "Play a subtle sound when a task completes", IsChecked = preferences.CompletionSounds };
        content.Children.Add(sounds);
        content.Children.Add(recursive);
        content.Children.Add(age);
        content.Children.Add(Work("Save defaults", async (_, _) =>
        {
            if (!double.IsFinite(age.Value) || age.Value < 1)
                throw new IOException("Enter a valid age threshold.");
            preferences.IncludeSubdirectories = recursive.IsChecked == true;
            preferences.CleanupAgeDays = (int)age.Value;
            preferences.CompletionSounds = sounds.IsChecked == true;
            preferences.Save();
            FinishTask("Defaults saved");
            await Task.CompletedTask;
        }));
        page.Children.Add(Glass(content));
        var data = Section("Local data");
        data.Children.Add(Text("Preferences and move undo records", 14, "#A9BAD4"));
        data.Children.Add(Text(Path.GetDirectoryName(toolkit.JournalPath)!, 13, "#A9BAD4"));
        data.Children.Add(ActionButton("\uE8B7", "Open data directory", (_, _) =>
        {
            try
            {
                var directory = Path.GetDirectoryName(toolkit.JournalPath)!;
                Directory.CreateDirectory(directory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception e) { status.Text = e.Message; }
        }));
        page.Children.Add(Glass(data));
    }
    private UIElement PageHeading(string name, double size = 34, string? informationRoute = null)
    {
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        heading.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        if (name == "Home")
            heading.Children.Add(HomeButton());
        else
            heading.Children.Add(Text(name, size, "#F2F6FF", true));
        var more = new Button { Content = Icon("\uE712", 20, "#C5D8FA"), Width = 44, Height = 40, CornerRadius = new CornerRadius(12), Background = Brush("#16FFFFFF"), BorderBrush = Brush("#30FFFFFF") };
        Automation(more, "Information about " + name);
        ToolTipService.SetToolTip(more, "Information");
        more.Click += (_, _) => ToggleInformation(more, name, (informationRoute ?? name) switch
        {
            "Home" => "Choose System for files and cleanup, Performance for settings and diagnostics, or Media for compression and conversion.",
            "Media" => "Compress images and videos, or extract MP3 audio from an MP4. Copies are saved locally and originals stay unchanged.",
            "System" => "Inspect storage, clean reviewed files, organize folders or manage files.",
            "Convert" => "Choose an MP4 and convert its audio to MP3. The original stays unchanged. A numbered copy is saved if the output name already exists.",
            "Storage" => "Scan a directory for logical file sizes and search the results. Directory links are skipped; access errors are listed. Cloud placeholder files are not opened during size scans.",
            "Organize" => "Map extensions to destination directories, preview every move, then apply. Collisions are skipped. Undo checks file contents and never overwrites an occupied original path.",
            "Compress" => "Create a smaller video or image copy locally. Originals are preserved. Choose an output size for sharing or use quality settings to save space. Video uses Windows codecs; unsupported formats are reported. Size-limited output is checked before saving. JPEG removes transparency and metadata; PNG preserves transparency.",
            "Settings" => "Saved defaults apply when you open a tool page. Preferences and move records stay in your local application data directory.",
            "Diagnostics" => "Monitor local hardware and processes. Sensors require supported hardware; advanced access and driver setup are explicit. Game recordings use PresentMon and capture only the selected process. No input-to-screen latency is inferred.",
            "Performance" => "Review Windows settings before applying. Direct changes retain restoration records; manual guidance stays separate. Display changes require timed confirmation.",
            "PerformanceHub" => "Choose Windows Settings, Diagnostics or Game Booster.",
            "Game Booster" => "Select apps to close and optional cleanup, then review preparation. Network usage needs a short administrator scan. Refused closures are reported; force close requires confirmation. Resource readings do not promise FPS gains.",
            "System Clean" => "Select categories, scan, then review individual files before recycling. App/browser caches require closed apps. History/cookies stay untouched. Game junk excludes saves and shaders. Emptying Recycle Bin is a separately confirmed permanent action. Registry cleaning is excluded.",
            _ => "Recycle selected files by default. Permanent deletion and app closure require confirmation. Older files are not necessarily unused. System directories and system processes are protected. Lock diagnostics cannot identify every possible lock or diagnose disk corruption."

        });
        Grid.SetColumn(more, 1);
        heading.Children.Add(more);
        return heading;
    }
    private void ToggleInformation(Button owner, string title, string message)
    {
        if (informationOwner == owner && informationPanel is not null)
        {
            DismissInformation();
            return;
        }
        DismissInformation(false);
        informationOwner = owner;
        var content = new StackPanel { Spacing = 12 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        header.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        header.Children.Add(Text(title, 16, "#EDF3FF", true));
        var close = new Button { Content = Icon("\uE711", 12, "#BDCCE5"), Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(9), Background = Brush("#13FFFFFF"), BorderThickness = new Thickness(0) };
        Automation(close, "Dismiss information");
        close.Click += (_, _) => DismissInformation();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        content.Children.Add(header);
        content.Children.Add(Text(message, 13, "#B5C5DE"));
        var anchor = owner.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, owner.ActualHeight + 10));
        double left = Math.Max(24, Math.Min(root.ActualWidth - 364, anchor.X + owner.ActualWidth - 340));
        informationPanel = new Border { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(left, anchor.Y, 24, 12), Padding = new Thickness(18), CornerRadius = new CornerRadius(18), BorderBrush = Gradient("#709EDFFF", "#35FFFFFF", "#428982FF"), BorderThickness = new Thickness(1), Background = Gradient("#FA101B33", "#F50B1429", "#FA1B1639"), Child = content };
        Automation(informationPanel, title + " information");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(informationPanel, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetRowSpan(informationPanel, 3);
        Canvas.SetZIndex(informationPanel, 100);
        root.Children.Add(informationPanel);
        close.Loaded += (_, _) => close.Focus(FocusState.Keyboard);
    }
    private void DismissInformation(bool restoreFocus = true)
    {
        if (informationPanel is not null)
            root.Children.Remove(informationPanel);
        informationPanel = null;
        if (restoreFocus)
            informationOwner?.Focus(FocusState.Keyboard);
        informationOwner = null;
    }
    private static bool Within(DependencyObject? child, DependencyObject? parent)
    {
        if (parent is null)
            return false;
        for (var node = child; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, parent))
                return true;
        return false;
    }
    private async Task DialogAsync(string title, string message) => await new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark }.ShowAsync();
    private static Border Glass(UIElement child) => new() { Child = child, Padding = new Thickness(20), CornerRadius = new CornerRadius(18), Background = Gradient("#17FFFFFF", "#0AFFFFFF", "#0D8982FF"), BorderBrush = Brush("#2AFFFFFF"), BorderThickness = new Thickness(1) };
    private static Border Pill(string text, string color) => new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(11, 6, 11, 6), CornerRadius = new CornerRadius(8), BorderBrush = Brush("#2D83DDFF"), BorderThickness = new Thickness(1), Background = Brush("#1280C7FF"), Child = Text(text, 10, color, true) };
    private static Button ActionButton(string glyph, string text, RoutedEventHandler action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(Icon(glyph, 14, "#DEEAFF"));
        content.Children.Add(Text(text, Ui.Body, "#DEEAFF"));
        var button = new Button { Content = content, MinHeight = 40, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10) };
        Automation(button, text);
        button.Click += action;
        return button;
    }
    private static void Automation(DependencyObject element, string name) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
    private static FontIcon Icon(string glyph, double size, string color) => new() { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    private static Image Flame(double size, bool transparent = false)
    {
        return new Image { Width = size, Height = size, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(transparent ? "ms-appx:///Assets/FlameMark.png" : "ms-appx:///Assets/AppIcon.png")), Stretch = Stretch.Uniform };
    }
    private static TextBlock Text(string value, double size, string color, bool bold = false) => new() { Text = value, FontSize = size is 13 or 15 ? Ui.Body : size, Foreground = Brush(color), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI Variable"), VerticalAlignment = VerticalAlignment.Center };
    private static SolidColorBrush Brush(string color) => new(ColorOf(color));
    private static Color ColorOf(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 6)
            h = "FF" + h;
        return Color.FromArgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16), Convert.ToByte(h[6..8], 16));
    }
    private static LinearGradientBrush Gradient(string start, string middle, string end) => new() { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1), GradientStops = { new GradientStop { Color = ColorOf(start), Offset = 0 }, new GradientStop { Color = ColorOf(middle), Offset = .55 }, new GradientStop { Color = ColorOf(end), Offset = 1 } } };

    // Retain native resize/maximize behavior while enforcing a minimum client-friendly window size.
    private nint WindowMessage(nint handle, uint message, nint wParam, nint lParam)
    {
        var result = CallWindowProc(subclassProc, handle, message, wParam, lParam);
        if (message == 0x0024)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            double scale = GetDpiForWindow(handle) / 96d;
            info.MinTrackSize = new NativePoint { X = (int)(800 * scale), Y = (int)(600 * scale) };
            Marshal.StructureToPtr(info, lParam, false);
        }
        return result;
    }
    private delegate nint WindowProc(nint handle, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern nint CallWindowProc(nint previous, nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
