using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace Apex;

public sealed partial class MainWindow
{
    private Button HomeButton() => ActionButton("\uE80F", "Home", (_, _) => ShowPage("Home"));
    private static TextBox Field(string title, string placeholder = "") => new() { Header = title, PlaceholderText = placeholder, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Stretch };
    private UIElement PathPicker(string title, out TextBox field, bool file = false, Func<IReadOnlyList<string>, Task>? acceptFiles = null, string? fileExtension = null)
    {
        // Keep path state separate from its compact, accessible presentation.
        var box = Field(title);
        box.Visibility = Visibility.Collapsed;
        field = box;
        var content = new StackPanel { Spacing = 14 };
        var symbol = Icon(file ? "\uE8A5" : "\uE8B7", 34, "#83DDFF");
        symbol.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(symbol);
        content.Children.Add(Text(title, 16, "#EDF3FF", true));
        var name = Text("Browse or drag here", 15, "#A9BAD4");
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        content.Children.Add(name);
        var choose = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 140,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(18),
            Background = Gradient("#22FFFFFF", "#100B142A", "#188173EF"),
            BorderBrush = Gradient("#659EDFFF", "#28FFFFFF", "#428982FF"),
            BorderThickness = new Thickness(1)
        };
        Automation(choose, "Browse · " + title);
        choose.Click += async (_, _) =>
        {
            try
            {
                if (acceptFiles is not null)
                {
                    var paths = await pickers.FilesAsync();
                    if (paths.Count > 0)
                    {
                        await acceptFiles(paths);
                        box.Text = paths[^1];
                    }
                }
                else
                {
                    var selected = file ? await pickers.FileAsync(fileExtension ?? "*") : await pickers.FolderAsync();
                    if (selected is not null)
                        box.Text = selected;
                }
            }
            catch (Exception e) { status.Text = "Picker unavailable: " + e.Message; ShowTaskNotice("Could not open picker", e.Message, NoticeKind.Error); }
        };
        choose.AllowDrop = true;
        choose.DragOver += (_, e) =>
        {
            e.AcceptedOperation = !working && e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems) ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            if (e.AcceptedOperation != Windows.ApplicationModel.DataTransfer.DataPackageOperation.None)
            {
                choose.BorderBrush = Brush("#FF72BBEE");
                e.DragUIOverride.Caption = file ? "Select files" : "Select folder";
            }
        };
        void ResetBorder() => choose.BorderBrush = Gradient("#659EDFFF", "#28FFFFFF", "#428982FF");
        choose.DragLeave += (_, _) => ResetBorder();
        choose.Drop += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (working)
                    return;
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = SelectionValidation.Validate(items.Select(i => i.Path), file, acceptFiles is not null, acceptFiles is not null);
                if (fileExtension is not null && paths.Any(p => !Path.GetExtension(p).Equals(fileExtension, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Choose an " + fileExtension.TrimStart('.').ToUpperInvariant() + " file.");
                if (acceptFiles is not null)
                    await acceptFiles(paths);
                box.Text = paths[^1];
            }
            catch (Exception error) { ShowTaskNotice("Selection unchanged", error.Message, NoticeKind.Error); }
            finally { ResetBorder(); deferral.Complete(); }
        };
        choose.Resources["ButtonBackgroundPointerOver"] = Gradient("#32FFFFFF", "#190B142A", "#278173EF");
        choose.Resources["ButtonBackgroundPressed"] = Brush("#3280C7FF");
        var widget = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        widget.Children.Add(choose);
        widget.Children.Add(box);
        var more = new Button
        {
            Content = Icon("\uE712", 16, "#BDCCE5"),
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = Brush("#12FFFFFF"),
            BorderThickness = new Thickness(0)
        };
        Automation(more, title + " options");
        var menu = new MenuFlyout();
        var enter = new MenuFlyoutItem { Text = "Enter path...", Icon = new FontIcon { Glyph = "\uE70F" } };
        enter.Click += (_, _) =>
        {
            var entry = Field("File or folder path");
            entry.Text = box.Text;
            var panel = new StackPanel { Width = 280, Spacing = 12 };
            panel.Children.Add(entry);
            var flyout = new Flyout { Content = panel };
            panel.Children.Add(ActionButton("\uE73E", "Use path", (_, _) => { box.Text = entry.Text.Trim().Trim('"'); flyout.Hide(); }));
            flyout.ShowAt(more);
        };
        menu.Items.Add(enter);
        var clear = new MenuFlyoutItem { Text = "Clear selection", Icon = new FontIcon { Glyph = "\uE711" } };
        clear.Click += (_, _) => box.Text = "";
        menu.Items.Add(clear);
        more.Flyout = menu;
        widget.Children.Add(more);
        box.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) =>
        {
            string path = box.Text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            name.Text = box.Text.Length == 0 ? "Browse or drag here" : Path.GetFileName(path) is { Length: > 0 } leaf ? leaf : box.Text;
            name.FontSize = box.Text.Length == 0 ? 14 : 16;
            name.Foreground = Brush(box.Text.Length == 0 ? "#A9BAD4" : "#83DDFF");
            symbol.Glyph = box.Text.Length == 0 ? (file ? "\uE8A5" : "\uE8B7") : "\uE73E";
            ToolTipService.SetToolTip(choose, box.Text.Length == 0 ? "Browse · " + title : box.Text);
            Automation(choose, "Browse · " + title + (box.Text.Length == 0 ? "" : " · " + box.Text));
        });
        return widget;
    }

    private Grid SelectionPair(UIElement left, UIElement right)
    {
        var pair = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        pair.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        pair.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        foreach (var item in new[] { left, right }.OfType<FrameworkElement>())
        {
            item.Width = double.NaN;
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        pair.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        pair.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        pair.Children.Add(left);
        Grid.SetColumn((FrameworkElement)right, 1);
        pair.Children.Add(right);
        pair.SizeChanged += (_, _) =>
        {
            bool narrow = pair.ActualWidth < 620;
            pair.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn((FrameworkElement)right, narrow ? 0 : 1);
            Grid.SetRow((FrameworkElement)right, narrow ? 1 : 0);
            pair.RowSpacing = narrow ? Ui.Gap : 0;
        };
        return pair;
    }

    private UIElement Work(string label, Func<CancellationToken, IProgress<string>, Task> action, bool mutation = false, bool allowNavigation = false)
    {
        var group = new StackPanel { Spacing = 10 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var note = Text("", 13, "#A9BAD4");
        note.Visibility = Visibility.Collapsed;
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var cancel = new Button { Content = "Cancel", IsEnabled = false, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(10) };
        Button run = null!;
        run = ActionButton("\uE768", label, async (_, _) =>
        {
            if (working)
                return;
            working = true;
            allowWorkingNavigation = allowNavigation;
            stopRecording.Visibility = allowNavigation ? Visibility.Visible : Visibility.Collapsed;
            taskCompletion = null;
            DismissTaskNotice();
            using var source = new CancellationTokenSource();
            simulation = source;
            run!.IsEnabled = false;
            cancel.IsEnabled = true;
            cancel.Visibility = Visibility.Visible;
            progress.Visibility = Visibility.Visible;
            note.Visibility = Visibility.Visible;
            note.Text = "Working…";
            if (!allowNavigation)
                foreach (var nav in navigation.Values)
                    nav.IsEnabled = false;
            var workRoot = (DependencyObject?)performancePopup ?? page;
            var inputs = Descendants(workRoot).OfType<Control>().Where(c => c is TextBox or ComboBox or CheckBox or NumberBox or Slider or Microsoft.UI.Xaml.Controls.Primitives.ToggleButton).Select(c => (Control: c, WasEnabled: c.IsEnabled)).ToArray();
            foreach (var input in inputs)
                input.Control.IsEnabled = false;
            var otherButtons = Descendants(workRoot).OfType<Button>().Where(b => b != run && b != cancel && (!allowNavigation || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) != "Home")).Select(b => (Button: b, WasEnabled: b.IsEnabled)).ToArray();
            foreach (var other in otherButtons)
                other.Button.IsEnabled = false;
            try
            {
                await action(source.Token, new Progress<string>(text => { if (simulation == source && taskCompletion is null) { note.Text = text; status.Text = text; } }));
                if (taskCompletion is { } final)
                    status.Text = final.Message;
                note.Text = status.Text;
                if (taskCompletion is { } completed)
                    ShowTaskNotice(completed.Kind switch
                    {
                        NoticeKind.Success => "Completed",
                        NoticeKind.Warning => "Review remaining items",
                        NoticeKind.Error => "Could not complete",
                        _ => "Canceled"
                    }, completed.Message, completed.Kind);
            }
            catch (OperationCanceledException) { note.Text = status.Text = mutation ? "Canceled · completed operations are kept" : "Canceled"; ShowTaskNotice("Canceled", note.Text, NoticeKind.Information); }
            catch (Exception e) { note.Text = status.Text = "Could not complete: " + e.Message; ShowTaskNotice("Could not complete", e.Message, NoticeKind.Error); }
            finally
            {
                working = false;
                allowWorkingNavigation = false;
                stopRecording.Visibility = Visibility.Collapsed;
                run.IsEnabled = true;
                cancel.IsEnabled = false;
                cancel.Visibility = Visibility.Collapsed;
                progress.Visibility = Visibility.Collapsed;
                note.Visibility = Visibility.Collapsed;
                if (simulation == source)
                    simulation = null;
                foreach (var nav in navigation.Values)
                    nav.IsEnabled = performancePopup is null;
                foreach (var input in inputs)
                    input.Control.IsEnabled = input.WasEnabled;
                foreach (var other in otherButtons)
                    other.Button.IsEnabled = other.WasEnabled;
            }
        });
        run.Background = Brush("#83DDFF");
        run.Foreground = Brush("#101B33");
        run.Resources["ButtonBackgroundPointerOver"] = Brush("#B0E9FF");
        run.Resources["ButtonBackgroundPressed"] = Brush("#A09FFF");
        run.Resources["ButtonForegroundPointerOver"] = Brush("#101B33");
        run.Resources["ButtonForegroundPressed"] = Brush("#101B33");
        if (run.Content is StackPanel runContent)
            foreach (var child in runContent.Children)
            {
                if (child is TextBlock labelText)
                    labelText.Foreground = Brush("#101B33");
                if (child is FontIcon labelIcon)
                    labelIcon.Foreground = Brush("#101B33");
            }
        bool primary = new[] { "Clean", "Scan", "Move files", "Compress", "Convert to MP3", "Prepare for gaming", "Confirm & apply", "Move to Recycle Bin", "Scan network usage" }.Contains(label);
        if (primary)
        {
            run.MinHeight = 48;
            run.Padding = new Thickness(20, 12, 20, 12);
            run.BorderBrush = Brush("#D1A4E9FF");
            run.BorderThickness = new Thickness(1);
        }
        else
        {
            run.Resources["ButtonBackgroundPointerOver"] = Brush("#A0304D6A");
            run.Resources["ButtonForegroundPointerOver"] = Brush("#EDF3FF");
            run.Background = Brush("#7020304D");
            run.Foreground = Brush("#DEEAFF");
            if (run.Content is StackPanel normal)
                foreach (var child in normal.Children)
                {
                    if (child is TextBlock text)
                        text.Foreground = Brush("#DEEAFF");
                    if (child is FontIcon icon)
                        icon.Foreground = Brush("#DEEAFF");
                }
        }
        cancel.Click += (_, _) => simulation?.Cancel();
        row.Children.Add(run);
        row.Children.Add(cancel);
        group.Children.Add(row);
        group.Children.Add(progress);
        group.Children.Add(note);
        return group;
    }
    private async Task<bool> Confirm(string title, string message, string action)
    {
        var dialog = new ContentDialog { Title = title, Content = new ScrollViewer { Content = Text(message, 14, "#E3ECFF"), MaxHeight = 360 }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark };
        bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        if (!confirmed)
            ShowTaskNotice("Canceled", "No action taken", NoticeKind.Information);
        return confirmed;
    }
    private static StackPanel Section(string title)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Text(title, Ui.SectionTitle, "#EDF3FF", true));
        return panel;
    }
    private static void Results(StackPanel panel, IEnumerable<(string Title, string Detail, string Value)> rows, string empty)
    {
        panel.Children.Clear();
        var list = rows.ToList();
        if (list.Count == 0)
        {
            panel.Children.Add(Text(empty, 14, "#9CACC7"));
            return;
        }
        panel.Children.Add(Text($"{list.Count:N0} results" + (list.Count > 1000 ? " · first 1,000 shown; narrow the filter" : ""), 12, "#9CACC7"));
        var view = new ListView { MaxHeight = 340, SelectionMode = ListViewSelectionMode.None };
        foreach (var item in list.Take(1000))
        {
            var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 8) };
            row.Children.Add(Text(item.Title, 14, "#EDF3FF", true));
            row.Children.Add(Text(item.Detail, 12, "#9CACC7"));
            row.Children.Add(Text(item.Value, 13, "#83DDFF", true));
            view.Items.Add(row);
        }
        panel.Children.Add(view);
    }
    private static void Issues(StackPanel panel, IEnumerable<string> issues)
    {
        panel.Children.Clear();
        var all = issues.ToList();
        if (all.Count == 0)
            return;
        var details = new StackPanel { Spacing = 6 };
        foreach (var issue in all.Take(100))
            details.Children.Add(Text(issue, 12, "#B4C8EB"));
        if (all.Count > 100)
            details.Children.Add(Text("Additional issues: " + (all.Count - 100), 12, "#B4C8EB"));
        panel.Children.Add(new Expander { Header = $"{all.Count:N0} skipped items / errors", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch });
    }
    private void Storage()
    {
        page.Children.Add(PathPicker("Directory to inspect", out var source));
        ScanResult? scan = null;
        var summary = Text("Choose a directory to see its size and files.", 15, "#A9BAD4");
        page.Children.Add(summary);
        var search = Field("Find files", "Name, extension, or relative path");
        var sort = new ComboBox { Header = "Sort", Items = { "Largest first", "Smallest first", "Newest first", "Name" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var controls = new StackPanel { Spacing = 12 };
        controls.Children.Add(search);
        controls.Children.Add(sort);
        var folders = new StackPanel { Spacing = 10 };
        var files = new StackPanel { Spacing = 10 };
        var errors = new StackPanel();
        FrameworkElement? folderCard = null, fileCard = null, filterCard = null;
        void Render()
        {
            foreach (var card in new[] { folderCard, fileCard, filterCard })
                if (card is not null)
                    card.Visibility = scan is null ? Visibility.Collapsed : Visibility.Visible;
            summary.Visibility = scan is null ? Visibility.Collapsed : Visibility.Visible;
            if (scan is null)
                return;
            summary.Text = $"{(scan.Issues.Count > 0 ? "At least " : "")}{FileToolkit.Size(scan.Files.Sum(f => f.Size))} readable logical size · {scan.Files.Count:N0} files · {scan.Issues.Count:N0} inaccessible items · {scan.SkippedLinks:N0} links skipped";
            var sizes = scan.Files.GroupBy(f => f.RelativePath.Contains(Path.DirectorySeparatorChar) ? f.RelativePath.Split(Path.DirectorySeparatorChar)[0] : "").ToDictionary(g => g.Key, g => (Count: g.Count(), Size: g.Sum(f => f.Size)), StringComparer.OrdinalIgnoreCase);
            foreach (var child in scan.Directories.Where(d => string.Equals(Path.GetDirectoryName(d), scan.Root, StringComparison.OrdinalIgnoreCase)))
                sizes.TryAdd(Path.GetFileName(child), (0, 0));
            var groups = sizes.Select(g => (Name: g.Key.Length == 0 ? "Files in this directory" : g.Key, g.Value.Count, g.Value.Size)).OrderByDescending(g => g.Size);
            Results(folders, groups.Select(g => (g.Name, $"{g.Count:N0} readable files", (scan.Issues.Count > 0 ? "At least " : "") + FileToolkit.Size(g.Size))), "Directory contains no readable files.");
            var filtered = scan.Files.Where(f => f.RelativePath.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            var sorted = sort.SelectedIndex switch
            {
                1 => filtered.OrderBy(f => f.Size),
                2 => filtered.OrderByDescending(f => f.ModifiedUtc),
                3 => filtered.OrderBy(f => f.RelativePath),
                _ => filtered.OrderByDescending(f => f.Size)
            };
            Results(files, sorted.Select(f => (f.RelativePath, $"Modified {f.ModifiedUtc.ToLocalTime():g} · {f.Path}", FileToolkit.Size(f.Size))), "No matching files.");
            Issues(errors, scan.Issues);
        }
        source.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => { scan = null; summary.Text = "Choose Scan to inspect this directory."; Render(); });
        search.TextChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        page.Children.Add(Work("Scan directory", async (token, progress) => { scan = null; Render(); scan = await FileToolkit.ScanAsync(source.Text, true, progress, token); Render(); FinishTask($"Scan complete · {scan.Files.Count:N0} files", scan.Issues.Count); }));
        filterCard = Glass(controls);
        page.Children.Add(filterCard);
        var folderSection = Section("Directory sizes");
        folderSection.Children.Add(folders);
        folderCard = Glass(folderSection);
        page.Children.Add(folderCard);
        var fileSection = Section("Files");
        fileSection.Children.Add(files);
        fileCard = Glass(fileSection);
        page.Children.Add(fileCard);
        page.Children.Add(errors);
        Render();
    }
    private void Organize()
    {
        page.Children.Add(PathPicker("Source folder", out var source));
        var recursive = new CheckBox { Content = "Include subdirectories", IsChecked = preferences.IncludeSubdirectories };
        page.Children.Add(recursive);
        var groups = SortingPresets.Items.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var saved in preferences.ExtensionGroups)
            if (!groups.ContainsKey(saved.Key))
                groups[saved.Key] = saved.Value;
        var rules = new StackPanel { Spacing = 12 };
        var fields = new List<(TextBox Extensions, TextBox Destination)>();
        var chips = new Dictionary<string, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>();
        bool updating = false;
        var results = new StackPanel { Spacing = 10 };
        var errors = new StackPanel();
        var resultPanel = Glass(results);
        resultPanel.Visibility = Visibility.Collapsed;
        void Highlight()
        {
            updating = true;
            foreach (var chip in chips)
            {
                chip.Value.IsChecked = fields.Any(f => f.Extensions.Text == groups[chip.Key]);
                if (chip.Value.Content is StackPanel c && c.Children.FirstOrDefault() is FontIcon icon)
                    icon.Visibility = chip.Value.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                chip.Value.Background = Brush(chip.Value.IsChecked == true ? "#3567D8FF" : "#70152239");
                chip.Value.BorderBrush = Brush(chip.Value.IsChecked == true ? "#83DDFF" : "#456C92C7");
            }
            updating = false;
        }
        void AddRule(string extensions)
        {
            var panel = new StackPanel { Spacing = 8 };
            var ext = Field("Extensions", ".cs, .py");
            ext.Text = extensions;
            var type = new ComboBox { Header = "File type", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var name in groups.Keys)
                type.Items.Add(name);
            type.Items.Add("Custom");
            type.SelectedItem = groups.FirstOrDefault(p => p.Value == extensions).Key ?? "Custom";
            ext.Visibility = type.SelectedItem?.ToString() == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            var destination = CompactPathPicker("Destination folder", out var dest);
            var pair = (ext, dest);
            fields.Add(pair);
            type.SelectionChanged += (_, _) => { var name = type.SelectedItem?.ToString() ?? "Custom"; ext.Visibility = name == "Custom" ? Visibility.Visible : Visibility.Collapsed; if (groups.TryGetValue(name, out var value)) ext.Text = value; Highlight(); };
            ext.TextChanged += (_, _) => Highlight();
            var ruleRow = new Grid { ColumnSpacing = 8 };
            ruleRow.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(140)
            });
            ruleRow.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            ruleRow.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            ruleRow.Children.Add(type);
            Grid.SetColumn((FrameworkElement)destination, 1);
            ruleRow.Children.Add(destination);
            var remove = ActionButton("\uE711", "Remove rule", (_, _) => { fields.Remove(pair); rules.Children.Remove(panel); Highlight(); });
            remove.Content = Icon("\uE711", 12, "#A9BAD4");
            remove.Width = 32;
            remove.Padding = new Thickness(4);
            ToolTipService.SetToolTip(remove, "Remove rule");
            Grid.SetColumn(remove, 2);
            ruleRow.Children.Add(remove);
            panel.Children.Add(ruleRow);
            panel.Children.Add(ext);
            rules.Children.Add(panel);
            Highlight();
        }
        var presetGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        void Chips()
        {
            presetGrid.Children.Clear();
            presetGrid.ColumnDefinitions.Clear();
            presetGrid.RowDefinitions.Clear();
            chips.Clear();
            for (int i = 0; i < 3; i++)
                presetGrid.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            int index = 0;
            foreach (var group in groups)
            {
                string key = group.Key, value = group.Value;
                if (index % 3 == 0)
                    presetGrid.RowDefinitions.Add(new()
                    {
                        Height = GridLength.Auto
                    });
                var chip = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Tag = key, MinWidth = 100, MinHeight = 40, CornerRadius = new(10), HorizontalAlignment = HorizontalAlignment.Stretch };
                Automation(chip, key + " sorting preset");
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                label.Children.Add(Icon("\uE73E", 12, "#83DDFF"));
                label.Children.Add(Text(key, Ui.Body, "#EDF3FF"));
                chip.Content = label;
                chip.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}' Padding='12,9'><ContentPresenter Content='{TemplateBinding Content}'/></Border></ControlTemplate>");
                chip.Checked += (_, _) => { if (!updating && !fields.Any(f => f.Extensions.Text == value)) AddRule(value); };
                chip.Unchecked += (_, _) => { if (updating) return; for (int i = fields.Count - 1; i >= 0; i--) if (fields[i].Extensions.Text == value) { fields.RemoveAt(i); rules.Children.RemoveAt(i); } Highlight(); };
                chips[key] = chip;
                Grid.SetRow(chip, index / 3);
                Grid.SetColumn(chip, index % 3);
                presetGrid.Children.Add(chip);
                index++;
            }
            Highlight();
        }
        Chips();
        page.Children.Add(presetGrid);
        page.Children.Add(ActionButton("\uE710", "Add preset", (_, _) =>
        {
            var content = new StackPanel { Spacing = 12 };
            var name = Field("Name", "Code files");
            var extensions = Field("Extensions", ".cs, .py, .js");
            content.Children.Add(name);
            content.Children.Add(extensions);
            content.Children.Add(ActionButton("\uE74E", "Save preset", (_, _) => { try { string key = name.Text.Trim(); string value = ExtensionGroupValidation.Normalize(extensions.Text); if (key.Length is < 1 or > 40 || groups.ContainsKey(key)) throw new IOException("Use a new preset name (1–40 characters)."); preferences.ExtensionGroups[key] = value; preferences.Save(); groups[key] = value; Chips(); AddRule(value); ClosePerformancePopup(); } catch (Exception e) { ShowTaskNotice("Could not save preset", e.Message, NoticeKind.Error); } }));
            OpenPerformancePopup("New sorting preset", content);
        }));
        AddRule(groups["Pictures"]);
        AddRule(groups["Documents"]);
        var section = Section("Sorting rules");
        section.Children.Add(rules);
        page.Children.Add(Glass(section));
        UIElement? undoAction = null;
        page.Children.Add(Work("Move files", async (token, progress) =>
        {
            var scan = await FileToolkit.ScanAsync(source.Text, recursive.IsChecked == true, progress, token);
            var plans = FileToolkit.Plan(scan, fields.Select(f => new SortRule(f.Extensions.Text, f.Destination.Text)).ToArray());
            var ready = plans.Where(p => p.State == "Ready").ToArray();
            if (ready.Length == 0)
                throw new IOException("No files match valid destinations.");
            string review = $"{ready.Length} files · {plans.Count - ready.Length} skipped\n" + string.Join("\n", ready.Take(12).Select(p => p.Source.RelativePath + " → " + p.Destination));
            if (!await Confirm("Move files?", review, "Move"))
                return;
            var outcome = await toolkit.MoveAsync(ready, progress, token);
            if (undoAction is not null)
                undoAction.Visibility = toolkit.HasUndo ? Visibility.Visible : Visibility.Collapsed;
            Results(results, (outcome.Outputs ?? []).Select(m => (Path.GetFileName(m.Source), m.Destination, "Moved")), "No files moved.");
            resultPanel.Visibility = Visibility.Visible;
            Issues(errors, scan.Issues.Concat(outcome.Issues));
            FinishTask(new TaskOutcome(token.IsCancellationRequested ? TaskOutcomeKind.Canceled : outcome.Issues.Count > 0 || plans.Count > ready.Length || scan.Issues.Count > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, $"{outcome.Completed} files moved · {plans.Count - ready.Length + outcome.Issues.Count} skipped"));
        }, true));
        undoAction = Work("Undo", async (token, progress) => { if (!toolkit.HasUndo) throw new IOException("No moves to undo."); if (!await Confirm("Undo moves?", "Restore recorded files. Changed files and occupied paths are skipped.", "Undo")) return; var outcome = await toolkit.UndoAsync(progress, token); Issues(errors, outcome.Issues); FinishTask($"{outcome.Completed} restored · {outcome.Issues.Count} conflicts", outcome.Issues.Count); }, true);
        undoAction.Visibility = toolkit.HasUndo ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(undoAction);
        page.Children.Add(resultPanel);
        page.Children.Add(errors);
    }
    private void FileActions()
    {
        page.Children.Add(PathPicker("File to manage", out var path, true));
        var info = Text("", Ui.Supporting, "#A9BAD4");
        info.Visibility = Visibility.Collapsed;
        page.Children.Add(info);
        FrameworkElement? deletionPanel = null;
        Entry? selected = null;
        int selectionVersion = 0;
        Task inspection = Task.CompletedTask;
        async Task InspectSelection()
        {
            int version = ++selectionVersion;
            string input = path.Text;
            selected = null;
            info.Visibility = string.IsNullOrWhiteSpace(input) ? Visibility.Collapsed : Visibility.Visible;
            if (deletionPanel is not null)
                deletionPanel.Visibility = Visibility.Collapsed;
            info.Text = string.IsNullOrWhiteSpace(input) ? "" : "Reading file…";
            if (string.IsNullOrWhiteSpace(input))
                return;
            try
            {
                var metadata = await Task.Run(() => { var entry = FileToolkit.Inspect(input); return (entry, File.GetAttributes(entry.Path)); });
                if (version != selectionVersion || currentPage != "File Actions")
                    return;
                selected = metadata.entry;
                if (deletionPanel is not null)
                    deletionPanel.Visibility = Visibility.Visible;
                info.Text = $"{FileToolkit.Size(selected.Size)} · Modified {selected.ModifiedUtc.ToLocalTime():g} · {metadata.Item2}";
                ToolTipService.SetToolTip(info, selected.Path);
            }
            catch (Exception e) { if (version == selectionVersion) info.Text = "Unavailable: " + e.Message; }
        }
        path.RegisterPropertyChangedCallback(TextBox.TextProperty, async (_, _) => { inspection = InspectSelection(); await inspection; });
        Entry Target() => selected ?? throw new IOException("Choose a readable file first.");
        var deletion = Section("Delete file");
        deletionPanel = deletion;
        deletion.Visibility = Visibility.Collapsed;
        deletion.Children.Add(Work("Move to Recycle Bin", async (token, progress) =>
        {
            var target = Target();
            if (!await Confirm("Recycle file?", target.Path + "\n\nRestore it later from Windows Recycle Bin.", "Recycle"))
            {
                status.Text = "Canceled";
                return;
            }
            WindowsActions.Delete(target, false, hwnd);
            path.Text = "";
            FinishTask("File recycled");
        }, true));
        var advanced = new Expander { Header = "Advanced file actions", HorizontalAlignment = HorizontalAlignment.Stretch };
        var advancedPanel = new StackPanel { Spacing = 14 };
        advancedPanel.Children.Add(Work("Permanently delete", async (token, progress) =>
        {
            var target = Target();
            if (!await Confirm("Permanently delete file?", target.Path + "\n\nThis cannot be undone through the Recycle Bin.", "Delete permanently"))
            {
                status.Text = "Canceled";
                return;
            }
            WindowsActions.Delete(target, true, hwnd);
            path.Text = "";
            FinishTask("File deleted");
        }, true));
        advancedPanel.Children.Add(Work("Clear read-only attribute", async (token, progress) =>
        {
            var target = Target();
            if (!await Confirm("Clear read-only?", target.Path + "\n\nAllow this file to be modified.", "Clear attribute"))
            {
                status.Text = "Canceled";
                return;
            }
            WindowsActions.ClearReadOnly(target);
            await InspectSelection();
            FinishTask("Read-only attribute cleared");
        }, true));
        var recoveryAction = new ComboBox { Header = "Administrator retry", Items = { "Recycle this file", "Permanently delete this file", "Clear read-only attribute" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        advancedPanel.Children.Add(recoveryAction);
        advancedPanel.Children.Add(Work("Retry selected action as administrator", async (token, progress) =>
        {
            var target = Target();
            FileToolkit.ValidateMutation(target.Path);
            FileToolkit.Recheck(target);
            int mode = recoveryAction.SelectedIndex;
            if (!await Confirm("Administrator retry?", target.Path + "\n\nAction: " + recoveryAction.SelectedItem + "\n\nWindows will ask for administrator permission. The helper can act only on this file." + (mode == 1 ? " Permanent deletion cannot be undone." : ""), "Continue"))
            {
                status.Text = "Canceled";
                return;
            }
            var request = new
            {
                File = target,
                Action = mode switch
                {
                    1 => "Permanent",
                    2 => "ReadOnly",
                    _ => "Recycle"
                }
            };
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request)));
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Apex.Recovery.exe")) { UseShellExecute = true, Verb = "runas", Arguments = "--request " + encoded, WindowStyle = ProcessWindowStyle.Hidden };
            try
            {
                using var process = Process.Start(start) ?? throw new IOException("Could not start the recovery helper.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    throw new IOException("Administrator action failed: " + new System.ComponentModel.Win32Exception(process.ExitCode).Message);
                if (mode == 2)
                    await InspectSelection();
                else
                    path.Text = "";
                FinishTask("Administrator action completed");
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { status.Text = "Administrator permission canceled · no action taken"; ShowTaskNotice("Canceled", status.Text, NoticeKind.Information); }
        }, true));
        advanced.Content = advancedPanel;
        deletion.Children.Add(advanced);
        var deletionCard = Glass(deletion);
        deletionPanel = deletionCard;
        deletion.Visibility = Visibility.Visible;
        deletionCard.Visibility = Visibility.Collapsed;
        page.Children.Add(deletionCard);
        var apps = new StackPanel { Spacing = 10 };
        var appMode = new ComboBox { Header = "Show", Items = { "Apps locking this file", "Running apps" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        apps.Children.Add(appMode);
        var tasks = new ComboBox { Header = "Application", PlaceholderText = "Loading apps…", HorizontalAlignment = HorizontalAlignment.Stretch };
        apps.Children.Add(tasks);
        var taskInfo = Text("", Ui.Supporting, "#A9BAD4");
        apps.Children.Add(taskInfo);
        int appRevision = 0;
        void SetTasks(List<AppTask> list)
        {
            tasks.ItemsSource = list;
            tasks.SelectedIndex = -1;
            tasks.PlaceholderText = list.Count == 0 ? "No apps found" : "Select an app";
            taskInfo.Text = list.Count == 0 ? "No apps reported. Some locks cannot be detected." : $"{list.Count} apps";
        }
        async Task RefreshTasks(CancellationToken token)
        {
            int version = ++appRevision;
            bool locking = appMode.SelectedIndex == 0;
            Entry? file = selected;
            if (locking && file is null)
            {
                SetTasks([]);
                taskInfo.Text = "Choose a file first.";
                return;
            }
            var list = await Task.Run(() => locking ? WindowsActions.Locks(file!) : WindowsActions.Tasks(), token);
            if (version == appRevision)
                SetTasks(list);
        }
        async Task RefreshQuietly()
        {
            try
            {
                await RefreshTasks(CancellationToken.None);
            }
            catch (Exception e) { taskInfo.Text = e.Message; }
        }
        var appPanel = Section("Locking & stuck apps");
        var appHeading = new Grid();
        var appTitle = appPanel.Children[0];
        appPanel.Children.RemoveAt(0);
        appHeading.Children.Add(appTitle);
        appPanel.Children.Insert(0, appHeading);
        appPanel.Children.Add(Text("Find what is holding a file open, or close an unresponsive app.", Ui.Supporting, "#A9BAD4"));
        appPanel.Children.Add(apps);
        var appCard = Glass(appPanel);
        appMode.SelectedIndex = 1;
        appCard.Loaded += async (_, _) => { if (!verificationMode) await RefreshQuietly(); };
        appMode.SelectionChanged += async (_, _) => await RefreshQuietly();
        path.RegisterPropertyChangedCallback(TextBox.TextProperty, async (_, _) => { await inspection; appMode.SelectedIndex = 0; await RefreshQuietly(); });
        var refreshLocks = ActionButton("\uE72C", "Refresh apps", async (_, _) => await RefreshQuietly());
        refreshLocks.Content = Icon("\uE72C", 14, "#DEEAFF");
        refreshLocks.Width = 36;
        refreshLocks.HorizontalAlignment = HorizontalAlignment.Right;
        ToolTipService.SetToolTip(refreshLocks, "Refresh apps");
        appHeading.Children.Add(refreshLocks);
        var force = new CheckBox { Content = "Force close (unsaved work may be lost)", IsChecked = false };
        var forcePanel = new StackPanel();
        forcePanel.Children.Add(force);
        apps.Children.Add(new Expander { Header = "Advanced", Content = forcePanel, HorizontalAlignment = HorizontalAlignment.Stretch });
        apps.Children.Add(Work("Close app", async (token, progress) =>
        {
            if (tasks.SelectedItem is not AppTask app || !app.CanClose)
                throw new IOException("Select an app that can be closed.");
            bool forcibly = force.IsChecked == true;
            if (!await Confirm(forcibly ? "Force close app?" : "Close app?", app.Name + "\n" + (forcibly ? "Unsaved work may be lost." : "The app may ask you to save."), forcibly ? "Force close" : "Close"))
                return;
            token.ThrowIfCancellationRequested();
            bool closed = await WindowsActions.CloseAsync(app, forcibly);
            FinishTask(new TaskOutcome(closed ? TaskOutcomeKind.Success : TaskOutcomeKind.Partial, closed ? app.Name + " closed" : "Close requested; the app is still running."));
            await RefreshTasks(CancellationToken.None);
        }, true));
        page.Children.Add(appCard);
    }

    private void Cleanup()
    {
        var section = Section("Clean up files");
        var temporary = new CheckBox { Content = "User temporary files older than 24 hours", IsChecked = true };
        var duplicates = new CheckBox { Content = "Verified duplicates in Downloads", IsChecked = false };
        var older = new CheckBox { Content = "Older files in a chosen directory", IsChecked = false };
        section.Children.Add(temporary);
        section.Children.Add(duplicates);
        section.Children.Add(older);
        section.Children.Add(PathPicker("Directory for older files", out var directory));
        var days = new NumberBox { Header = "Older than (days)", Value = preferences.CleanupAgeDays, Minimum = 1, Maximum = 36500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        section.Children.Add(days);
        section.Children.Add(Text("Age does not mean a file is unused. Review each file before recycling. Duplicates are verified by SHA-256 and one original is kept.", 13, "#A9BAD4"));
        var choices = new StackPanel { Spacing = 10 };
        var errors = new StackPanel();
        var checkedFiles = new Dictionary<CheckBox, CleanupCandidate>();
        List<CleanupCandidate> candidates = [];
        void Clear()
        {
            candidates.Clear();
            checkedFiles.Clear();
            choices.Children.Clear();
            choices.Children.Add(Text("Scan to find cleanup candidates. Nothing is selected automatically.", 14, "#A9BAD4"));
        }
        temporary.Checked += (_, _) => Clear();
        temporary.Unchecked += (_, _) => Clear();
        duplicates.Checked += (_, _) => Clear();
        duplicates.Unchecked += (_, _) => Clear();
        older.Checked += (_, _) => Clear();
        older.Unchecked += (_, _) => Clear();
        directory.TextChanged += (_, _) => Clear();
        days.ValueChanged += (_, _) => Clear();
        section.Children.Add(Work("Scan cleanup candidates", async (token, progress) =>
        {
            bool temp = temporary.IsChecked == true, dup = duplicates.IsChecked == true, old = older.IsChecked == true;
            var rootPath = directory.Text;
            var age = days.Value;
            if (!double.IsFinite(age) || age < 1)
                throw new IOException("Enter a valid file age.");
            Clear();
            errors.Children.Clear();
            var findings = new List<CleanupCandidate>();
            var issues = new List<string>();
            if (temp)
            {
                var scan = await FileToolkit.ScanAsync(Path.GetTempPath(), true, progress, token);
                issues.AddRange(scan.Issues);
                findings.AddRange(scan.Files.Where(f => f.ModifiedUtc < DateTime.UtcNow.AddDays(-1)).Select(f => new CleanupCandidate(f, "Temporary file")));
            }
            if (dup)
            {
                var downloads = NativePickers.DownloadsPath();
                var scan = await FileToolkit.ScanAsync(downloads, true, progress, token);
                issues.AddRange(scan.Issues);
                findings.AddRange(await FileToolkit.DuplicatesAsync(scan, progress, token));
            }
            if (old)
            {
                var scan = await FileToolkit.ScanAsync(rootPath, true, progress, token);
                issues.AddRange(scan.Issues);
                findings.AddRange(scan.Files.Where(f => f.ModifiedUtc < DateTime.UtcNow.AddDays(-age)).Select(f => new CleanupCandidate(f, $"Modified over {age:0} days ago · review before removal")));
            }
            var keptOriginals = findings.Where(f => f.Keep is not null).Select(f => f.Keep!.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates = findings.Where(f => !keptOriginals.Contains(f.File.Path)).OrderByDescending(f => f.Keep is not null).DistinctBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase).OrderByDescending(f => f.File.Size).ToList();
            choices.Children.Clear();
            checkedFiles.Clear();
            choices.Children.Add(Text($"{candidates.Count:N0} candidates · {FileToolkit.Size(candidates.Sum(f => f.File.Size))}" + (candidates.Count > 1000 ? " · first 1,000 shown" : ""), 14, "#83DDFF", true));
            var view = new ListView { MaxHeight = 340, SelectionMode = ListViewSelectionMode.None };
            foreach (var item in candidates.Take(1000))
            {
                var copy = new StackPanel { Spacing = 3 };
                copy.Children.Add(Text(item.File.Path, 13, "#EDF3FF"));
                copy.Children.Add(Text(item.Reason + " · " + FileToolkit.Size(item.File.Size), 12, "#9CACC7"));
                var checkbox = new CheckBox { Content = copy, IsChecked = false };
                checkedFiles[checkbox] = item;
                view.Items.Add(checkbox);
            }
            choices.Children.Add(view);
            Issues(errors, issues);
            FinishTask($"Cleanup scan complete: {candidates.Count:N0} candidates", issues.Count);
        }));
        section.Children.Add(choices);
        section.Children.Add(Work("Recycle selected files", async (token, progress) =>
        {
            var selected = checkedFiles.Where(p => p.Key.IsChecked == true).Select(p => p.Value).ToArray();
            if (selected.Length == 0)
                throw new IOException("Select specific cleanup candidates first.");
            if (!await Confirm("Recycle selected files?", $"Recycle {selected.Length} files ({FileToolkit.Size(selected.Sum(f => f.File.Size))})?\n\n" + string.Join("\n", selected.Take(12).Select(f => f.File.Path)), "Recycle"))
            {
                status.Text = "Canceled";
                return;
            }
            var issues = new List<string>();
            int done = 0;
            foreach (var candidate in selected)
            {
                if (token.IsCancellationRequested)
                {
                    issues.Add("Cleanup canceled.");
                    break;
                }
                try
                {
                    // Re-verify the kept original before removing a duplicate.
                    if (candidate.Keep is Entry keep)
                    {
                        if (await FileToolkit.HashAsync(keep, token) != await FileToolkit.HashAsync(candidate.File, token))
                            throw new IOException("Duplicate contents changed; skipped.");
                    }
                    WindowsActions.Delete(candidate.File, false, hwnd);
                    done++;
                    progress.Report($"Recycled {done} files");
                }
                catch (OperationCanceledException) { issues.Add("Cleanup canceled."); break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { issues.Add(candidate.File.Path + ": " + e.Message); }
                await Task.Yield();
            }
            Clear();
            Issues(errors, issues);
            choices.Children.Add(Text($"Recycled {done} files. Restore them through Windows Recycle Bin.", Ui.WidgetTitle, "#83DDFF", true));
            FinishTask(new TaskOutcome(token.IsCancellationRequested ? TaskOutcomeKind.Canceled : issues.Count > 0 ? TaskOutcomeKind.Partial : TaskOutcomeKind.Success, $"Recycled {done} files; {issues.Count} skipped / errors. Restore through Windows Recycle Bin."));
        }, true));
        section.Children.Add(errors);
        page.Children.Add(Glass(section));
        Clear();
    }
}
