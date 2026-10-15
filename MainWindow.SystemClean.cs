using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Apex;
public sealed partial class MainWindow
{
    private void SystemCleanPage()
    {
        var chosen = new HashSet<string>();
        var options = new List<UIElement>();
        var choices = new Dictionary<CheckBox, CleanItem>();
        var findings = new StackPanel { Spacing = 10 };
        var issues = new StackPanel();
        bool scanned = false;
        Action updateLayout = () => { };
        void Clear()
        {
            scanned = false;
            choices.Clear();
            findings.Children.Clear();
            issues.Children.Clear();
            updateLayout();
        }
        Action discoverGames = () => { };
        void Option(string key, string icon, string detail, bool available = true) => options.Add(OptionWidget(key, icon, detail, false, on => { if (on) chosen.Add(key); else chosen.Remove(key); Clear(); if (key == "Game junk" && on) discoverGames(); }, available));
        Option("Application junk", "\uE8A5", "Cached application files.");
        Option("Game junk", "\uE7FC", "Old logs and temporary files.");
        Option("System junk", "\uE7F4", "Old temporary files.");
        Option("Downloads", "\uE896", "Duplicate files.");
        Option("Backups", "\uE74E", "Backup copies you select.");
        Option("Older files", "\uE823", "Files older than your chosen age.");
        Option("Browser cache", "\uE774", "Cached files. Keeps history and cookies.");
        Option("Recycle Bin", "\uE74D", "Empty after confirmation.");
        page.Children.Add(WidgetGrid(new[] { options[0], options[1], options[2], options[6], options[7] }));
        var personalFiles = new Expander
        {
            Header = "Personal files & backups",
            Content = WidgetGrid(new[] { options[3], options[4], options[5] }),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        page.Children.Add(personalFiles);
        var selectCategories = ActionButton("\uE73A", "Select all categories", (_, _) => { personalFiles.IsExpanded = true; foreach (var toggle in options.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>()) if (toggle.IsEnabled) toggle.IsChecked = true; });
        page.Children.Add(selectCategories);
        var gamePanel = new StackPanel { Spacing = 12 };
        var gameStatus = Text("Detect games automatically, or add a folder.", Ui.Supporting, "#A9BAD4");
        gamePanel.Children.Add(gameStatus);
        var gameSelector = new ComboBox { Header = "Games", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        gamePanel.Children.Add(gameSelector);
        var discoveryDetails = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var discoveryExpander = new Expander { Header = "Detection details", Content = discoveryDetails, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch };
        bool discovering = false, discovered = false;
        var detected = new List<InstalledGame>();
        Button refreshGames = null!;
        refreshGames = ActionButton("\uE72C", "Detect games", async (_, _) => await DetectGames());
        gamePanel.Children.Add(refreshGames);
        async Task DetectGames()
        {
            if (discovering || working)
                return;
            discovering = true;
            refreshGames.IsEnabled = false;
            gameStatus.Text = "Detecting installed games…";
            try
            {
                var result = await Task.Run(WindowsGameDiscovery.Discover);
                if (!page.Children.Contains(gamePanel))
                    return;
                detected = result.Games;
                discovered = true;
                gameSelector.Items.Clear();
                gameSelector.Items.Add("All detected games");
                foreach (var game in detected)
                    gameSelector.Items.Add(game);
                gameSelector.SelectedIndex = 0;
                gameSelector.Visibility = detected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                gameStatus.Text = detected.Count == 0 ? "No games found. Add a folder below." : $"{detected.Count} games detected · {detected.Count(g => g.CleanupFolders.Count > 0)} with accessible cleanup folders";
                discoveryDetails.Text = "Only old .tmp, .log and .dmp files are eligible. Saves and shader caches stay untouched. Protected install folders are skipped; known user log folders can be scanned.\n" + string.Join("\n", result.Issues);
                discoveryExpander.Visibility = Visibility.Visible;
                Clear();
            }
            catch (Exception e) { gameStatus.Text = "Game detection unavailable. Add a folder below."; discoveryDetails.Text = e.Message; discoveryExpander.Visibility = Visibility.Visible; }
            finally { discovering = false; refreshGames.IsEnabled = true; }
        }
        discoverGames = () => { if (!discovered && !verificationMode) _ = DetectGames(); };
        gameSelector.SelectionChanged += (_, _) => Clear();
        var gameLocation = PathPicker("Game folder", out var games);
        gamePanel.Children.Add(new Expander { Header = "Add a game folder", Content = gameLocation, HorizontalAlignment = HorizontalAlignment.Stretch });
        gamePanel.Children.Add(discoveryExpander);
        page.Children.Add(gamePanel);
        var backupLocation = PathPicker("Backup folder", out var backups);
        page.Children.Add(backupLocation);
        games.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => { gameSelector.IsEnabled = string.IsNullOrWhiteSpace(games.Text); Clear(); });
        backups.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => Clear());
        var olderPanel = new StackPanel { Spacing = 12 };
        olderPanel.Children.Add(PathPicker("Older-file folder", out var olderDirectory));
        var age = new NumberBox { Header = "Last modified more than (days)", Minimum = 1, Maximum = 36500, Value = preferences.CleanupAgeDays, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        olderPanel.Children.Add(age);
        page.Children.Add(olderPanel);
        olderDirectory.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => Clear());
        age.ValueChanged += (_, _) => Clear();
        var scanAction = Work("Scan", async (token, progress) =>
        {
            if (chosen.Count == 0)
                throw new IOException("Include at least one cleanup category.");
            Clear();
            issues.Children.Clear();
            var collected = new List<CleanItem>();
            var errors = new List<string>();
            async Task Scan(string category, string root, Func<Entry, bool> filter, string? process = null)
            {
                if (process is not null && WindowsSystemClean.Running(process))
                {
                    errors.Add(category + ": close " + process + " yourself before scanning its cache.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(root))
                {
                    errors.Add(category + ": choose a folder.");
                    return;
                }
                try
                {
                    var scan = await FileToolkit.ScanAsync(root, true, progress, token);
                    errors.AddRange(scan.Issues);
                    collected.AddRange(scan.Files.Where(filter).Select(f => new CleanItem(new(f, category), category, scan.Root, process)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(category + ": " + e.Message); }
            }
            if (chosen.Contains("System junk"))
                await Scan("System junk", Path.GetTempPath(), f => Apex.Services.GameBooster.SafeTemporaryFile(f, Path.GetTempPath(), DateTime.UtcNow));
            if (chosen.Contains("Game junk"))
            {
                if (discovering)
                    throw new IOException("Wait for game detection to finish.");
                if (!string.IsNullOrWhiteSpace(games.Text))
                    await Scan("Game junk", games.Text, SystemCleanRules.GameJunk);
                else
                {
                    var selectedGames = gameSelector.SelectedItem is InstalledGame game ? new[] { game } : detected.ToArray();
                    var roots = selectedGames.SelectMany(g => g.CleanupFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (roots.Length == 0)
                        errors.Add("Game junk: no accessible cleanup folders. Detect games or add a folder.");
                    foreach (var root in roots)
                        await Scan("Game junk", root, SystemCleanRules.GameJunk);
                    foreach (var skipped in selectedGames.Where(g => g.CleanupFolders.Count == 0))
                        errors.Add(skipped.Name + ": protected install folder; no known user logs found.");
                }
            }
            if (chosen.Contains("Backups"))
                await Scan("Backups", backups.Text, SystemCleanRules.Backup);
            if (chosen.Contains("Older files"))
            {
                if (!double.IsFinite(age.Value))
                    throw new IOException("Enter a valid file age.");
                var cutoff = DateTime.UtcNow.AddDays(-age.Value);
                await Scan("Older files", olderDirectory.Text, f => f.ModifiedUtc < cutoff);
            }
            foreach (bool browser in new[] { false, true })
                if (chosen.Contains(browser ? "Browser cache" : "Application junk"))
                    foreach (var root in WindowsSystemClean.Caches(browser))
                        await Scan(browser ? "Browser cache" : "Application junk", root.Path, _ => true, root.Process);
            if (chosen.Contains("Downloads"))
            {
                string root = NativePickers.DownloadsPath();
                var scan = await FileToolkit.ScanAsync(root, true, progress, token);
                errors.AddRange(scan.Issues);
                collected.AddRange((await FileToolkit.DuplicatesAsync(scan, progress, token)).Select(c => new CleanItem(c, "Downloads", root)));
            }
            var keep = collected.Where(c => c.Candidate.Keep is not null).Select(c => c.Candidate.Keep!.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            collected = collected.Where(c => !keep.Contains(c.Candidate.File.Path)).DistinctBy(c => c.Candidate.File.Path, StringComparer.OrdinalIgnoreCase).OrderByDescending(c => c.Candidate.File.Size).ToList();
            token.ThrowIfCancellationRequested();
            scanned = true;
            findings.Children.Clear();
            findings.Children.Add(Text($"{collected.Count:N0} files · {FileToolkit.Size(collected.Sum(c => c.Candidate.File.Size))}", Ui.WidgetTitle, "#83DDFF", true));
            var selectionActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            selectionActions.Children.Add(ActionButton("\uE73A", collected.Count > 1000 ? "Select all shown" : "Select all", (_, _) => { foreach (var check in choices.Keys) check.IsChecked = true; }));
            selectionActions.Children.Add(ActionButton("\uE711", "Clear selection", (_, _) => { foreach (var check in choices.Keys) check.IsChecked = false; }));
            findings.Children.Add(selectionActions);
            var list = new ListView { MaxHeight = 400, SelectionMode = ListViewSelectionMode.None };
            foreach (var item in collected.Take(1000))
            {
                var check = new CheckBox { Content = Text(Path.GetFileName(item.Candidate.File.Path) + " · " + FileToolkit.Size(item.Candidate.File.Size), Ui.Supporting, "#A9BAD4") };
                ToolTipService.SetToolTip(check, item.Category + "\n" + item.Candidate.File.Path);
                choices[check] = item;
                list.Items.Add(check);
            }
            findings.Children.Add(list);
            if (chosen.Contains("Recycle Bin"))
            {
                var bin = WindowsSystemClean.RecycleBin();
                findings.Children.Add(Text($"Recycle Bin: {bin.Count:N0} items · {FileToolkit.Size(bin.Bytes)}. Permanently emptied by Clean.", Ui.Body, "#A9BAD4"));
            }
            Issues(issues, errors);
            updateLayout();
            FinishTask($"Found {collected.Count:N0} files", errors.Count);
        });
        page.Children.Add(scanAction);
        var findingsPanel = Glass(findings);
        page.Children.Add(findingsPanel);
        var issueDetails = new Expander { Header = "Skipped files and scan details", Content = issues, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(issueDetails);
        var cleanAction = Work("Clean", async (token, progress) =>
        {
            var selected = choices.Where(c => c.Key.IsChecked == true).Select(c => c.Value).ToArray();
            bool emptyBin = chosen.Contains("Recycle Bin");
            if (selected.Length == 0 && !emptyBin)
                throw new IOException("Select files to clean.");
            var bin = emptyBin ? WindowsSystemClean.RecycleBin() : default;
            string message = $"Recycle {selected.Length} files · {FileToolkit.Size(selected.Sum(c => c.Candidate.File.Size))}";
            if (emptyBin)
                message += $"\nPermanently empty the current Recycle Bin ({bin.Count} items · {FileToolkit.Size(bin.Bytes)}). This cannot be undone. New files are recycled afterward and remain recoverable.";
            if (!await Confirm("Clean selected items?", message, "Clean"))
                return;
            token.ThrowIfCancellationRequested();
            if (emptyBin && bin.Count > 0)
                await Task.Run(() => WindowsSystemClean.EmptyBin(hwnd), token);
            int completed = 0;
            var errors = new List<string>();
            foreach (var item in selected)
            {
                if (token.IsCancellationRequested)
                    break;
                try
                {
                    WindowsSystemClean.Recheck(item);
                    if (item.Candidate.Keep is { } original && await FileToolkit.HashAsync(original, token) != await FileToolkit.HashAsync(item.Candidate.File, token))
                        throw new IOException("Duplicate changed; skipped.");
                    await Task.Run(() => WindowsActions.Delete(item.Candidate.File, false, hwnd), token);
                    completed++;
                    progress.Report($"Recycled {completed}/{selected.Length}");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { errors.Add(item.Candidate.File.Path + ": " + e.Message); }
            }
            Clear();
            findings.Children.Add(Icon(errors.Count == 0 && !token.IsCancellationRequested ? "\uE73E" : "\uE7BA", 28, "#83DDFF"));
            findings.Children.Add(Text($"{completed} files cleaned" + (emptyBin ? " · Recycle Bin emptied" : ""), Ui.WidgetTitle, "#83DDFF", true));
            findingsPanel.Visibility = Visibility.Visible;
            Issues(issues, errors);
            issueDetails.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FinishTask(new TaskOutcome(token.IsCancellationRequested ? TaskOutcomeKind.Canceled : errors.Count > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, $"{completed} cleaned · {selected.Length - completed} skipped/not completed" + (emptyBin ? " · Recycle Bin emptied" : "")));
        }, true);
        var cancelScan = ActionButton("\uE711", "Cancel", (_, _) =>
        {
            Clear();
            DismissTaskNotice();
            status.Text = "Scan canceled · ready to scan again";
        });
        cancelScan.MinHeight = 48;
        cancelScan.Tag = "Discard cleanup scan";
        ToolTipService.SetToolTip(cancelScan, "Discard scan results. No files are changed.");
        var cleanupActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        cleanupActions.Children.Add(cleanAction);
        cleanupActions.Children.Add(cancelScan);
        page.Children.Add(cleanupActions);
        var more = new StackPanel { Spacing = 12 };
        more.Children.Add(ActionButton("\uE8A7", "Windows Storage cleanup", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:storagesense"))));
        page.Children.Add(new Expander { Header = "More cleanup tools", Content = more, HorizontalAlignment = HorizontalAlignment.Stretch });
        updateLayout = () => { gamePanel.Visibility = chosen.Contains("Game junk") ? Visibility.Visible : Visibility.Collapsed; backupLocation.Visibility = chosen.Contains("Backups") ? Visibility.Visible : Visibility.Collapsed; olderPanel.Visibility = chosen.Contains("Older files") ? Visibility.Visible : Visibility.Collapsed; scanAction.Visibility = scanned ? Visibility.Collapsed : Visibility.Visible; cleanupActions.Visibility = scanned ? Visibility.Visible : Visibility.Collapsed; cleanAction.Visibility = scanned && (choices.Count > 0 || chosen.Contains("Recycle Bin")) ? Visibility.Visible : Visibility.Collapsed; findingsPanel.Visibility = scanned ? Visibility.Visible : Visibility.Collapsed; issueDetails.Visibility = issues.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed; };
        Clear();
    }
}
