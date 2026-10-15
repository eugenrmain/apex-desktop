using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Apex;
public sealed partial class MainWindow
{
    private readonly GameBoostStore boostStore = new();
    private bool boostLoaded;
    private CancellationTokenSource? boostPageLifetime;
    private Action<IReadOnlyList<BoostAppGroup>>? renderBoostFixture;
    private void GameBoosterPage()
    {
        try
        {
            if (!boostLoaded)
            {
                boostStore.Load();
                boostLoaded = true;
            }
        }
        catch (Exception e) { page.Children.Add(Text("Could not open Game Booster: " + e.Message, Ui.Body, "#FF8294")); return; }
        boostPageLifetime = new();
        var lifetime = boostPageLifetime.Token;
        var provider = new WindowsGameBooster(boostStore, hwnd);
        var groups = new List<BoostAppGroup>();
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new Dictionary<string, ExecutableIdentity>(StringComparer.OrdinalIgnoreCase);
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var networkRates = new Dictionary<(int, long), double>();
        bool measuredNetwork = false, loading = false, firstLoad = true;
        var apps = Section("Apps to close");
        var observed = Text("Reading app usage…", Ui.Supporting, "#A9BAD4");
        apps.Children.Add(observed);
        var search = Field("", "Find an app");
        var sort = new ComboBox { Items = { "RAM", "CPU", "GPU", "Network", "Name" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 130 };
        Automation(sort, "Sort apps by resource");
        var filters = new Grid { ColumnSpacing = 8 };
        filters.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        filters.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        filters.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        filters.Children.Add(search);
        Grid.SetColumn(sort, 1);
        filters.Children.Add(sort);
        var rows = new StackPanel { Spacing = 4 };
        var selectionCount = Text("", Ui.Supporting, "#A9BAD4");
        var protectedApps = ActionButton("\uE72E", "Protected apps", (_, _) => { });
        protectedApps.Padding = new Thickness(8, 4, 8, 4);
        Button refresh = null!;
        refresh = ActionButton("\uE72C", "Refresh app usage", async (_, _) => await RefreshQuietly());
        refresh.Content = Icon("\uE72C", 16, "#DEEAFF");
        refresh.Width = 40;
        ToolTipService.SetToolTip(refresh, "Refresh app usage");
        Grid.SetColumn(refresh, 2);
        filters.Children.Add(refresh);
        apps.Children.Add(filters);
        apps.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var selectionFooter = new Grid();
        selectionFooter.Children.Add(selectionCount);
        selectionCount.VerticalAlignment = VerticalAlignment.Center;
        protectedApps.HorizontalAlignment = HorizontalAlignment.Right;
        selectionFooter.Children.Add(protectedApps);
        apps.Children.Add(selectionFooter);
        void UpdateCount()
        {
            selectionCount.Text = $"{wanted.Count} selected";
            protectedApps.Content = $"{groups.Count(g => g.Protection is not null)} protected";
        }
        void Render()
        {
            rows.Children.Clear();
            checks.Clear();
            var visible = groups.Where(g => g.Protection is null && (g.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || g.Executable.Contains(search.Text, StringComparison.OrdinalIgnoreCase)));
            visible = sort.SelectedIndex switch
            {
                1 => visible.OrderByDescending(g => g.Cpu),
                2 => visible.OrderByDescending(g => g.Gpu ?? -1),
                3 => visible.OrderByDescending(g => g.NetworkBytesPerSecond ?? -1),
                4 => visible.OrderBy(g => g.Name),
                _ => visible.OrderByDescending(g => g.Memory)
            };
            foreach (var group in visible)
            {
                var line = new Grid { ColumnSpacing = 8, Padding = new Thickness(2, 8, 2, 8) };
                line.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                line.ColumnDefinitions.Add(new()
                {
                    Width = GridLength.Auto
                });
                var copy = new StackPanel { Spacing = 4 };
                string display = BoostDisplayName(group.Name);
                copy.Children.Add(Text(display, Ui.Body, "#EDF3FF", true));
                string gpu = group.Gpu.HasValue ? $"{group.Gpu:0.#}%" : "—", net = group.NetworkBytesPerSecond.HasValue ? Rate(group.NetworkBytesPerSecond) : "—";
                copy.Children.Add(Text($"CPU {group.Cpu:0.#}% · GPU {gpu} · RAM {FileToolkit.Size(group.Memory)} · Network {net}", Ui.Supporting, "#A9BAD4"));
                var check = new CheckBox { Content = copy, IsChecked = wanted.Contains(group.Executable), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = !pending.Contains(group.Executable) };
                checks[group.Executable] = check;
                Automation(check, "Close " + display);
                ToolTipService.SetToolTip(check, group.Executable + "\n" + group.Instances.Count + " processes · network is a short TCP/UDP sample, including local traffic");
                line.Children.Add(check);
                check.Checked += async (_, _) =>
                {
                    wanted.Add(group.Executable);
                    pending.Add(group.Executable);
                    check.IsEnabled = false;
                    UpdateCount();
                    try
                    {
                        var identity = await Task.Run(() => WindowsGameBooster.Identify(group.Executable), lifetime);
                        if (!wanted.Contains(group.Executable) || lifetime.IsCancellationRequested)
                            return;
                        boostStore.Approve(identity, true);
                        identities[group.Executable] = identity;
                    }
                    catch (Exception e) { wanted.Remove(group.Executable); identities.Remove(group.Executable); if (!lifetime.IsCancellationRequested) ShowTaskNotice("App not selected", e.Message, NoticeKind.Error); }
                    finally { pending.Remove(group.Executable); if (checks.TryGetValue(group.Executable, out var current)) { current.IsChecked = wanted.Contains(group.Executable); current.IsEnabled = !working; } UpdateCount(); }
                };
                check.Unchecked += (_, _) => { wanted.Remove(group.Executable); identities.Remove(group.Executable); try { var old = boostStore.Profile.Approved.FirstOrDefault(a => a.Path.Equals(group.Executable, StringComparison.OrdinalIgnoreCase)); if (old is not null) boostStore.Approve(old, false); } catch (Exception e) { ShowTaskNotice("Could not save selection", e.Message, NoticeKind.Error); } UpdateCount(); };
                var menu = new MenuFlyout();
                var protect = new MenuFlyoutItem { Text = "Keep this app open" };
                protect.Click += (_, _) => { try { boostStore.Protect(group.Executable, true); wanted.Remove(group.Executable); identities.Remove(group.Executable); groups = groups.Select(g => g.Executable == group.Executable ? g with { Protection = "Protected by you" } : g).ToList(); Render(); } catch (Exception e) { ShowTaskNotice("Could not protect app", e.Message, NoticeKind.Error); } };
                menu.Items.Add(protect);
                var more = ActionButton("\uE712", display + " options", (_, _) => { });
                more.Content = Icon("\uE712", 14, "#A9BAD4");
                more.Width = 32;
                more.Padding = new Thickness(4);
                more.Flyout = menu;
                Grid.SetColumn(more, 1);
                line.Children.Add(more);
                rows.Children.Add(line);
            }
            if (rows.Children.Count == 0)
                rows.Children.Add(Text(loading ? "Reading app usage…" : "No matching apps available to close.", Ui.Supporting, "#A9BAD4"));
            UpdateCount();
        }
        search.TextChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        async Task RefreshApps(CancellationToken token)
        {
            if (loading)
                return;
            loading = true;
            refresh.IsEnabled = false;
            try
            {
                using var monitor = new WindowsMonitoring();
                using var gpuCounter = new PdhCounter("\\GPU Engine(*)\\Utilization Percentage");
                gpuCounter.GpuProcesses();
                await monitor.SampleAsync(token);
                await Task.Delay(1000, token);
                var snapshot = await monitor.SampleAsync(token);
                var gpuValues = gpuCounter.GpuProcesses();
                var candidates = await Task.Run(() =>
                {
                    var items = new List<BoostCandidate>();
                    foreach (var family in snapshot.Processes.Where(p => p.Executable.Length > 0).GroupBy(p => p.Executable, StringComparer.OrdinalIgnoreCase))
                    {
                        token.ThrowIfCancellationRequested();
                        var representative = family.First();
                        var app = new AppTask(representative.Id, representative.Started, representative.Name, representative.Executable, representative.CanClose);
                        string? reason = family.Any(p => !p.CanClose) ? "System or inaccessible process" : provider.Protection(app);
                        foreach (var process in family)
                            items.Add(new(new(process.Id, process.Started, process.Name, process.Executable, process.CanClose), process.Cpu, process.Memory, reason, gpuValues is null ? null : gpuValues.GetValueOrDefault(process.Id), measuredNetwork && networkRates.TryGetValue((process.Id, process.Started), out var rate) ? rate : null));
                    }
                    return items;
                }, token);
                if (token.IsCancellationRequested || currentPage != "Game Booster")
                    return;
                groups = BoostApps.Group(candidates);
                wanted.RemoveWhere(path => !groups.Any(g => g.Protection is null && g.Executable.Equals(path, StringComparison.OrdinalIgnoreCase)));
                if (firstLoad)
                {
                    foreach (var group in groups.Where(g => g.Protection is null))
                    {
                        var remembered = boostStore.Profile.Approved.FirstOrDefault(a => a.Path.Equals(group.Executable, StringComparison.OrdinalIgnoreCase));
                        if (remembered is null)
                            continue;
                        try
                        {
                            var actual = await Task.Run(() => WindowsGameBooster.Identify(group.Executable), token);
                            if (actual == remembered)
                            {
                                wanted.Add(group.Executable);
                                identities[group.Executable] = actual;
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                    firstLoad = false;
                }
                observed.Text = $"CPU {snapshot.Cpu:0.#}% · RAM {FileToolkit.Size(snapshot.MemoryUsed)} · Updated {DateTime.Now:HH:mm:ss}" + (measuredNetwork ? " · Network sample" : "");
                Render();
            }
            finally { loading = false; refresh.IsEnabled = true; }
        }
        async Task RefreshQuietly()
        {
            try
            {
                await RefreshApps(lifetime);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!lifetime.IsCancellationRequested) { observed.Text = "Could not read apps: " + e.Message; Render(); } }
        }
        var network = Work("Scan network usage", async (token, progress) =>
        {
            if (loading)
                throw new IOException("Wait for the app scan to finish.");
            if (groups.Count == 0)
                await RefreshApps(token);
            progress.Report("Approve administrator access, then measuring traffic for 5 seconds…");
            var requested = groups.SelectMany(g => g.Instances).Select(p => new NetworkProcess(p.Id, p.Started)).Distinct().ToList();
            networkRates = await NetworkScan.RunAsync(requested, token);
            measuredNetwork = true;
            await RefreshApps(token);
            sort.SelectedIndex = 3;
            FinishTask("Network usage measured · 5-second TCP/UDP sample");
        });
        apps.Children.Insert(2, network);
        page.Children.Add(Glass(apps));
        var cleanup = Section("Optional cleanup");
        cleanup.Children.Add(Text("Included in the preparation review.", Ui.Supporting, "#A9BAD4"));
        var categories = new HashSet<string>();
        var choices = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        for (int i = 0; i < 3; i++)
            choices.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        foreach (string category in new[] { "Temporary files", "Application junk", "Game junk", "Browser cache", "Recycle Bin" })
        {
            int index = choices.Children.Count;
            if (index % 3 == 0)
                choices.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            var check = new CheckBox { Content = category };
            Automation(check, category);
            check.Checked += (_, _) => categories.Add(category);
            check.Unchecked += (_, _) => categories.Remove(category);
            Grid.SetColumn(check, index % 3);
            Grid.SetRow(check, index / 3);
            choices.Children.Add(check);
        }
        cleanup.Children.Add(choices);
        page.Children.Add(Glass(cleanup));
        var resultPanel = new StackPanel { Spacing = 6 };
        page.Children.Add(Work("Prepare for gaming", async (token, progress) =>
        {
            if (loading || pending.Count > 0)
                throw new IOException("Wait for app details to finish loading.");
            var closing = groups.Where(g => g.Protection is null && wanted.Contains(g.Executable)).Select(g => new BoostAppSelection(g.Instances[0], identities.TryGetValue(g.Executable, out var id) ? id : throw new IOException("Review " + g.Name + " again."), g.Instances)).ToArray();
            if (closing.Length == 0 && categories.Count == 0)
                throw new IOException("Select apps or cleanup categories first.");
            var preview = await BoostCleanup.ScanAsync(categories, closing.Select(c => c.App.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), progress, token);
            var selectedFiles = await ReviewPreparation(closing, preview, categories.Contains("Recycle Bin"));
            if (selectedFiles is null)
                return;
            var result = await ReviewedPreparation.RunAsync(closing, selectedFiles, preview.BinCount > 0 && categories.Contains("Recycle Bin"), provider, provider, boostStore.Record, progress, token);
            if (preview.Issues.Count > 0)
            {
                result.Actions.Add(new("Scan", "Cleanup scan", "Skipped", string.Join("\n", preview.Issues.Take(12))));
                boostStore.Record(result);
            }
            wanted.Clear();
            identities.Clear();
            FinishTask(result.Outcome);
            RenderResults();
            await RefreshApps(CancellationToken.None);
        }, true));
        page.Children.Add(resultPanel);
        void RenderResults()
        {
            resultPanel.Children.Clear();
            if (boostStore.LastResult is not { } result)
                return;
            resultPanel.Children.Add(Text(result.Outcome.Message, Ui.Supporting, "#A9BAD4"));
            resultPanel.Children.Add(ActionButton("\uE946", "Results", (_, _) => ShowBoostResults(provider)));
        }
        protectedApps.Click += (_, _) =>
        {
            var content = new StackPanel { Spacing = 8 };
            foreach (var group in groups.Where(g => g.Protection is not null))
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(Text(BoostDisplayName(group.Name) + " · " + group.Protection, Ui.Supporting, "#A9BAD4"));
                if (group.Protection == "Protected by you")
                    row.Children.Add(ActionButton("\uE785", "Allow selection", async (_, _) => { boostStore.Protect(group.Executable, false); ClosePerformancePopup(); await RefreshQuietly(); }));
                content.Children.Add(row);
            }
            if (content.Children.Count == 0)
                content.Children.Add(Text("No protected apps in this snapshot.", Ui.Body, "#A9BAD4"));
            OpenPerformancePopup("Protected apps", content);
        };
        ToolTipService.SetToolTip(protectedApps, "View apps that will stay open");
        renderBoostFixture = fixture => { groups = fixture.ToList(); observed.Text = "Verification fixture · no apps are closed"; Render(); };
        bool started = false;
        apps.Loaded += async (_, _) => { if (started || verificationMode) return; started = true; await RefreshQuietly(); };
        RenderResults();
        Render();
    }
    private static string BoostDisplayName(string name) => name.ToLowerInvariant() switch { "chrome" => "Google Chrome", "msedge" => "Microsoft Edge", "firefox" => "Firefox", "discord" => "Discord", "msteams" or "teams" => "Microsoft Teams", "spotify" => "Spotify", "slack" => "Slack", _ => name };
    private async Task<CleanItem[]?> ReviewPreparation(IReadOnlyList<BoostAppSelection> apps, BoostCleanupPreview preview, bool includeBin)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text($"{apps.Count} apps · {preview.Files.Count} files ({FileToolkit.Size(preview.Files.Sum(f => f.Candidate.File.Size))})", Ui.Body, "#EDF3FF", true));
        if (apps.Count > 0)
            content.Children.Add(Text(string.Join(", ", apps.Select(a => BoostDisplayName(a.App.Name))) + "\nApps may ask to save. Tray-only apps may need Quit.", Ui.Supporting, "#A9BAD4"));
        if (includeBin)
            content.Children.Add(Text($"Permanently empty existing Recycle Bin: {preview.BinCount} items · {FileToolkit.Size(preview.BinBytes)}. Newly cleaned files are recycled afterward and stay recoverable.", Ui.Supporting, "#F4C17B"));
        var selected = new Dictionary<CheckBox, CleanItem>();
        if (preview.Files.Count > 0)
        {
            var list = new StackPanel { Spacing = 5 };
            var all = new CheckBox { Content = "Select all shown", IsChecked = true };
            list.Children.Add(all);
            foreach (var file in preview.Files)
            {
                var check = new CheckBox { Content = Path.GetFileName(file.Candidate.File.Path) + " · " + FileToolkit.Size(file.Candidate.File.Size), IsChecked = true };
                ToolTipService.SetToolTip(check, file.Candidate.File.Path);
                selected[check] = file;
                list.Children.Add(check);
            }
            all.Checked += (_, _) => { foreach (var check in selected.Keys) check.IsChecked = true; };
            all.Unchecked += (_, _) => { foreach (var check in selected.Keys) check.IsChecked = false; };
            content.Children.Add(new Expander { Header = "Review files", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = list });
        }
        if (preview.Issues.Count > 0)
            content.Children.Add(new Expander { Header = $"{preview.Issues.Count} scan notes", Content = Text(string.Join("\n", preview.Issues.Take(12)), Ui.Supporting, "#A9BAD4") });
        var dialog = new ContentDialog { Title = "Review preparation", Content = new ScrollViewer { Content = content, MaxHeight = 360 }, PrimaryButtonText = "Prepare", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? selected.Where(p => p.Key.IsChecked == true).Select(p => p.Value).ToArray() : null;
    }
    private void ShowBoostResults(WindowsGameBooster provider)
    {
        if (boostStore.LastResult is not { } result)
            return;
        var content = new StackPanel { Spacing = 12 };
        bool actionRunning = false;
        foreach (var item in result.Actions)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            var copy = new StackPanel { Spacing = 4 };
            copy.Children.Add(Text(Path.GetFileName(item.Target) + " · " + item.State, Ui.Body, "#EDF3FF", true));
            copy.Children.Add(Text(item.Detail, Ui.Supporting, "#A9BAD4"));
            row.Children.Add(copy);
            if (item.App is { } app && item.State is "Completed" or "Skipped")
            {
                var menu = new MenuFlyout();
                var action = new MenuFlyoutItem { Text = item.State == "Completed" ? "Reopen app" : "Force close…" };
                action.Click += async (_, _) =>
                {
                    if (actionRunning)
                        return;
                    actionRunning = true;
                    action.IsEnabled = false;
                    try
                    {
                        if (item.State == "Completed")
                        {
                            WindowsGameBooster.Reopen(app);
                            ShowTaskNotice("App opened", "Unsaved work is not restored.", NoticeKind.Information);
                        }
                        else
                        {
                            if (!await Confirm("Force close app?", app.App.Name + "\nUnsaved work can be lost.", "Force close"))
                                return;
                            bool closed = await provider.CloseAsync(app, true, CancellationToken.None);
                            if (!closed)
                                throw new IOException("App is still running.");
                            var updated = result with
                            {
                                Actions = result.Actions.Select(a => a == item ? a with { State = "Completed", Detail = "Force closed after confirmation" } : a).ToList()
                            };
                            boostStore.Record(updated);
                            ClosePerformancePopup();
                            ShowBoostResults(provider);
                        }
                    }
                    catch (Exception e) { ShowTaskNotice("Could not complete", e.Message, NoticeKind.Error); }
                    finally { actionRunning = false; action.IsEnabled = true; }
                };
                menu.Items.Add(action);
                var more = ActionButton("\uE712", "App actions", (_, _) => { });
                more.Content = Icon("\uE712", 16, "#A9BAD4");
                more.Flyout = menu;
                Grid.SetColumn(more, 1);
                row.Children.Add(more);
            }
            content.Children.Add(row);
        }
        OpenPerformancePopup("Preparation results", content);
    }
}
