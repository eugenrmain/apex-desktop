using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text.Json;

namespace Apex;
public sealed partial class MainWindow
{
    private readonly PerformanceDraft performanceDraft = new();
    private Action? refreshPerformance;
    private readonly ManualChecklist manualChecklist = new();
    private readonly Dictionary<string, Button> performanceCategories = [];
    private static readonly string[] CategoryNames = ["Power", "FPS", "Input", "Network", "Startup / Background Apps", "Advanced", "Visual Effects", "Display"];
    private void Performance()
    {
        EnsurePerformanceLoaded();
        if (performanceLoadError is not null)
        {
            page.Children.Add(Glass(Text("Could not open saved presets: " + performanceLoadError + ". Existing records were preserved.", Ui.Body, "#83DDFF")));
            return;
        }
        List<PowerScheme> schemes;
        try
        {
            schemes = WindowsPerformance.PowerSchemes();
        }
        catch { schemes = []; }
        var current = Text("", Ui.Body, "#A9BAD4");
        page.Children.Add(current);
        var presets = new Grid { ColumnSpacing = Ui.Gap, RowSpacing = Ui.Gap };
        for (int i = 0; i < 2; i++)
            presets.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        var presetButtons = new Dictionary<string, Button>();
        foreach (string label in new[] { "Performance", "Custom" })
        {
            var description = label == "Performance" ? "Appearance, input, display and gaming settings" : "Choose individual Windows tweaks";
            var copy = Section(label);
            copy.Children.Add(Text(description, Ui.Supporting, "#A9BAD4"));
            var button = new Button { Content = copy, MinHeight = 130, Padding = new(20), CornerRadius = new(18), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush("#19FFFFFF"), BorderBrush = Brush("#3D72BBEE"), BorderThickness = new(1) };
            Automation(button, label + " preset");
            presetButtons[label] = button;
            button.Resources["ButtonBorderBrushPointerOver"] = Brush("#FF83DDFF");
            button.Resources["ButtonBorderBrushPressed"] = Brush("#FF83DDFF");
            button.Resources["ButtonBackgroundPointerOver"] = Brush("#2967D8FF");
            button.Resources["ButtonBackgroundPressed"] = Brush("#3867D8FF");
            Grid.SetColumn(button, presets.Children.Count);
            presets.Children.Add(button);
            button.Click += (_, _) =>
            {
                try
                {
                    if (label == "Custom")
                    {
                        performanceDraft.SelectCustom();
                        ShowPerformanceCategory(-1, button);
                    }
                    else
                        performanceDraft.SelectBuiltIn(label, schemes.ToDictionary(s => s.Id, s => s.Name), WindowsMouse.Read(), WindowsDisplay.Choices());
                    refreshPerformance?.Invoke();
                }
                catch (Exception e) { ShowTaskNotice("Preset unavailable", e.Message, NoticeKind.Information); ShowPerformanceCategory(0, button); }
            };
        }
        presets.SizeChanged += (_, _) =>
        {
            bool narrow = presets.ActualWidth < 680;
            presets.RowDefinitions.Clear();
            for (int i = 0; i < (narrow ? 2 : 1); i++)
                presets.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            for (int i = 0; i < 2; i++)
            {
                Grid.SetRow((FrameworkElement)presets.Children[i], narrow ? i : 0);
                Grid.SetColumn((FrameworkElement)presets.Children[i], narrow ? 0 : i);
                Grid.SetColumnSpan((FrameworkElement)presets.Children[i], narrow ? 2 : 1);
            }
        };
        page.Children.Add(presets);
        var pending = Text("", Ui.Body, "#83DDFF");
        page.Children.Add(pending);
        performanceCategories.Clear();
        var categories = new Grid { ColumnSpacing = Ui.Gap, RowSpacing = Ui.Gap };
        categories.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        categories.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        for (int i = 0; i < 4; i++)
            categories.RowDefinitions.Add(new()
            {
                Height = GridLength.Auto
            });
        string[] icons = ["\uE945", "\uE7F4", "\uE765", "\uE774", "\uE7F8", "\uE713", "\uE790", "\uE7F4"];
        string[] descriptions = ["Direct · existing power scheme", "Manual · graphics and recording", "Direct + Manual · pointer and game controls", "Needs selection · adapter and DNS", "Needs selection · startup entries and apps", "Needs selection · exact process priority", "Direct + Manual · Windows appearance", "Direct where supported · maximum refresh rate"];
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            int index = new[] { 0, 1, 2, 3, 4, 6, 7, 5 }[i];
            string label = CategoryNames[index];
            var copy = new StackPanel { Spacing = 4 };
            copy.Children.Add(Text(label, Ui.WidgetTitle, "#EDF3FF", true));
            copy.Children.Add(Text(descriptions[index], Ui.Supporting, "#A9BAD4"));
            var button = ActionButton(icons[index], label, (_, _) => ShowPerformanceCategory(index, performanceCategories[label]));
            button.Content = copy;
            button.Padding = new(16, 12, 16, 12);
            button.MinHeight = 74;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Background = Gradient("#20FFFFFF", "#100B142A", "#188173EF");
            performanceCategories[label] = button;
            Grid.SetColumn(button, i % 2);
            Grid.SetRow(button, i / 2);
            categories.Children.Add(button);
        }
        categories.SizeChanged += (_, _) =>
        {
            bool narrow = categories.ActualWidth < 620;
            categories.RowDefinitions.Clear();
            for (int row = 0; row < (narrow ? CategoryNames.Length : 4); row++)
                categories.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            for (int i = 0; i < categories.Children.Count; i++)
            {
                var child = (FrameworkElement)categories.Children[i];
                Grid.SetRow(child, narrow ? i : i / 2);
                Grid.SetColumn(child, narrow ? 0 : i % 2);
                Grid.SetColumnSpan(child, narrow ? 2 : 1);
            }
        };
        page.Children.Add(categories);
        var footer = new StackPanel { Spacing = 12 };
        var review = ActionButton("\uE73E", "Review & apply", (_, _) => ReviewPerformance());
        review.Background = Brush("#83DDFF");
        if (review.Content is StackPanel r)
            foreach (var child in r.Children)
            {
                if (child is TextBlock t)
                    t.Foreground = Brush("#101B33");
                if (child is FontIcon f)
                    f.Foreground = Brush("#101B33");
            }
        review.MinHeight = 48;
        footer.Children.Add(review);
        footer.Children.Add(SelectionPair(ActionButton("\uE711", "Clear selections", (_, _) => { performanceDraft.Clear(); refreshPerformance?.Invoke(); }), ActionButton("\uE777", "Restore changes", (_, _) => ShowRestorePerformance())));
        page.Children.Add(Glass(footer));
        var manualButton = ActionButton("\uE73A", "Windows settings guide", (_, _) => ShowManualChecklist());
        page.Children.Add(manualButton);
        try
        {
            manualChecklist.Load();
        }
        catch (Exception e) { page.Children.Add(Text("Guide unavailable: " + e.Message, Ui.Supporting, "#83DDFF")); }
        refreshPerformance = () =>
        {
            current.Text = "Choose a preset, then review & apply.";
            pending.Text = performanceDraft.Actions.Count == 0 ? "No pending changes" : $"{performanceDraft.Actions.Count(a => a.Kind != PlanActionKind.Manual)} changes · {performanceDraft.Actions.Count(a => a.Kind == PlanActionKind.Manual)} Windows/game steps";
            review.IsEnabled = performanceDraft.Actions.Count > 0;
            manualButton.Visibility = manualChecklist.Steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var entry in presetButtons)
                entry.Value.BorderBrush = Brush(performanceDraft.Actions.Count > 0 && entry.Key == performanceDraft.Selected ? "#83DDFF" : "#3D72BBEE");
        };
        refreshPerformance();
    }
    private void ShowPerformanceCategory(int selected, Button? owner = null)
    {
        var content = new StackPanel { Spacing = Ui.Gap };
        if (selected < 0)
        {
            bool choosing = false;
            var saved = new ComboBox { Header = "Saved Custom preset", PlaceholderText = "Choose a saved preset", DisplayMemberPath = "Name", ItemsSource = performanceStore.Plans.ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(saved);
            saved.SelectedItem = saved.Items.OfType<PerformancePlan>().FirstOrDefault(p => p.Id == performanceDraft.CustomId);
            var name = Field("Save Custom preset as", "Preset name");
            if (performanceDraft.CustomName != "Custom")
                name.Text = performanceDraft.CustomName;
            content.Children.Add(name);
            var groups = new StackPanel { Spacing = 10 };
            void Groups()
            {
                groups.Children.Clear();
                for (int i = 0; i < CategoryNames.Length; i++)
                {
                    int index = new[] { 0, 1, 2, 3, 4, 6, 7, 5 }[i];
                    var body = new StackPanel { Spacing = 12 };
                    bool loaded = false;
                    var expander = new Expander { Header = CategoryNames[index], Content = body, HorizontalAlignment = HorizontalAlignment.Stretch };
                    expander.Expanding += (_, _) => { if (!loaded) { BuildPerformanceEditor(body, index); loaded = true; } };
                    groups.Children.Add(expander);
                }
            }
            saved.SelectionChanged += (_, _) => { if (!choosing && saved.SelectedItem is PerformancePlan preset) { performanceDraft.Load(preset); name.Text = preset.Name; Groups(); refreshPerformance?.Invoke(); } };
            content.Children.Add(ActionButton("\uE74E", "Save Custom preset", (_, _) => { try { if (string.IsNullOrWhiteSpace(name.Text)) throw new IOException("Enter a preset name."); var snapshot = performanceDraft.Snapshot(name.Text.Trim()); if (performanceStore.Plans.Any(p => p.Id == snapshot.Id && p.Name != snapshot.Name)) snapshot = snapshot with { Id = Guid.NewGuid() }; performanceStore.SavePlan(snapshot); performanceDraft.Load(snapshot); choosing = true; saved.ItemsSource = performanceStore.Plans.ToArray(); saved.SelectedItem = saved.Items.OfType<PerformancePlan>().First(p => p.Id == snapshot.Id); choosing = false; ShowTaskNotice("Preset saved", name.Text.Trim(), NoticeKind.Success); } catch (Exception e) { choosing = false; ShowTaskNotice("Could not save preset", e.Message, NoticeKind.Error); } }));
            Groups();
            content.Children.Add(groups);
        }
        else
        {
            BuildPerformanceEditor(content, selected);
            if (content.Children.FirstOrDefault() is StackPanel editor && editor.Children.FirstOrDefault() is TextBlock heading && heading.FontSize == Ui.SectionTitle)
                editor.Children.Remove(heading);
        }
        OpenPerformancePopup(selected < 0 ? "Custom preset" : CategoryNames[selected], content, owner);
    }
    private void BuildPerformanceEditor(StackPanel host, int categoryIndex)
    {
        var editor = new StackPanel { Spacing = Ui.Gap };
        host.Children.Add(editor);
        var factories = new Dictionary<CheckBox, Func<PlanAction>>();
        var selectedActions = new Dictionary<CheckBox, PlanAction>();
        void Toggle(CheckBox check)
        {
            try
            {
                if (check.IsChecked == true)
                {
                    var action = factories[check]();
                    if (selectedActions.TryGetValue(check, out var old) && (old.Target != action.Target || old.Kind != action.Kind))
                        performanceDraft.Toggle(old, false);
                    performanceDraft.Toggle(action, true);
                    selectedActions[check] = action;
                }
                else if (selectedActions.TryGetValue(check, out var action))
                {
                    performanceDraft.Toggle(action, false);
                    selectedActions.Remove(check);
                }
                refreshPerformance?.Invoke();
            }
            catch (Exception e) { ShowTaskNotice("Selection not added", e.Message, NoticeKind.Information); check.IsChecked = false; }
        }
        CheckBox Check(string label, Func<PlanAction> create, PlanAction? initial = null)
        {
            if (initial is null && label.StartsWith("Manual:"))
            {
                var action = create();
                initial = performanceDraft.Actions.FirstOrDefault(a => a.Kind == action.Kind && a.Target == action.Target);
            }
            var check = new CheckBox { Content = label, IsChecked = initial is not null };
            factories[check] = create;
            if (initial is not null)
                selectedActions[check] = initial;
            check.Checked += (_, _) => Toggle(check);
            check.Unchecked += (_, _) => Toggle(check);
            editor.Children.Add(check);
            return check;
        }
        void UpdateChecked(CheckBox check)
        {
            if (check.IsChecked == true)
                Toggle(check);
        }
        void Link(string label, string uri)
        {
            editor.Children.Add(ActionButton("\uE8A7", label, async (_, _) => { try { await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)); } catch (Exception e) { ShowTaskNotice("Could not open settings", e.Message, NoticeKind.Error); } }));
        }
        void Checklist(string label, string target) => editor.Children.Add(Text(label, Ui.Supporting, "#A9BAD4"));
        async Task RefreshApps(ComboBox selector)
        {
            try
            {
                var apps = await Task.Run(WindowsActions.Tasks);
                selector.ItemsSource = apps.Where(p => p.CanClose).Select(p => new ProcessChoice(new(p.Id, p.Started, p.Name, p.Executable, 0, 0, true))).ToList();
                selector.PlaceholderText = apps.Any(p => p.CanClose) ? "Select an app" : "No eligible apps running";
            }
            catch (Exception e) { ShowTaskNotice("Could not read apps", e.Message, NoticeKind.Error); }
        }
        void Editor()
        {
            editor.Children.Clear();
            try
            {
                switch (categoryIndex)
                {
                    case 0:
                        editor.Children.Add(Text("Power", Ui.SectionTitle, "#EDF3FF", true));
                        var schemes = WindowsPerformance.PowerSchemes();
                        string active = WindowsPerformance.Read(new(PlanActionKind.PowerPlan, "Active", "", "Power plan"));
                        editor.Children.Add(Text("Current: " + (schemes.FirstOrDefault(s => s.Id.ToString() == active)?.Name ?? active), Ui.Body, "#A9BAD4"));
                        var power = new ComboBox { Header = "Existing Windows power plan", PlaceholderText = "Select a scheme", ItemsSource = schemes, HorizontalAlignment = HorizontalAlignment.Stretch };
                        editor.Children.Add(power);
                        editor.Children.Add(Text("Direct change. Can increase power consumption and heat. Administrator approval may be needed. No restart is normally required.", Ui.Supporting, "#A9BAD4"));
                        var selectedPower = performanceDraft.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.PowerPlan);
                        power.SelectedItem = schemes.FirstOrDefault(s => s.Id.ToString() == selectedPower?.Value);
                        var powerCheck = Check("Change power scheme", () => power.SelectedItem is PowerScheme scheme ? new(PlanActionKind.PowerPlan, "Active", scheme.Id.ToString(), "Power: " + scheme.Name) : throw new IOException("Choose an existing power scheme."), selectedPower);
                        power.SelectionChanged += (_, _) => UpdateChecked(powerCheck);
                        Link("Windows Power settings", "ms-settings:powersleep");
                        break;
                    case 1:
                        editor.Children.Add(Text("FPS settings", Ui.SectionTitle, "#EDF3FF", true));
                        editor.Children.Add(Text("Set these in Windows. Apex cannot apply or verify them.", Ui.Supporting, "#A9BAD4"));
                        Link("Windows Graphics settings", "ms-settings:display-advancedgraphics");
                        Link("Windows Game Mode", "ms-settings:gaming-gamemode");
                        Link("Windows game recording", "ms-settings:gaming-gamedvr");
                        Checklist("Review per-game graphics preference", "Graphics");
                        Checklist("Review Game Mode", "GameMode");
                        Checklist("Review optimizations for windowed games", "Windowed");
                        Checklist("Review unwanted background game recording", "Captures");
                        Checklist("Experiment with hardware-accelerated GPU scheduling (hardware/driver dependent)", "GpuScheduling");
                        break;
                    case 2:
                        editor.Children.Add(Text("Input settings", Ui.SectionTitle, "#EDF3FF", true));
                        editor.Children.Add(Text("Direct: Windows pointer acceleration. Raw-input games handle their mouse input separately. Game options below are Manual; presentation timing does not measure physical input latency.", Ui.Supporting, "#A9BAD4"));
                        var mouse = performanceDraft.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.Mouse);
                        Check("Direct: disable Windows pointer acceleration", () => new(PlanActionKind.Mouse, "Pointer", WindowsMouse.WithoutAcceleration(WindowsMouse.Read()), "Input: disable Windows pointer acceleration"), mouse);
                        Link("Windows mouse settings", "ms-settings:mousetouchpad");
                        Checklist("Review mouse settings", "Mouse");
                        Checklist("Review in-game VSync", "VSync");
                        Checklist("Review game frame cap", "FrameCap");
                        Checklist("Review supported in-game latency option", "Latency");
                        break;
                    case 3:
                        editor.Children.Add(Text("Network", Ui.SectionTitle, "#EDF3FF", true));
                        var adapters = new ComboBox { Header = "Adapter", PlaceholderText = "Select a network adapter", DisplayMemberPath = "Name", ItemsSource = WindowsPerformance.Adapters(), HorizontalAlignment = HorizontalAlignment.Stretch };
                        var dns = new ComboBox { Header = "DNS selection", PlaceholderText = "Choose DNS", Items = { "Automatic", "Cloudflare", "Google", "Custom" }, HorizontalAlignment = HorizontalAlignment.Stretch };
                        var ipv4 = Field("Custom IPv4 servers", "Comma-separated addresses");
                        var ipv6 = Field("Custom IPv6 servers", "Comma-separated addresses");
                        editor.Children.Add(adapters);
                        editor.Children.Add(dns);
                        var customDns = SelectionPair(ipv4, ipv6);
                        editor.Children.Add(customDns);
                        customDns.Visibility = Visibility.Collapsed;
                        dns.SelectionChanged += (_, _) => customDns.Visibility = dns.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
                        editor.Children.Add(Text("DNS selection changes name resolution only. It does not tune Ethernet offloads, link speed or game packet routing.", Ui.Supporting, "#A9BAD4"));
                        Link("Network adapter properties (Manual)", "ms-settings:network-advancedsettings");
                        editor.Children.Add(Text("Adapter tuning is hardware-specific. Offloads normally reduce CPU work; disabling them can reduce throughput. EEE and interrupt moderation involve power/latency tradeoffs. Keep defaults unless testing a specific problem.", Ui.Supporting, "#A9BAD4"));
                        Checklist("Review adapter power saving / EEE", "AdapterPower");
                        Checklist("Review interrupt moderation for the selected adapter", "AdapterInterrupts");
                        editor.Children.Add(Text("Direct change. DNS affects name lookup and privacy; it does not guarantee lower game ping. Changing it can interrupt connectivity. VPN, managed, or encrypted DNS configurations are rejected when they cannot be restored faithfully.", Ui.Supporting, "#A9BAD4"));
                        var traffic = Text("Select an adapter to measure its local throughput.", Ui.Body, "#A9BAD4");
                        editor.Children.Add(traffic);
                        editor.Children.Add(Work("Measure selected adapter traffic", async (token, progress) =>
                        {
                            if (adapters.SelectedItem is not System.Net.NetworkInformation.NetworkInterface adapter)
                                throw new IOException("Choose an adapter explicitly.");
                            var clock = Stopwatch.StartNew();
                            var first = await Task.Run(() => adapter.GetIPStatistics(), token);
                            await Task.Delay(1000, token);
                            var next = await Task.Run(() => adapter.GetIPStatistics(), token);
                            double elapsed = clock.Elapsed.TotalSeconds;
                            traffic.Text = adapter.Name + ": " + Rate(Math.Max(0, next.BytesReceived - first.BytesReceived) / elapsed) + " receive; " + Rate(Math.Max(0, next.BytesSent - first.BytesSent) / elapsed) + " send";
                            FinishTask("Adapter throughput measured locally; no network request sent");
                        }));
                        var oldDns = performanceDraft.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.Dns);
                        if (oldDns is not null)
                        {
                            adapters.SelectedItem = adapters.Items.OfType<System.Net.NetworkInformation.NetworkInterface>().FirstOrDefault(a => a.Id == oldDns.Target);
                            var value = JsonSerializer.Deserialize<DnsChoice>(oldDns.Value);
                            dns.SelectedIndex = value == new DnsChoice("", "") ? 0 : 3;
                            ipv4.Text = value?.IPv4 ?? "";
                            ipv6.Text = value?.IPv6 ?? "";
                        }
                        var dnsCheck = Check("Change adapter DNS", () =>
                        {
                            if (adapters.SelectedItem is not System.Net.NetworkInformation.NetworkInterface adapter || dns.SelectedIndex < 0)
                                throw new IOException("Choose an adapter and DNS selection.");
                            var choice = dns.SelectedIndex switch
                            {
                                0 => new DnsChoice("", ""),
                                1 => new("1.1.1.1,1.0.0.1", "2606:4700:4700::1111,2606:4700:4700::1001"),
                                2 => new("8.8.8.8,8.8.4.4", "2001:4860:4860::8888,2001:4860:4860::8844"),
                                _ => WindowsPerformance.NormalizeDns(ipv4.Text, ipv6.Text)
                            };
                            WindowsPerformance.ReadDns(adapter.Id);
                            return new(PlanActionKind.Dns, adapter.Id, JsonSerializer.Serialize(choice), "DNS: " + adapter.Name + " / " + dns.SelectedItem);
                        }, oldDns);
                        adapters.SelectionChanged += (_, _) => UpdateChecked(dnsCheck);
                        dns.SelectionChanged += (_, _) => UpdateChecked(dnsCheck);
                        ipv4.TextChanged += (_, _) => UpdateChecked(dnsCheck);
                        ipv6.TextChanged += (_, _) => UpdateChecked(dnsCheck);
                        Link("Windows network settings", "ms-settings:network-status");
                        break;
                    case 4:
                        editor.Children.Add(Text("Startup and background apps", Ui.SectionTitle, "#EDF3FF", true));

                        editor.Children.Add(Text("Disable selected registrations at your next sign-in. Restore them from Apex.", Ui.Supporting, "#A9BAD4"));
                        foreach (var entry in WindowsPerformance.StartupEntries())
                        {
                            var action = new PlanAction(entry.Kind, entry.Target, "Disabled", "Disable startup: " + entry.Label);
                            Check("Disable at sign-in: " + entry.Label, () => action, performanceDraft.Actions.FirstOrDefault(a => a.Kind == entry.Kind && a.Target == entry.Target));
                        }
                        Link("Windows startup apps", "ms-settings:startupapps");
                        editor.Children.Add(ActionButton("\uE7FC", "Manage running apps in Game Booster", (_, _) => { ClosePerformancePopup(); ShowPage("Game Booster"); }));
                        break;
                    case 6:
                        editor.Children.Add(Text("Visual effects", Ui.SectionTitle, "#EDF3FF", true));
                        editor.Children.Add(Text("Reduce animations. Keep smooth fonts and thumbnails.", Ui.Body, "#A9BAD4"));
                        editor.Children.Add(ActionButton("\uE73E", "Use performance appearance", (_, _) =>
                        {
                            foreach (var effect in WindowsVisualEffects.Supported)
                                performanceDraft.Toggle(new(PlanActionKind.VisualEffect, effect.Key, WindowsVisualEffects.PresetValue(effect.Key), "Appearance: " + effect.Value.Label), true);
                            performanceDraft.Toggle(new(PlanActionKind.Manual, "AppearanceGuided", "Keep thumbnails and smooth fonts enabled; turn other effects off in Windows Performance Options.", "Finish appearance in Windows"), true);
                            refreshPerformance?.Invoke();
                            ShowTaskNotice("Ready to review", "Appearance changes added", NoticeKind.Information);
                        }));
                        editor.Children.Add(ActionButton("\uE8A7", "Open Windows visual effects", (_, _) => { try { Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); } catch (Exception e) { ShowTaskNotice("Could not open settings", e.Message, NoticeKind.Error); } }));
                        editor.Children.Add(Text("Use Adjust for best performance, then enable thumbnails and smooth font edges. Apex applies its supported effects after review; finish the remaining switches in Windows.", Ui.Supporting, "#A9BAD4"));
                        break;
                    case 7:
                        editor.Children.Add(Text("Display", Ui.SectionTitle, "#EDF3FF", true));
                        editor.Children.Add(Text("Direct where supported: use the highest reported refresh rate at the unchanged resolution and configuration. Each change requires Keep within 15 seconds; a separate helper reverts unconfirmed changes. HDR and mirrored configurations remain Manual.", Ui.Body, "#A9BAD4"));
                        foreach (var display in WindowsDisplay.Choices())
                        {
                            editor.Children.Add(Text(display.Name, Ui.WidgetTitle, "#EDF3FF", true));
                            if (display.Highest is null)
                                editor.Children.Add(Text("Unavailable: " + display.Reason, Ui.Body, "#83DDFF"));
                            else
                            {
                                var selectedDisplay = performanceDraft.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.Display && a.Target == display.Target);
                                editor.Children.Add(Text("Current: " + DescribeSetting(new(PlanActionKind.Display, display.Target, "", ""), display.Current) + "\nHighest: " + DescribeSetting(new(PlanActionKind.Display, display.Target, "", ""), display.Highest), Ui.Body, "#A9BAD4"));
                                Check("Direct: highest refresh rate / " + display.Name, () => new(PlanActionKind.Display, display.Target, display.Highest, "Display: " + display.Name), selectedDisplay);
                            }
                        }
                        Link("Windows Advanced display settings", "ms-settings:display-advanced");
                        break;
                    default:
                        editor.Children.Add(Text("Advanced", Ui.SectionTitle, "#EDF3FF", true));
                        var app = new ComboBox { Header = "Selected non-system process", PlaceholderText = "Loading apps…", HorizontalAlignment = HorizontalAlignment.Stretch };
                        var priority = new ComboBox { Header = "Priority", PlaceholderText = "Choose priority", Items = { "Idle", "BelowNormal", "Normal", "AboveNormal" }, HorizontalAlignment = HorizontalAlignment.Stretch };
                        editor.Children.Add(app);
                        if (!verificationMode)
                            _ = RefreshApps(app);
                        editor.Children.Add(ActionButton("\uE72C", "Refresh processes", async (_, _) => await RefreshApps(app)));
                        editor.Children.Add(priority);
                        editor.Children.Add(Text("Direct change for this exact process instance. Priority can redistribute CPU time; it does not guarantee better FPS. Restoration requires the same process to remain running.", Ui.Supporting, "#A9BAD4"));
                        var existingPriority = performanceDraft.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.Priority);
                        if (existingPriority is not null && lastMonitor is not null)
                        {
                            app.ItemsSource = lastMonitor.Processes.Where(p => p.CanClose).Select(p => new ProcessChoice(p)).ToList();
                            app.SelectedItem = app.Items.OfType<ProcessChoice>().FirstOrDefault(p => p.Process.Id.ToString() == existingPriority.Target && p.Process.Started == existingPriority.ProcessStarted);
                            priority.SelectedItem = existingPriority.Value;
                        }
                        var priorityCheck = Check("Change selected process priority", () => app.SelectedItem is ProcessChoice p && priority.SelectedItem is string value ? new(PlanActionKind.Priority, p.Process.Id.ToString(), value, "Priority: " + p.Process.Name + " / " + value, p.Process.Started) : throw new IOException("Choose a non-system process and priority."), existingPriority);
                        app.SelectionChanged += (_, _) => UpdateChecked(priorityCheck);
                        priority.SelectionChanged += (_, _) => UpdateChecked(priorityCheck);
                        break;
                }
                var details = new StackPanel { Spacing = 10 };
                foreach (var text in editor.Children.OfType<TextBlock>().Where(t => (t.FontSize == Ui.Supporting && t.Text.Length > 130) || (t.FontSize == Ui.Body && t.Text.StartsWith("Manual: disable")) || (t.FontSize == Ui.Body && t.Text.StartsWith("Direct where supported:")) || t.Text.StartsWith("Screenshot target:")).ToArray())
                {
                    editor.Children.Remove(text);
                    details.Children.Add(text);
                }
                if (details.Children.Count > 0)
                    editor.Children.Add(new Expander { Header = "Details and limitations", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            catch (Exception e) { editor.Children.Add(Text("Unavailable: " + e.Message, Ui.Body, "#83DDFF")); }
        }
        Editor();
    }
    private async void ReviewPerformance()
    {
        try
        {
            var preset = performanceDraft.Snapshot();
            var content = new StackPanel { Spacing = Ui.Gap };
            var review = new StackPanel { Spacing = 12 };
            content.Children.Add(review);
            var provider = new SettingsProvider { ConfirmDisplay = ConfirmDisplayAsync };
            bool available = true;
            foreach (var action in preset.Actions)
            {
                if (action.Kind == PlanActionKind.Manual)
                    continue;
                else
                {
                    try
                    {
                        string before = await provider.ReadAsync(action, CancellationToken.None);
                        review.Children.Add(Text(action.Label + "\n" + DescribeSetting(action, before) + " → " + DescribeSetting(action, action.Value), Ui.Supporting, "#A9BAD4"));
                    }
                    catch (Exception e) { if (action.Kind != PlanActionKind.Priority) available = false; review.Children.Add(Text(action.Label + " - Unavailable: " + e.Message + (action.Kind == PlanActionKind.Priority ? "; skipped without blocking Windows tweaks" : ""), Ui.Body, "#83DDFF")); }
                }
            }
            int steps = preset.Actions.Count(a => a.Kind == PlanActionKind.Manual);
            Expander? manualSummary = null;
            if (steps > 0)
            {
                manualSummary = new Expander { Header = $"{steps} Windows/game steps to finish yourself", Content = Text(string.Join("\n", preset.Actions.Where(a => a.Kind == PlanActionKind.Manual).Select(a => a.Label.Replace("Manual: ", ""))), Ui.Supporting, "#A9BAD4"), HorizontalAlignment = HorizontalAlignment.Stretch };
                content.Children.Add(manualSummary);
            }
            var permissions = new List<string>();
            if (preset.Actions.Any(a => a.Kind == PlanActionKind.Dns))
                permissions.Add("DNS may interrupt connectivity and request administrator approval.");
            if (preset.Actions.Any(a => a.Kind is PlanActionKind.StartupRun or PlanActionKind.StartupShortcut))
                permissions.Add("Startup changes take effect at next sign-in.");
            if (preset.Actions.Any(a => a.Kind == PlanActionKind.Priority))
                permissions.Add("Priority applies to this process instance only.");
            if (preset.Actions.Any(a => a.Kind == PlanActionKind.PowerPlan))
                permissions.Add("Power may request administrator approval.");
            if (permissions.Count > 0)
                content.Children.Add(Text(string.Join(" ", permissions), Ui.Supporting, "#A9BAD4"));
            UIElement? apply = null;
            apply = Work("Confirm & apply", async (token, progress) =>
            {
                manualChecklist.Load();
                manualChecklist.Stage(preset);
                var outcome = await performanceStore.ApplyAsync(preset, provider, token);
                review.Children.Clear();
                review.Children.Add(Text(outcome.Message, Ui.Body, "#83DDFF", true));
                if (manualSummary is not null)
                    manualSummary.Visibility = Visibility.Collapsed;
                RenderManualGuidance(content, preset.Actions);
                if (outcome.Kind == TaskOutcomeKind.Success)
                    apply!.Visibility = Visibility.Collapsed;
                FinishTask(steps > 0 && outcome.Kind == TaskOutcomeKind.Success ? outcome with
                {
                    Kind = TaskOutcomeKind.Partial
                } : outcome);
                if (outcome.Kind == TaskOutcomeKind.Success)
                    performanceDraft.Clear();
                refreshPerformance?.Invoke();

            }, true);
            foreach (var button in Descendants(apply).OfType<Button>())
                if (!available)
                    button.IsEnabled = false;
            if (currentPage == "Performance")
                OpenPerformancePopup("Review pending changes", content, footer: apply);
        }
        catch (Exception e) { ShowTaskNotice("Could not review", e.Message, NoticeKind.Error); }
    }
    private void ShowRestorePerformance()
    {
        var content = new StackPanel { Spacing = Ui.Gap };
        var records = new StackPanel { Spacing = 8 };
        content.Children.Add(records);
        void Render()
        {
            records.Children.Clear();
            foreach (var change in performanceStore.Changes.Where(c => c.State != "Restored"))
                records.Children.Add(Text(change.Action.Label + " - " + change.State + (change.Error is null ? "" : "\n" + change.Error), Ui.Body, "#A9BAD4"));
            if (records.Children.Count == 0)
                records.Children.Add(Text("No changes need restoration.", Ui.Body, "#A9BAD4"));
        }
        content.Children.Add(Work("Restore recorded changes", async (token, progress) => { if (!await Confirm("Restore previous values?", "Settings changed outside Apex will be left untouched and reported as conflicts.", "Restore")) return; FinishTask(await performanceStore.RestoreAsync(new SettingsProvider { ConfirmDisplay = ConfirmDisplayAsync }, token)); Render(); refreshPerformance?.Invoke(); }, true));
        Render();
        OpenPerformancePopup("Restore changes", content);
    }
    private static string DescribeSetting(PlanAction action, string value)
    {
        if (value == "Disabled")
            return value;
        if (action.Kind == PlanActionKind.Mouse)
        {
            var m = WindowsMouse.Parse(value);
            return "Pointer acceleration " + (m.Acceleration == 0 ? "off" : "on") + "; thresholds " + m.Threshold1 + " / " + m.Threshold2;
        }
        if (action.Kind == PlanActionKind.Display)
        {
            var m = JsonSerializer.Deserialize<DisplayMode>(value);
            return m is null ? "Unavailable" : $"{m.Width} × {m.Height} / {m.Hertz} Hz";
        }
        if (action.Kind is PlanActionKind.StartupShortcut or PlanActionKind.StartupRun)
            return "Registered at sign-in";
        if (action.Kind == PlanActionKind.PowerPlan)
            return WindowsPerformance.PowerSchemes().FirstOrDefault(p => p.Id.ToString() == value)?.Name ?? value;
        if (action.Kind == PlanActionKind.Dns)
        {
            var dns = JsonSerializer.Deserialize<DnsChoice>(value);
            return dns is null ? "Unavailable" : "IPv4: " + (dns.IPv4.Length == 0 ? "Automatic" : dns.IPv4) + "; IPv6: " + (dns.IPv6.Length == 0 ? "Automatic" : dns.IPv6);
        }
        return value;
    }
    private static string Rate(double? bytes) => bytes is double value ? FileToolkit.Size((long)value) + "/s" : "Unavailable";
    private async Task<bool> ConfirmDisplayAsync()
    {
        var dialog = new ContentDialog { Title = "Keep this refresh rate?", Content = "Reverting in 15 seconds unless you choose Keep.", PrimaryButtonText = "Keep", CloseButtonText = "Revert", DefaultButton = ContentDialogButton.Close, XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int remaining = 15;
        timer.Tick += (_, _) => { remaining--; dialog.Content = $"Reverting in {remaining} seconds unless you choose Keep."; if (remaining <= 0) dialog.Hide(); };
        try
        {
            timer.Start();
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { timer.Stop(); }
    }
    private sealed record ActionChoice(PlanAction Action)
    {
        public override string ToString() => Action.Label + (Action.Kind == PlanActionKind.Manual ? " (manual)" : " (direct)");
    }
    private sealed record ProcessChoice(ProcessUsage Process)
    {
        public override string ToString() => Process.Name + $" · PID {Process.Id}";
    }
}
