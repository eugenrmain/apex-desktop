using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Apex;
public sealed partial class MainWindow
{
    private readonly List<CompressionQueueItem> compressionQueue = [];
    private string? compressionDirectory;
    private Grid SliderWithValue(Slider slider, NumberBox number)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(110)
        });
        row.Children.Add(slider);
        number.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(number, 1);
        row.Children.Add(number);
        return row;
    }
    private void Compress()
    {
        var mode = new ComboBox { Header = "Settings for", Items = { "Video", "Image" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var method = new ComboBox { Header = "Compression method", Items = { "Quality", "Target size (% of original)", "Target size (MB)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var quality = new Slider { Header = "Quality (%)", Minimum = 10, Maximum = 100, Value = 80, StepFrequency = 1 };
        var qualityNumber = new NumberBox { Value = 80, Minimum = 10, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var percent = new Slider { Header = "Target size (% of original)", Minimum = 1, Maximum = 100, Value = 60, StepFrequency = 1 };
        var percentNumber = new NumberBox { Value = 60, Minimum = 1, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var limit = new NumberBox { Minimum = .1, Maximum = 10240, Value = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var mb = new Slider { Header = "Target size (MB) per file", Minimum = .1, Maximum = 10240, Value = 10, StepFrequency = .1 };
        var mbPair = SliderWithValue(mb, limit);
        mb.ValueChanged += (_, _) => limit.Value = Math.Round(mb.Value, 1);
        limit.ValueChanged += (_, _) => { if (double.IsFinite(limit.Value)) mb.Value = limit.Value; };
        var originalDimensions = new CheckBox { Content = "Original dimensions", IsChecked = true };
        var edge = new NumberBox { Header = "Maximum dimension (pixels)", Minimum = 64, Maximum = 8192, Value = 1280, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var originalFrameRate = new CheckBox { Content = "Original frame rate", IsChecked = true };
        var fps = new NumberBox { Header = "Maximum frame rate", Minimum = 1, Maximum = 240, Value = 30, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var resize = new CheckBox { Content = "Allow resizing to meet target", IsChecked = false };
        var format = new ComboBox { Header = "Image output format", Items = { "JPEG", "PNG - lossless / transparency" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var hardware = new CheckBox { Content = "Use hardware acceleration when available", IsChecked = true };
        var qualityPair = SliderWithValue(quality, qualityNumber);
        var percentPair = SliderWithValue(percent, percentNumber);
        quality.ValueChanged += (_, _) => qualityNumber.Value = quality.Value;
        qualityNumber.ValueChanged += (_, _) => { if (double.IsFinite(qualityNumber.Value)) quality.Value = qualityNumber.Value; };
        percent.ValueChanged += (_, _) => percentNumber.Value = percent.Value;
        percentNumber.ValueChanged += (_, _) => { if (double.IsFinite(percentNumber.Value)) percent.Value = percentNumber.Value; };
        var explanation = Text("", Ui.Body, "#A9BAD4");
        var queueView = new ListView { MaxHeight = 280, SelectionMode = ListViewSelectionMode.None };
        var outputs = new StackPanel { Spacing = Ui.Gap };
        var count = Text("No files queued", Ui.Body, "#A9BAD4");
        CompressionOptions Options(string path)
        {
            if (!double.IsFinite(limit.Value) || !double.IsFinite(edge.Value) || !double.IsFinite(qualityNumber.Value) || !double.IsFinite(percentNumber.Value) || !double.IsFinite(fps.Value))
                throw new IOException("Enter valid compression settings.");
            return new(SelectionValidation.IsVideo(path), (int)edge.Value, (int)qualityNumber.Value, (long)(limit.Value * 1_000_000), format.SelectedIndex == 1, hardware.IsChecked == true,
                (CompressionMethod)method.SelectedIndex, percentNumber.Value, originalDimensions.IsChecked == true, originalFrameRate.IsChecked == true, fps.Value, resize.IsChecked == true);
        }
        Button? retry = null, clearCompleted = null;
        UIElement? compressAction = null, inputPicker = null;
        TextBox? sourceField = null;
        void Render(bool results = true)
        {
            queueView.Items.Clear();
            foreach (var item in compressionQueue)
            {
                var copy = new StackPanel { Spacing = 4 };
                var header = new Grid();
                header.ColumnDefinitions.Add(new()
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                header.ColumnDefinitions.Add(new()
                {
                    Width = GridLength.Auto
                });
                header.Children.Add(Text(Path.GetFileName(item.Source), Ui.Body, "#EDF3FF", true));
                var remove = ActionButton("\uE711", "Remove file", (_, _) => { compressionQueue.Remove(item); if (compressionQueue.Count == 0 && sourceField is not null) sourceField.Text = ""; Render(); });
                remove.Content = Icon("\uE711", 12, "#A9BAD4");
                remove.Width = 30;
                remove.MinHeight = 30;
                remove.Padding = new Thickness(3);
                Grid.SetColumn(remove, 1);
                header.Children.Add(remove);
                copy.Children.Add(header);
                if (item.State != QueueState.Ready)
                    copy.Children.Add(Text(item.Detail, Ui.Supporting, "#A9BAD4"));

                long bytes = File.Exists(item.Source) ? new FileInfo(item.Source).Length : 0;
                long target = item.Options.ResolveTarget(bytes);
                copy.Children.Add(Text("Original: " + FileToolkit.Size(bytes) + " - " + (target == 0 ? item.Options.Png && !item.Options.Video ? "Lossless PNG" : "Quality " + item.Options.Quality + "%" : "Target: up to " + FileToolkit.Size(target) + (item.Options.EffectiveMethod == CompressionMethod.TargetPercent ? " (" + item.Options.TargetPercent + "% of original)" : "")), Ui.Supporting, "#9CACC7"));
                copy.Children.Add(Text((item.Options.Video ? "MP4 / H.264" : item.Options.Png ? "PNG" : "JPEG") + " - " + (item.Options.PreserveDimensions ? "Original dimensions" : item.Options.MaximumEdge + "px maximum") + (item.Options.Video ? item.Options.PreserveFrameRate ? " - Original frame rate" : " - Up to " + item.Options.MaximumFrameRate + " fps" : ""), Ui.Supporting, "#9CACC7"));
                var row = new ListViewItem { Content = copy, Tag = item };
                queueView.Items.Add(row);

            }
            queueView.Visibility = compressionQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Visibility = compressionQueue.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (inputPicker is not null)
            {
                var choose = Descendants(inputPicker).OfType<Button>().First();
                choose.MinHeight = compressionQueue.Count > 0 ? 84 : 140;
                if (choose.Content is StackPanel content)
                {
                    var icon = content.Children.OfType<FontIcon>().FirstOrDefault();
                    if (icon is not null)
                        icon.Visibility = compressionQueue.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            if (compressAction is not null)
                compressAction.Visibility = compressionQueue.Any(i => i.State is QueueState.Ready or QueueState.Working) ? Visibility.Visible : Visibility.Collapsed;
            count.Text = compressionQueue.Count == 0 ? "" : $"{compressionQueue.Count} files · {compressionQueue.Count(i => i.State == QueueState.Ready)} ready";
            if (retry is not null)
                retry.Visibility = compressionQueue.Any(i => i.State is QueueState.Canceled or QueueState.Failed) ? Visibility.Visible : Visibility.Collapsed;
            if (clearCompleted is not null)
                clearCompleted.Visibility = compressionQueue.Any(i => i.State == QueueState.Completed) ? Visibility.Visible : Visibility.Collapsed;
            if (results)
            {
                outputs.Children.Clear();
                foreach (var item in compressionQueue.Where(i => i.Output is not null))
                    outputs.Children.Add(OutputCard(item));

            }
        }
        Task AddFiles(IReadOnlyList<string> files)
        {
            var paths = SelectionValidation.Validate(files, true, true, true);
            // Validate the whole drop before changing the queue.
            var additions = paths.Where(p => !compressionQueue.Any(q => q.Source.Equals(p, StringComparison.OrdinalIgnoreCase) && q.State is QueueState.Ready or QueueState.Working)).Select(p => new CompressionQueueItem(p, Options(p)) { Destination = compressionDirectory ?? Path.GetDirectoryName(p)! }).ToArray();
            compressionQueue.AddRange(additions);
            if (paths.Length > 0)
                mode.SelectedIndex = SelectionValidation.IsVideo(paths[^1]) ? 0 : 1;
            Render();
            return Task.CompletedTask;
        }
        var input = PathPicker("Files to compress", out var source, true, AddFiles);
        sourceField = source;
        inputPicker = input;
        var directory = Field("Save folder");
        directory.Visibility = Visibility.Collapsed;
        directory.Text = compressionDirectory ?? "";
        var destinationNote = Text("", Ui.Supporting, "#A9BAD4");
        var saveRow = new Grid { ColumnSpacing = 10 };
        saveRow.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        saveRow.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        void SaveLabel()
        {
            destinationNote.Text = compressionDirectory is null ? "Save beside originals" : "Save to " + compressionDirectory;
            ToolTipService.SetToolTip(destinationNote, compressionDirectory ?? "Each original folder");
        }
        SaveLabel();
        saveRow.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var resetSave = ActionButton("\uE711", "Use original folders", (_, _) => directory.Text = "");
        resetSave.Content = Icon("\uE711", 12, "#DEEAFF");
        resetSave.Width = 32;
        resetSave.Visibility = compressionDirectory is null ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(resetSave, "Use original folders");
        Grid.SetColumn(resetSave, 2);
        saveRow.Children.Add(resetSave);
        saveRow.Children.Add(destinationNote);
        var saveButton = ActionButton("\uE8B7", "Change", async (_, _) => { try { var folder = await pickers.FolderAsync(); if (folder is not null) directory.Text = folder; } catch (Exception e) { ShowTaskNotice("Could not choose folder", e.Message, NoticeKind.Error); } });
        Grid.SetColumn(saveButton, 1);
        saveRow.Children.Add(saveButton);
        saveRow.Children.Add(directory);
        var saveMenu = new MenuFlyout();
        var beside = new MenuFlyoutItem { Text = "Save beside originals" };
        beside.Click += (_, _) => directory.Text = "";
        saveMenu.Items.Add(beside);
        saveButton.ContextFlyout = saveMenu;
        directory.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) =>
        {
            compressionDirectory = string.IsNullOrWhiteSpace(directory.Text) ? null : directory.Text;
            SaveLabel();
            resetSave.Visibility = compressionDirectory is null ? Visibility.Collapsed : Visibility.Visible;
            foreach (var item in compressionQueue.Where(i => i.State == QueueState.Ready))
                item.Destination = compressionDirectory ?? Path.GetDirectoryName(item.Source)!;
            Render(false);
        });
        source.RegisterPropertyChangedCallback(TextBox.TextProperty, async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(source.Text))
                return;
            try
            {
                await AddFiles([source.Text]);
            }
            catch (Exception e) { ShowTaskNotice("Could not add file", e.Message, NoticeKind.Error); }
        });
        page.Children.Add(input);
        page.Children.Add(saveRow);
        page.Children.Add(count);
        page.Children.Add(queueView);

        var options = Section("Compression");
        options.Children.Add(mode);
        options.Children.Add(method);
        options.Children.Add(qualityPair);
        options.Children.Add(percentPair);
        options.Children.Add(mbPair);
        options.Children.Add(resize);
        var advanced = new StackPanel { Spacing = Ui.Gap };
        advanced.Children.Add(originalDimensions);
        advanced.Children.Add(edge);
        advanced.Children.Add(originalFrameRate);
        advanced.Children.Add(fps);
        advanced.Children.Add(format);
        advanced.Children.Add(hardware);
        options.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        void Controls()
        {
            bool video = mode.SelectedIndex == 0, target = method.SelectedIndex != 0, lossless = !video && format.SelectedIndex == 1;
            qualityPair.Visibility = target ? Visibility.Collapsed : Visibility.Visible;
            quality.IsEnabled = qualityNumber.IsEnabled = !lossless;
            percentPair.Visibility = method.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            mbPair.Visibility = method.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            resize.Visibility = target ? Visibility.Visible : Visibility.Collapsed;
            format.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
            hardware.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            originalFrameRate.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            fps.Visibility = video && originalFrameRate.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
            edge.Visibility = originalDimensions.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            explanation.Text = lossless ? "PNG is lossless; quality does not apply. A small size target may require explicit resizing." : video ? "Quality controls source-relative video bitrate, not a measured percentage of visual fidelity. MP4 / H.264 / AAC. Size targets are maximums, not exact padded sizes." : "JPEG quality controls encoding. Size targets are maximums. JPEG flattens transparency; orientation is respected and metadata removed.";
        }
        method.SelectionChanged += (_, _) => Controls();
        mode.SelectionChanged += (_, _) => Controls();
        format.SelectionChanged += (_, _) => Controls();
        originalDimensions.Checked += (_, _) => Controls();
        originalDimensions.Unchecked += (_, _) => Controls();
        originalFrameRate.Checked += (_, _) => Controls();
        originalFrameRate.Unchecked += (_, _) => Controls();
        void SyncReady()
        {
            try
            {
                foreach (var item in compressionQueue.Where(i => i.State == QueueState.Ready))
                    item.Options = Options(item.Source);
                Render(false);
            }
            catch (IOException) { }
        }
        foreach (var control in new[] { qualityNumber, percentNumber, limit, edge, fps })
            control.ValueChanged += (_, _) => SyncReady();
        method.SelectionChanged += (_, _) => SyncReady();
        format.SelectionChanged += (_, _) => SyncReady();
        foreach (var check in new[] { originalDimensions, originalFrameRate, resize, hardware })
        {
            check.Checked += (_, _) => SyncReady();
            check.Unchecked += (_, _) => SyncReady();
        }
        options.Children.Add(new Expander { Header = "Format details", Content = explanation, HorizontalAlignment = HorizontalAlignment.Stretch });
        page.Children.Add(Glass(options));
        compressAction = Work("Compress", async (token, progress) =>
        {
            foreach (var item in compressionQueue.Where(i => i.State == QueueState.Ready))
                item.Options = Options(item.Source);
            var outcome = await CompressionBatch.RunAsync(compressionQueue, async (item, cancel) =>
            {
                var detail = new Progress<string>(message => { if (item.State == QueueState.Working) { item.Detail = message; Render(false); progress.Report(Path.GetFileName(item.Source) + ": " + message); } });
                return await MediaCompressor.CompressAsync(item.Source, item.Destination, item.Options, detail, cancel);
            }, _ => Render(), token);
            FinishTask(outcome);
        });
        page.Children.Insert(page.Children.IndexOf(queueView) + 1, compressAction);
        retry = ActionButton("\uE710", "Retry failed or canceled files", (_, _) => { foreach (var item in compressionQueue.Where(i => i.State is QueueState.Canceled or QueueState.Failed)) { item.State = QueueState.Ready; item.Detail = "Ready"; } Render(); });
        page.Children.Add(retry);
        page.Children.Add(outputs);
        clearCompleted = ActionButton("\uE711", "Clear completed results", (_, _) => { compressionQueue.RemoveAll(i => i.State == QueueState.Completed); Render(); });
        page.Children.Add(clearCompleted);
        Render();
        Controls();
    }
}
