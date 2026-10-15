using System.Text.Json;
using Apex.Services;
using Apex.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Apex;

public sealed partial class MainWindow
{
    private static readonly string[] VerificationRoutes = ["Home", "System", "PerformanceHub", "Media", "Storage", "Organize", "File Actions", "System Clean", "Compress", "Convert", "Diagnostics", "Performance", "Game Booster", "Settings"];
    // Opt-in developer verification: render only Apex's own content, never the desktop.
    private async Task VerifyAsync()
    {
        var args = Environment.GetCommandLineArgs();
        bool layoutOnly = args.Contains("--verify-layout");
        bool bounded = args.Contains("--verify-bounded");
        int index = Array.IndexOf(args, layoutOnly ? "--verify-layout" : bounded ? "--verify-bounded" : "--verify");
        var output = index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "verification");
        Directory.CreateDirectory(output);
        var checks = new List<object>();
        try
        {
            // Activate the desktop picker APIs against this live window without opening dialogs or selecting user files.
            _ = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
            _ = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            var functionalChecks = layoutOnly ? new List<string>() : bounded ? await ReleaseChecks.RunAsync(Path.Combine(output, "bounded-" + Guid.NewGuid().ToString("N"))) : await ToolkitChecks.RunAsync(Path.Combine(output, "fixtures-" + Guid.NewGuid().ToString("N")));
            if (!layoutOnly)
                functionalChecks.AddRange(await MediaCompressionChecks.RunAsync(Path.Combine(output, "media-fixtures-" + Guid.NewGuid().ToString("N"))));
            string conversionChecksRoot = Path.Combine(output, "conversion-fixtures-" + Guid.NewGuid().ToString("N"));
            if (!layoutOnly)
                functionalChecks.AddRange(await MediaConversionChecks.RunAsync(conversionChecksRoot));
            foreach (var size in new[] { new SizeInt32(1100, 760), new SizeInt32(800, 600), new SizeInt32(1600, 1000) })
            {
                AppWindow.Resize(size);
                foreach (var route in VerificationRoutes)
                {
                    ShowPage(route);
                    await Task.Delay(400);
                    root.UpdateLayout();
                    var buttons = Descendants(root).OfType<Button>().ToArray();
                    bool noSampleContent = !Descendants(root).OfType<TextBlock>().Any(t => t.Text.Contains("sample", StringComparison.OrdinalIgnoreCase));
                    if (!noSampleContent)
                        throw new InvalidOperationException("Sample text remains.");
                    foreach (var nav in navigation.Values)
                        if (!nav.IsTabStop || string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(nav)))
                            throw new InvalidOperationException("Navigation lacks keyboard or accessibility support.");
                    var trail = page.Children.OfType<StackPanel>().SingleOrDefault(p => p.Tag?.ToString() == "Breadcrumbs");
                    if (trail is null || !trail.Children.OfType<Button>().Any(b => b.Tag?.ToString() == "Home" && Descendants(b).OfType<FontIcon>().Any(i => i.Glyph == "\uE80F")))
                        throw new IOException("Universal Home breadcrumb missing.");
                    if (IsWorkspace(route))
                    {
                        var tiles = page.Children.OfType<Grid>().Single(g => g.Tag?.ToString() == "Workspace cards");
                        int expected = route == "System" ? 4 : route == "Media" ? 2 : 3;
                        if (tiles.Children.Count != expected || Descendants(page).OfType<TextBox>().Any())
                            throw new IOException("Workspace does not show only its tool cards.");
                    }
                    CheckNavigationCardSizing(route);
                    double settingsTop = navigation["Settings"].TransformToVisual(root).TransformPoint(new(0, 0)).Y;
                    foreach (var nav in navigation.Where(n => n.Key != "Settings").Select(n => n.Value))
                        if (nav.TransformToVisual(root).TransformPoint(new(0, 0)).Y + nav.ActualHeight > settingsTop + 1)
                            throw new IOException("Navigation is clipped above Settings.");
                    var rendered = await CaptureAsync(Path.Combine(output, $"{route.Replace(' ', '-')}-{size.Width}x{size.Height}.png"));
                    var scroll = Descendants(root).OfType<ScrollViewer>().First(s => ReferenceEquals(s.Content, page));
                    CheckLeftAlignment(scroll);
                    bool scrollable = scroll.ScrollableHeight > 0;
                    if (scrollable)
                    {
                        scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                        await Task.Delay(200);
                        await CaptureAsync(Path.Combine(output, $"{route.Replace(' ', '-')}-{size.Width}x{size.Height}-bottom.png"));
                        scroll.ChangeView(null, 0, null, true);
                    }
                    checks.Add(new
                    {
                        route,
                        scaling = root.XamlRoot.RasterizationScale,
                        width = rendered.PixelWidth,
                        height = rendered.PixelHeight,
                        noSampleContent,
                        keyboardNavigationConfigured = true,
                        scrollable
                    });
                }
            }
            AppWindow.Resize(new SizeInt32(800, 600));
            ShowPage("Game Booster");
            var cleanupChecks = Descendants(page).OfType<CheckBox>().Where(c => new[] { "Temporary files", "Application junk", "Game junk", "Browser cache", "Recycle Bin" }.Contains(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c))).ToArray();
            if (cleanupChecks.Length != 5 || cleanupChecks.Any(c => c.IsChecked == true))
                throw new IOException("Booster cleanup does not start with five optional unchecked categories.");
            if (navigation.Count != 5 || !navigation.Keys.Order().SequenceEqual(new[] { "Home", "Media", "Performance", "Settings", "System" }))
                throw new IOException("Primary navigation is not grouped.");
            var fakeFirefox = new AppTask(101, 111, "firefox", Path.Combine(output, "firefox.exe"), true);
            var fakeDiscord = new AppTask(102, 222, "discord", Path.Combine(output, "discord.exe"), true);
            renderBoostFixture?.Invoke(new[] { new BoostAppGroup("firefox", fakeFirefox.Executable, 8.2, 650_000_000, [fakeFirefox, fakeFirefox with { Id = 103 }], null, 15, 128_000), new BoostAppGroup("discord", fakeDiscord.Executable, 1.2, 200_000_000, [fakeDiscord], null, 2, 8_000) });
            await Task.Delay(150);
            await CaptureAsync(Path.Combine(output, "Game-Booster-resource-fixture.png"));
            if (Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Refresh background apps") || !Descendants(page).OfType<TextBlock>().Any(t => t.Text.Contains("GPU 15%") && t.Text.Contains("Network")))
                throw new IOException("Grouped app resources or simplified actions missing.");
            checks.Add(new
            {
                route = "Game Booster",
                cleanupStartsUnchecked = true,
                fiveCleanupCategories = true,
                groupedResources = true,
                explicitNetworkScan = true,
                fixtureOnly = true
            });
            foreach (string group in new[] { "System", "Performance", "Media" })
            {
                await InvokeNavigation(navigation[group]);
                if (currentPage != WorkspaceRoute(group))
                    throw new IOException("Rail entered a tool instead of the workspace landing page.");
                foreach (string tool in group == "System" ? SystemPages : group == "Media" ? MediaPages : PerformancePages)
                {
                    var cards = page.Children.OfType<Grid>().Single(g => g.Tag?.ToString() == "Workspace cards");
                    await InvokeNavigation(cards.Children.OfType<Button>().Single(b => b.Tag?.ToString() == tool));
                    var trail = page.Children.OfType<StackPanel>().Single(p => p.Tag?.ToString() == "Breadcrumbs");
                    if (currentPage != tool || !trail.Children.OfType<TextBlock>().Any(t => t.Text == RouteTitle(tool)))
                        throw new IOException("Card did not open the named tool with its full breadcrumb.");
                    await InvokeNavigation(trail.Children.OfType<Button>().Single(b => b.Tag?.ToString() == WorkspaceRoute(group)));
                    if (currentPage != WorkspaceRoute(group))
                        throw new IOException("Parent breadcrumb did not return to the landing page.");
                }
                var homeTrail = page.Children.OfType<StackPanel>().Single(p => p.Tag?.ToString() == "Breadcrumbs");
                await InvokeNavigation(homeTrail.Children.OfType<Button>().Single(b => b.Tag?.ToString() == "Home"));
                if (currentPage != "Home")
                    throw new IOException("Home breadcrumb did not return home.");
                await InvokeNavigation(Descendants(page).OfType<Button>().Single(b => b.Tag?.ToString() == group));
                if (currentPage != WorkspaceRoute(group))
                    throw new IOException("Home card resumed the last tool instead of showing workspace cards.");
                checks.Add(new
                {
                    route = group,
                    actualCardNavigation = true,
                    clickableHomeAndParentBreadcrumbs = true,
                    workspaceDoesNotResumeTool = true
                });
            }
            checks.Add(new
            {
                route = "Navigation",
                threeGroups = true,
                workspaceLandingPages = true,
                breadcrumbHierarchy = true
            });
            ShowPage("Convert");
            if (!Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Back to Media") || SystemPages.Contains("Compress") || Descendants(page).OfType<ComboBox>().Any())
                throw new IOException("Media navigation or minimal converter controls are incorrect.");
            var conversionField = Descendants(page).OfType<TextBox>().First(t => t.Header?.ToString() == "Video to convert");
            string conversionFixture = Path.Combine(output, "conversion-selection.mp4");
            File.WriteAllText(conversionFixture, "UI metadata fixture; not encoded by this check");
            conversionField.Text = conversionFixture;
            await Task.Delay(200);
            if (!Descendants(page).OfType<TextBlock>().Any(t => t.Text == "conversion-selection.mp4") || !Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Convert to MP3" && b.IsEnabled && b.MinHeight >= 48))
                throw new IOException("Converter selection did not show the filename and primary action.");
            await CaptureAsync(Path.Combine(output, "Convert-selected-fixture.png"));
            OpenWorkspace("System");
            OpenWorkspace("Media");
            if (currentPage != "Media")
                throw new IOException("Media resumed conversion instead of showing its landing page.");
            await InvokeNavigation(page.Children.OfType<Grid>().Single(g => g.Tag?.ToString() == "Workspace cards").Children.OfType<Button>().Single(b => b.Tag?.ToString() == "Convert"));
            if (currentPage != "Convert" || conversionSource != conversionFixture)
                throw new IOException("Converter file state was lost through the landing page.");
            if (!layoutOnly)
            {
                string mp3 = Path.Combine(conversionChecksRoot, "audio-fixture.mp3");
                conversionResult = new(mp3, new FileInfo(mp3).Length, TimeSpan.FromSeconds(3));
                ShowPage("Convert");
                await Task.Delay(200);
                if (!Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Open MP3"))
                    throw new IOException("Converted output is not accessible after navigation.");
                await CaptureAsync(Path.Combine(output, "Convert-completed-fixture.png"));
            }
            conversionSource = "";
            conversionResult = null;
            checks.Add(new
            {
                route = "Media",
                compressionMoved = true,
                minimalConverter = true,
                selectedFileVisible = true,
                landingPageAndSelectionRetained = true
            });
            ShowPage("System Clean");
            if (!Descendants(page).OfType<Expander>().Any(e => e.Header?.ToString() == "Personal files & backups" && !e.IsExpanded))
                throw new IOException("Personal files are not separated from disposable cleanup categories.");
            var cleanWidgets = Descendants(page).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().ToArray();
            if (cleanWidgets.Any(w => w.IsChecked == true))
                throw new IOException("Cleanup categories start selected.");
            var cacheWidget = cleanWidgets.First(w => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(w) == "Browser cache");
            cacheWidget.IsChecked = true;
            if (!((StackPanel)cacheWidget.Content).Children.OfType<TextBlock>().Any(t => t.Text == "Included"))
                throw new IOException("Included cleanup state is not visible.");
            cacheWidget.IsChecked = false;
            checks.Add(new
            {
                route = "System Clean",
                nothingSelectedInitially = true,
                widgetStates = true
            });
            var cleanupFixture = Path.Combine(output, "cleanup-cancel-fixture");
            Directory.CreateDirectory(cleanupFixture);
            var cleanupFile = Path.Combine(cleanupFixture, "keep-this-file.txt");
            File.WriteAllText(cleanupFile, "Cleanup cancellation must preserve this file.");
            File.SetLastWriteTimeUtc(cleanupFile, DateTime.UtcNow.AddYears(-2));
            Descendants(page).OfType<Expander>().Single(e => e.Header?.ToString() == "Personal files & backups").IsExpanded = true;
            root.UpdateLayout();
            await Task.Delay(100);
            var olderWidget = Descendants(page).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().First(w => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(w) == "Older files");
            olderWidget.IsChecked = true;
            var olderField = Descendants(page).OfType<TextBox>().Single(t => t.Header?.ToString() == "Older-file folder");
            olderField.Text = cleanupFixture;
            async Task ScanCleanupFixture()
            {
                await InvokeNavigation(Descendants(page).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Scan"));
                for (int attempt = 0; working && attempt < 100; attempt++)
                    await Task.Delay(50);
                root.UpdateLayout();
                if (working || !Descendants(page).OfType<CheckBox>().Any(c => c.Content is TextBlock t && t.Text.StartsWith("keep-this-file.txt")))
                    throw new IOException("Cleanup fixture scan did not finish with its candidate.");
            }
            await ScanCleanupFixture();
            var discardScan = Descendants(page).OfType<Button>().Single(b => b.Tag?.ToString() == "Discard cleanup scan");
            await CaptureAsync(Path.Combine(output, "Cleanup-cancel-results.png"));
            await InvokeNavigation(discardScan);
            if (((FrameworkElement)discardScan.Parent).Visibility != Visibility.Collapsed || Descendants(page).OfType<CheckBox>().Any() || olderField.Text != cleanupFixture || olderWidget.IsChecked != true || File.ReadAllText(cleanupFile) != "Cleanup cancellation must preserve this file.")
                throw new IOException("Cancel did not discard results while preserving scan options and files.");
            await ScanCleanupFixture();
            await InvokeNavigation(discardScan);
            checks.Add(new
            {
                route = "System Clean",
                cancelDiscardsResults = true,
                optionsRetained = true,
                rescanWorks = true,
                sourceFileUnchanged = true,
                fixtureOnly = true
            });
            ShowPage("Performance");
            var fixtureA = new PerformancePlan(Guid.NewGuid(), "Fixture A", [], "");
            var fixtureB = new PerformancePlan(Guid.NewGuid(), "Fixture B", [], "");
            performanceStore.Plans.AddRange([fixtureA, fixtureB]);
            performanceDraft.Load(fixtureB);
            ShowPerformanceCategory(-1);
            await Task.Delay(150);
            var customSelector = Descendants(performancePopup!).OfType<ComboBox>().First();
            if (customSelector.SelectedItem is not PerformancePlan selectedCustom || selectedCustom.Id != fixtureB.Id)
                throw new IOException("Saved Custom preset selection lost its identity.");
            customSelector.SelectedItem = fixtureA;
            if (performanceDraft.CustomId != fixtureA.Id || (customSelector.SelectedItem as PerformancePlan)?.Id != fixtureA.Id)
                throw new IOException("Changing Custom selection reselected the first item unexpectedly.");
            if (Descendants(performancePopup!).OfType<Expander>().Count() != CategoryNames.Length || Descendants(performancePopup!).OfType<Expander>().Any(e => e.IsExpanded))
                throw new IOException("Custom categories did not start compact.");
            await CaptureAsync(Path.Combine(output, "Performance-Custom-compact.png"));
            ClosePerformancePopup();
            performanceStore.Plans.Remove(fixtureA);
            performanceStore.Plans.Remove(fixtureB);
            performanceDraft.Clear();
            checks.Add(new
            {
                route = "Performance",
                savedPresetSelectionByIdentity = true,
                compactCategories = true
            });
            ShowPage("Storage");
            var selectionFixture = PathPicker("Selected fixture", out var selectionField, true);
            page.Children.Clear();
            page.Children.Add(selectionFixture);
            await Task.Delay(150);
            root.UpdateLayout();
            selectionField.Text = Path.Combine(output, "image-fixture.png");
            if (!Descendants(selectionFixture).OfType<TextBlock>().Any(t => t.Text == "image-fixture.png"))
                throw new IOException("Selected filename did not update synchronously.");
            await CaptureAsync(Path.Combine(output, "Picker-selected-fixture.png"));
            selectionField.Text = "";
            if (!Descendants(selectionFixture).OfType<TextBlock>().Any(t => t.Text == "Browse or drag here"))
                throw new IOException("Cleared file prompt did not reset.");
            var folderFixture = PathPicker("Selected folder", out var folderField);
            page.Children.Add(folderFixture);
            await Task.Delay(150);
            root.UpdateLayout();
            folderField.Text = Path.Combine(output, "Folder fixture");
            if (!Descendants(folderFixture).OfType<TextBlock>().Any(t => t.Text == "Folder fixture"))
                throw new IOException("Selected folder did not update synchronously.");
            checks.Add(new
            {
                route = "Pickers",
                selectedFilenameImmediate = true,
                selectedFolderImmediate = true,
                clearResetsPrompt = true
            });
            ShowPage("Performance");
            if (performanceCategories.Last().Key != "Advanced" || performanceCategories.Any(c => c.Value.MinHeight >= 130))
                throw new IOException("Performance categories are not compact with Advanced last.");
            checks.Add(new
            {
                route = "Performance",
                compactCategoryCards = true,
                advancedLast = true
            });
            ShowPage("System Clean");
            var gameWidget = Descendants(page).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().First(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == "Game junk");
            gameWidget.IsChecked = true;
            await CaptureAsync(Path.Combine(output, "System-Clean-game-discovery.png"));
            if (!Descendants(page).OfType<Expander>().Any(e => e.Header?.ToString() == "Add a game folder"))
                throw new IOException("Manual fallback for game discovery missing.");
            checks.Add(new
            {
                route = "System Clean",
                optionalGameDiscovery = true,
                manualFolderFallback = true
            });
            ShowPage("Organize");
            await Task.Delay(150);
            if (!Descendants(page).OfType<ComboBox>().Any(c => c.Header?.ToString() == "File type" && c.Items.Contains("Videos")))
                throw new IOException("Sorting presets missing.");
            checks.Add(new
            {
                route = "Organize",
                sortingPresets = true
            });
            ShowPage("Organize");
            await Task.Delay(150);
            var sortingChips = Descendants(page).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().ToArray();
            var picturesChip = sortingChips.First(c => c.Tag?.ToString() == "Pictures");
            var codeChip = sortingChips.First(c => c.Tag?.ToString() == "Code");
            if (picturesChip.IsChecked != true || codeChip.IsChecked == true)
                throw new IOException("Sorting selected states are ambiguous.");
            int ruleCount = Descendants(page).OfType<ComboBox>().Count(c => c.Header?.ToString() == "File type");
            codeChip.IsChecked = true;
            if (Descendants(page).OfType<ComboBox>().Count(c => c.Header?.ToString() == "File type") != ruleCount + 1)
                throw new IOException("Code preset did not add its rule.");
            await CaptureAsync(Path.Combine(output, "Organize-selected-presets.png"));
            codeChip.IsChecked = false;
            if (Descendants(page).OfType<ComboBox>().Count(c => c.Header?.ToString() == "File type") != ruleCount || Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Preview moves"))
                throw new IOException("Sorting deselection or merged Move flow failed.");
            checks.Add(new
            {
                route = "Organize",
                selectedPresetsVisible = true,
                codeToggleAddsRemovesRule = true,
                automaticReviewInMove = true
            });
            ShowPage("File Actions");
            await Task.Delay(150);
            string automaticFile = Path.Combine(output, "auto-file-fixture.txt");
            await File.WriteAllTextAsync(automaticFile, "automatic details");
            var fileField = Descendants(page).OfType<TextBox>().First(c => c.Header?.ToString() == "File to manage");
            fileField.Text = automaticFile;
            await Task.Delay(500);
            root.UpdateLayout();
            if (Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Inspect file") || !Descendants(page).OfType<TextBlock>().Any(t => t.Text.Contains("Modified ")))
                throw new IOException("Automatic file details missing: " + string.Join(" | ", Descendants(page).OfType<TextBlock>().Select(t => t.Text)));
            if (!Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Locking & stuck apps") || Descendants(page).OfType<Expander>().Any(e => e.Header?.ToString() == "Locking and stuck apps"))
                throw new IOException("Lock tools are still hidden.");
            await CaptureAsync(Path.Combine(output, "File-Actions-auto-details.png"));
            checks.Add(new
            {
                route = "File Actions",
                automaticDetails = true,
                noInspectButton = true
            });
            ShowPage("Compress");
            await Task.Delay(150);
            if (Descendants(page).OfType<Expander>().Any(e => e.Header?.ToString() == "Specific save folder (optional)") || !Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Change") || Descendants(page).OfType<ListView>().Any(v => v.SelectionMode != ListViewSelectionMode.None))
                throw new IOException("Compression still requires duplicate queue selection or a large destination.");
            checks.Add(new
            {
                route = "Compress",
                compactOptionalDestination = true,
                noSecondSelection = true
            });
            var compressionInput = Descendants(page).OfType<TextBox>().First(c => c.Header?.ToString() == "Files to compress");
            compressionInput.Text = Directory.EnumerateFiles(output, "*.png").First();
            await Task.Delay(150);
            root.UpdateLayout();
            if (compressionQueue.Count != 1)
                throw new IOException("File selection did not queue automatically.");
            var saveField = Descendants(page).OfType<TextBox>().First(c => c.Header?.ToString() == "Save folder");
            saveField.Text = output;
            if (compressionQueue.Single().Destination != output)
                throw new IOException("Compact destination did not update the queue.");
            var qualityField = Descendants(page).OfType<NumberBox>().First(n => n.Value == 80);
            qualityField.Value = 65;
            if (compressionQueue.Single().Options.Quality != 65)
                throw new IOException("Compression still requires applying settings to selected items.");
            await CaptureAsync(Path.Combine(output, "Compress-auto-queue.png"));
            compressionQueue.Clear();
            compressionDirectory = null;
            checks.Add(new
            {
                route = "Compress",
                automaticQueue = true,
                destinationUpdatesImmediately = true,
                settingsUpdateWithoutSecondSelection = true
            });
            ShowPage("Performance");
            ShowPerformanceCategory(1);
            await Task.Delay(150);
            if (Descendants(performancePopup!).OfType<CheckBox>().Any())
                throw new IOException("Manual FPS guidance still uses checkboxes.");
            ClosePerformancePopup();
            ShowPerformanceCategory(6);
            await Task.Delay(150);
            if (Descendants(performancePopup!).OfType<CheckBox>().Any() || !Descendants(performancePopup!).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Use performance appearance"))
                throw new IOException("Appearance editor remains verbose.");
            ClosePerformancePopup();
            var startupAction = new PlanAction(PlanActionKind.StartupRun, "fixture", "Disabled", "Startup fixture");
            if (DescribeSetting(startupAction, "{\"Value\":\"private path\",\"Kind\":1}") != "Registered at sign-in")
                throw new IOException("Startup review exposes raw JSON.");
            checks.Add(new
            {
                route = "Performance",
                manualGuidanceWithoutCheckIns = true,
                compactAppearance = true,
                humanReadableStartupReview = true
            });
            ShowPage("Diagnostics");
            await Task.Delay(150);
            var enableSensor = Descendants(page).OfType<Button>().First(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Enable advanced sensors");
            var cpuHeading = Descendants(page).OfType<TextBlock>().First(t => t.Text == "CPU");
            if (enableSensor.TransformToVisual(root).TransformPoint(new(0, 0)).Y >= cpuHeading.TransformToVisual(root).TransformPoint(new(0, 0)).Y)
                throw new IOException("Sensor controls are below hardware sections.");
            checks.Add(new
            {
                route = "Diagnostics",
                sensorControlsFirst = true
            });
            var healthFixture = new MonitorSample(DateTime.UtcNow, 95, 8L * 1073741824, 32L * 1073741824, 15, 0, 0, 0, 0, [], new([new("Fixture CPU", "CPU Package", "Cpu", 88, "C"), new("Fixture GPU", "GPU Core", "GpuNvidia", 42, "C")], null), null);
            renderDiagnostics?.Invoke(healthFixture);
            await CaptureAsync(Path.Combine(output, "Diagnostics-health-fixture.png"));
            if (!Descendants(page).OfType<TextBlock>().Any(t => t.Text == "❄") || !Descendants(page).OfType<TextBlock>().Any(t => t.Text == "▲"))
                throw new IOException("Cool/high metric icons missing.");
            checks.Add(new
            {
                route = "Diagnostics",
                coolAndHighIconsRendered = true,
                usesFixtureReadings = true
            });
            var viewSelector = Descendants(page).OfType<ComboBox>().First(c => c.Header?.ToString() == "View");
            for (int view = 0; view < viewSelector.Items.Count; view++)
            {
                viewSelector.SelectedIndex = view;
                await Task.Delay(250);
                await CaptureAsync(Path.Combine(output, $"Diagnostics-view-{view}-minimum.png"));
                checks.Add(new
                {
                    route = "Diagnostics",
                    view,
                    readOnlyView = true
                });
            }
            foreach (var size in new[] { new SizeInt32(800, 600), new SizeInt32(1100, 760), new SizeInt32(1600, 1000) })
            {
                AppWindow.Resize(size);
                ShowPage("Performance");
                await Task.Delay(200);
                for (int category = 0; category < CategoryNames.Length; category++)
                {
                    ShowPerformanceCategory(category, performanceCategories[CategoryNames[category]]);
                    await Task.Delay(250);
                    root.UpdateLayout();
                    if (performancePopup is null || performancePopup.ActualWidth > 901 || performancePopup.ActualWidth > root.ActualWidth - 47)
                        throw new IOException("Popup width exceeds its available space.");
                    if (navigation.Values.Any(n => n.IsEnabled))
                        throw new IOException("Modal navigation remained enabled.");
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
                    if (!Within(focused, performancePopup))
                        throw new IOException("Popup did not receive keyboard focus.");
                    await CaptureAsync(Path.Combine(output, $"Performance-popup-{category}-{size.Width}.png"));
                    ClosePerformancePopup();
                    if (performancePopup is not null)
                        throw new IOException("Popup did not dismiss.");
                    checks.Add(new
                    {
                        route = "Performance",
                        category,
                        popup = true,
                        width = size.Width
                    });
                }
            }
            ShowPage("Performance");
            var beforeChanges = performanceStore.Changes.Count;
            ShowPerformanceCategory(0, performanceCategories[CategoryNames[0]]);
            await Task.Delay(150);
            var scheme = Descendants(performancePopup!).OfType<ComboBox>().First();
            if (scheme.Items.Count > 0)
            {
                scheme.SelectedIndex = 0;
                var selected = Descendants(performancePopup!).OfType<CheckBox>().First();
                selected.IsChecked = true;
                if (performanceDraft.Actions.Count != 1)
                    throw new IOException("Checkbox did not stage the selected power change.");
                var staged = performanceDraft.Snapshot();
                ClosePerformancePopup();
                ShowPage("Home");
                ShowPage("Performance");
                if (!performanceDraft.Actions.SequenceEqual(staged.Actions))
                    throw new IOException("Navigation discarded the Custom draft.");
                if (!Descendants(page).OfType<Button>().Any(b => b.IsEnabled && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Review & apply"))
                    throw new IOException("Review did not reflect selections after closing the popup.");
                if (performanceStore.Changes.Count != beforeChanges)
                    throw new IOException("Selection created an applied-change journal.");
                checks.Add(new
                {
                    route = "Performance",
                    draftRetainedAcrossNavigation = true,
                    selectionDoesNotApply = true
                });
            }
            ClosePerformancePopup();
            performanceDraft.Clear();
            foreach (var route in VerificationRoutes)
            {
                ShowPage(route);
                page.Width = 500;
                root.UpdateLayout();
                await Task.Delay(150);
                CheckNavigationCardSizing(route);
                await CaptureAsync(Path.Combine(output, route.Replace(' ', '-') + "-narrow-content.png"));
                checks.Add(new
                {
                    route,
                    constrainedContentWidth = 500
                });
            }
            var pageScroll = Descendants(root).OfType<ScrollViewer>().First(s => ReferenceEquals(s.Content, page));
            page.Width = pageScroll.ActualWidth - pageScroll.Padding.Left - pageScroll.Padding.Right;
            ShowPage("Home");
            if (navigation.ContainsKey("Compare") || Descendants(page).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Compare"))
                throw new IOException("Removed Compare tool is still visible.");
            await Task.Delay(150);
            root.UpdateLayout();
            var flameMark = Descendants(page).OfType<Grid>().First(g => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(g) == "Apex flame");
            if (flameMark.IsHitTestVisible || Descendants(flameMark).OfType<Image>().Count() != 1)
                throw new IOException("Flame still has interactive effects.");
            await CaptureAsync(Path.Combine(output, "Home-static-flame.png"));
            checks.Add(new
            {
                route = "Home",
                compareRemoved = true,
                staticBitmapFlame = true,
                noHoverEffect = true
            });
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
                foreach (var route in VerificationRoutes)
                {
                    ShowPage(route);
                    await Task.Delay(300);
                    root.UpdateLayout();
                    CheckNavigationCardSizing(route);
                    CheckLeftAlignment(Descendants(root).OfType<ScrollViewer>().First(s => ReferenceEquals(s.Content, page)));
                    await CaptureAsync(Path.Combine(output, route.Replace(' ', '-') + "-maximized.png"));
                    checks.Add(new
                    {
                        route,
                        maximized = true
                    });
                }
                presenter.Restore();
            }
            AppWindow.Resize(new SizeInt32(1100, 760));
            ShowPage("Home");
            ShowPage("Compress");
            ShowTaskNotice("Completed", "Compressed copy saved", NoticeKind.Success, false);
            await Task.Delay(300);
            await CaptureAsync(Path.Combine(output, "Compress-completion.png"));
            await Task.Delay(3300);
            if (taskNotice is not null)
                throw new IOException("Completion notification did not fade away.");
            checks.Add(new
            {
                route = "Notifications",
                automaticFade = true
            });
            ShowPage("Performance");
            performanceDraft.Load(new(Guid.NewGuid(), "Review fixture", [new(PlanActionKind.Manual, "GameMode", "Manual; unverified", "Review Game Mode")], ""));
            ReviewPerformance();
            await Task.Delay(200);
            if (performancePopup is null || Descendants(performancePopup).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Done") || !Descendants(performancePopup).OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Confirm & apply"))
                throw new IOException("Review footer did not replace Done with Confirm & apply.");
            await CaptureAsync(Path.Combine(output, "Performance-review-footer.png"));
            ShowTaskNotice("Could not complete", "Fixture error above the review", NoticeKind.Error, false);
            await Task.Delay(100);
            if (taskNotice is null || Canvas.GetZIndex(taskNotice) <= Canvas.GetZIndex(performanceOverlay!))
                throw new IOException("Error notification is behind the review.");
            await CaptureAsync(Path.Combine(output, "Performance-review-error-visible.png"));
            DismissTaskNotice();
            ClosePerformancePopup();
            performanceDraft.Clear();
            checks.Add(new
            {
                route = "Performance",
                singleReviewFooter = true
            });
            ShowPage("Home");
            bool originalSolid = preferences.SolidBackground;
            preferences.SolidBackground = true;
            ApplyBackdrop();
            await Task.Delay(300);
            if (SystemBackdrop is not null)
                throw new InvalidOperationException("Opaque fallback still uses Acrylic.");
            await CaptureAsync(Path.Combine(output, "Home-solid-background.png"));
            ShowPage("Performance");
            ShowPerformanceCategory(0, performanceCategories[CategoryNames[0]]);
            await Task.Delay(150);
            if (performancePopup?.Background is not SolidColorBrush)
                throw new IOException("Popup opaque fallback missing.");
            await CaptureAsync(Path.Combine(output, "Performance-popup-solid-background.png"));
            ClosePerformancePopup();
            preferences.SolidBackground = originalSolid;
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                functionalChecks,
                opaqueFallbackVerified = true,
                checks
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                passed = false,
                error = error.ToString(),
                checks
            }));
        }
        finally { Close(); }
    }
    private static async Task InvokeNavigation(Button button)
    {
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
        if (peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
            throw new IOException("Navigation button has no accessible invoke action.");
        invoke.Invoke();
        await Task.Delay(150);
    }

    private void CheckNavigationCardSizing(string route)
    {
        if (route != "Home" && !IsWorkspace(route))
            return;
        var cards = route == "Home"
            ? page.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>()).Where(b => b.Tag?.ToString() is "System" or "Performance" or "Media").ToArray()
            : page.Children.OfType<Grid>().Single(g => g.Tag?.ToString() == "Workspace cards").Children.OfType<Button>().ToArray();
        double expected = route == "Home" ? 160 : 144;
        if (cards.Length == 0 || cards.Any(b => b.ActualHeight < expected - 1 || b.ActualHeight > expected + 1))
            throw new IOException($"{route} navigation cards must stay near {expected}px tall as the window resizes.");
        if (cards.Any(b => Grid.GetColumnSpan(b) != 1))
            throw new IOException("Navigation cards should keep equal column widths.");
    }

    private void CheckLeftAlignment(ScrollViewer scroll)
    {
        var origin = new Windows.Foundation.Point(0, 0);
        var expected = scroll.TransformToVisual(root).TransformPoint(origin).X + scroll.Padding.Left;
        var actual = page.TransformToVisual(root).TransformPoint(origin).X;
        if (Math.Abs(actual - expected) > 1)
            throw new InvalidOperationException($"Content is not left aligned: {actual} instead of {expected}.");
    }
    private async Task<RenderTargetBitmap> CaptureAsync(string path)
    {
        var rendered = new RenderTargetBitmap();
        await rendered.RenderAsync(root);
        var pixels = await rendered.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var data = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels))
            reader.ReadBytes(data);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rendered.PixelWidth, (uint)rendered.PixelHeight, 96, 96, data);
        await encoder.FlushAsync();
        stream.Seek(0);
        var png = new byte[stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(png);
        }
        File.WriteAllBytes(path, png);
        return rendered;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
