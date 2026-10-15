using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace Apex;
public sealed partial class MainWindow
{
    private void RecordingPage(StackPanel content)
    {
        EnsurePerformanceLoaded();
        monitoring ??= new();
        gameCapture ??= new(monitoring, recordings);
        content.Children.Add(Text("Record your game's FPS, then compare runs before and after changing settings.", Ui.Body, "#A9BAD4"));
        var game = new ComboBox { Header = "Game", PlaceholderText = "Choose a running game", DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        game.ItemsSource = lastMonitor?.Processes.Where(p => p.CanClose).OrderBy(p => p.Name).ToList() ?? [];
        content.Children.Add(game);
        content.Children.Add(ActionButton("\uE72C", "Refresh games", async (_, _) =>
        {
            try
            {
                lastMonitor = await monitoring.SampleAsync(CancellationToken.None);
                game.ItemsSource = lastMonitor.Processes.Where(p => p.CanClose).OrderBy(p => p.Name).ToList();
            }
            catch (Exception e) { ShowTaskNotice("Could not list games", e.Message, NoticeKind.Error); }
        }));
        var name = Field("Recording name", "Baseline / new settings");
        var notes = Field("Notes", "Game, scene, resolution, graphics settings");
        var duration = new NumberBox { Header = "Duration (seconds)", Value = 60, Minimum = 1, Maximum = 900, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var admin = new CheckBox { Content = "Request administrator access (only if a previous capture reported insufficient permissions)", IsChecked = false };
        var plan = new ComboBox { Header = "Plan to attach", DisplayMemberPath = "Name", ItemsSource = performanceStore.Plans, HorizontalAlignment = HorizontalAlignment.Stretch };
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(name);
        advanced.Children.Add(notes);
        advanced.Children.Add(SelectionPair(duration, plan));
        advanced.Children.Add(admin);
        advanced.Children.Add(Text("Compare the same scene/resolution. FPS uses presentation timing, not input latency.", Ui.Supporting, "#A9BAD4"));
        content.Children.Add(new Expander { Header = "Advanced · name, duration and capture settings", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var captureSummary = Text("60 seconds · 3-second countdown · Stop anytime", Ui.Supporting, "#A9BAD4");
        content.Children.Add(captureSummary);
        duration.ValueChanged += (_, _) => captureSummary.Text = $"{duration.Value:0} seconds · 3-second countdown · Stop anytime";
        var result = new StackPanel { Spacing = Ui.Gap };
        var left = new ComboBox { Header = "Baseline recording", DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        var right = new ComboBox { Header = "Comparison recording", DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        var leftFrames = new ComboBox { Header = "Baseline stream", HorizontalAlignment = HorizontalAlignment.Stretch };
        var rightFrames = new ComboBox { Header = "Comparison stream", HorizontalAlignment = HorizontalAlignment.Stretch };
        void Refresh()
        {
            var all = recordings.List();
            left.ItemsSource = all;
            right.ItemsSource = all;
            result.Children.Clear();
            result.Children.Add(Text($"{all.Count} recordings saved locally", Ui.Body, "#A9BAD4"));
        }
        void SelectStream(ComboBox recording, ComboBox streams)
        {
            streams.ItemsSource = recording.SelectedItem is GameRecording selected ? selected.Frames.OrderByDescending(f => f.Frames).Select(f => new FrameChoice(f)).ToList() : [];
            streams.SelectedIndex = streams.Items.Count > 0 ? 0 : -1;
        }
        left.SelectionChanged += (_, _) => SelectStream(left, leftFrames);
        right.SelectionChanged += (_, _) => SelectStream(right, rightFrames);
        content.Children.Insert(3, Work("Start FPS recording", async (token, progress) =>
        {
            if (game.SelectedItem is not ProcessUsage selected)
                throw new IOException("Select the running game explicitly.");
            if (!double.IsFinite(duration.Value) || duration.Value < 1 || duration.Value > 900)
                throw new IOException("Choose a duration from 1 to 900 seconds.");
            if (!await Confirm("Record this game?", selected.Name + " - PID " + selected.Id + "\n\n" + (admin.IsChecked == true ? "Windows will request administrator access for the capture helper. " : "Capture uses standard permissions. ") + "Only the selected process is recorded. Stop cancels this capture only.", "Record"))
                return;
            gameCapture.RequestElevation = admin.IsChecked == true;
            var record = await gameCapture.RecordAsync(selected, string.IsNullOrWhiteSpace(name.Text) ? selected.Name + " · " + DateTime.Now.ToString("MMM d HH:mm") : name.Text, notes.Text, (int)duration.Value, plan.SelectedItem as PerformancePlan, text => { monitoring.Sensors = sensorSession.Latest; progress.Report(text); }, token);
            Refresh();
            result.Children.Add(Text(record.Complete ? "Recording saved" : "Incomplete recording saved", Ui.SectionTitle, "#83DDFF", true));
            result.Children.Add(Text(record.Issue ?? "Select this recording below to compare or export it.", Ui.Body, "#A9BAD4"));
            foreach (var stream in record.Frames)
                result.Children.Add(Text($"{stream.Metric} / {stream.SwapChain}: {stream.AverageFps:0.#} FPS, {stream.OnePercentLow:0.#} FPS 1% low", Ui.Body, "#A9BAD4"));
            FinishTask(new TaskOutcome(record.Complete ? TaskOutcomeKind.Success : token.IsCancellationRequested ? TaskOutcomeKind.Canceled : TaskOutcomeKind.Partial, record.Complete ? "Recording saved locally" : "Incomplete recording saved: " + record.Issue));
        }, allowNavigation: true));
        content.Children.Add(Glass(result));
        var savedRuns = new StackPanel { Spacing = 16 };
        content.Children.Add(new Expander { Header = "Compare saved recordings", Content = savedRuns, HorizontalAlignment = HorizontalAlignment.Stretch });
        savedRuns.Children.Add(SelectionPair(left, right));
        var streamSettings = new StackPanel { Spacing = 12 };
        streamSettings.Children.Add(SelectionPair(leftFrames, rightFrames));
        streamSettings.Children.Add(Text("If a run has multiple streams, choose the same game view and metric for both.", Ui.Supporting, "#A9BAD4"));
        savedRuns.Children.Add(new Expander { Header = "Advanced · captured streams", Content = streamSettings, HorizontalAlignment = HorizontalAlignment.Stretch });
        var comparison = new StackPanel { Spacing = 12 };
        savedRuns.Children.Add(Work("Compare recordings", async (token, progress) =>
        {
            if (left.SelectedItem is not GameRecording a || right.SelectedItem is not GameRecording b || leftFrames.SelectedItem is not FrameChoice x || rightFrames.SelectedItem is not FrameChoice y)
                throw new IOException("Choose two recordings and their streams.");
            if (a.Id == b.Id || !a.Executable.Equals(b.Executable, StringComparison.OrdinalIgnoreCase) || x.Frame.Metric != y.Frame.Metric)
                throw new IOException("Choose different runs from the same executable and the same presentation metric.");
            comparison.Children.Clear();
            if (!a.Complete || !b.Complete)
                comparison.Children.Add(Text("Includes an incomplete recording; results may be misleading.", Ui.Body, "#83DDFF"));
            comparison.Children.Add(Text("Swap-chain addresses can differ across runs. Match the same game view manually; notes and plan snapshots are shown below.", Ui.Supporting, "#A9BAD4"));
            string Delta(double before, double after) => $"{before:0.##} → {after:0.##} ({after - before:+0.##;-0.##;0}" + (before == 0 ? ")" : $", {(after - before) / before * 100:+0.#;-0.#;0}%)");
            comparison.Children.Add(Text("Average FPS: " + Delta(x.Frame.AverageFps, y.Frame.AverageFps), 24, "#83DDFF", true));
            comparison.Children.Add(Text("1% low FPS: " + Delta(x.Frame.OnePercentLow, y.Frame.OnePercentLow), Ui.Body, "#A9BAD4"));
            var advancedResults = new StackPanel { Spacing = 12 };
            comparison.Children.Add(new Expander { Header = "Advanced results · frame times and resource charts", Content = advancedResults, HorizontalAlignment = HorizontalAlignment.Stretch });
            advancedResults.Children.Add(Text("Frame time p50 / p95 / p99 (ms):\n" + Delta(x.Frame.P50, y.Frame.P50) + "\n" + Delta(x.Frame.P95, y.Frame.P95) + "\n" + Delta(x.Frame.P99, y.Frame.P99), Ui.Body, "#A9BAD4"));
            void Resource(string label, Func<MonitorSample, double?> metric)
            {
                var av = a.Samples.Select(metric).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                var bv = b.Samples.Select(metric).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                advancedResults.Children.Add(Text(label + ": " + (av.Length > 0 && bv.Length > 0 ? Delta(av.Average(), bv.Average()) : "Unavailable in one or both runs"), Ui.Body, "#A9BAD4"));
            }
            Resource("CPU (%)", s => s.Cpu);
            Resource("RAM (GB)", s => s.MemoryUsed / 1_000_000_000d);
            Resource("GPU (%)", s => s.Gpu);
            Resource("CPU temperature (C)", s => Temperature(s, "Cpu"));
            Resource("GPU temperature (C)", s => Temperature(s, "Gpu"));
            advancedResults.Children.Add(ComparisonGraph("CPU usage (%)", a, b, sample => sample.Cpu));
            advancedResults.Children.Add(ComparisonGraph("GPU usage (%)", a, b, sample => sample.Gpu));
            advancedResults.Children.Add(ComparisonGraph("RAM usage (GB)", a, b, sample => sample.MemoryUsed / 1_000_000_000d));
            foreach (var record in new[] { a, b })
                advancedResults.Children.Add(Text(record.Name + " - " + (record.Plan?.Name ?? "No attached plan") + "\n" + record.Notes, Ui.Body, "#A9BAD4"));
            FinishTask("Recordings compared");
            await Task.CompletedTask;
        }));
        savedRuns.Children.Add(Glass(comparison));
        var fileActions = new StackPanel { Spacing = 12 };
        savedRuns.Children.Add(new Expander { Header = "Export or delete", Content = fileActions, HorizontalAlignment = HorizontalAlignment.Stretch });
        fileActions.Children.Add(ActionButton("\uE896", "Export selected run", async (_, _) =>
        {
            try
            {
                if (left.SelectedItem is not GameRecording record)
                    throw new IOException("Choose the baseline recording first.");
                string? destination = await pickers.FolderAsync();
                if (destination is null)
                    return;
                recordings.Export(record, destination);
                ShowTaskNotice("Exported", "CSV and JSON saved to " + destination, NoticeKind.Success);
            }
            catch (Exception e) { ShowTaskNotice("Could not export", e.Message, NoticeKind.Error); }
        }));
        fileActions.Children.Add(ActionButton("\uE74D", "Delete selected run", async (_, _) =>
        {
            try
            {
                if (left.SelectedItem is not GameRecording record)
                    throw new IOException("Choose a recording first.");
                if (!await Confirm("Remove recording?", record.Name + "\n\nDelete its local capture and summary?", "Remove"))
                    return;
                recordings.Remove(record.Id);
                Refresh();
                ShowTaskNotice("Removed", "Recording removed", NoticeKind.Success);
            }
            catch (Exception e) { ShowTaskNotice("Could not remove recording", e.Message, NoticeKind.Error); }
        }));
        Refresh();
    }
    private UIElement ComparisonGraph(string title, GameRecording baseline, GameRecording changed, Func<MonitorSample, double?> metric)
    {
        var panel = Section(title);
        panel.Children.Add(Text(baseline.Name + " (amber) / " + changed.Name + " (white)", Ui.Supporting, "#A9BAD4"));
        var canvas = new Microsoft.UI.Xaml.Controls.Canvas { Height = 120, HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false };
        void Draw()
        {
            canvas.Children.Clear();
            var values = baseline.Samples.Concat(changed.Samples).Select(metric).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (values.Length == 0)
                return;
            double maximum = Math.Max(1, values.Max());
            foreach (var run in new[] { baseline, changed })
            {
                if (run.Samples.Count == 0)
                    continue;
                double duration = Math.Max(1, new[] { baseline, changed }.Where(r => r.Samples.Count > 0).Max(r => (r.Samples[^1].TimeUtc - r.Samples[0].TimeUtc).TotalSeconds));
                Microsoft.UI.Xaml.Shapes.Polyline? line = null;
                foreach (var sample in run.Samples)
                {
                    if (metric(sample) is not double value)
                    {
                        line = null;
                        continue;
                    }
                    if (line is null)
                    {
                        line = new()
                        {
                            Stroke = Brush(run.Id == baseline.Id ? "#A09FFF" : "#EDF3FF"),
                            StrokeThickness = 2
                        };
                        canvas.Children.Add(line);
                    }
                    line.Points.Add(new Windows.Foundation.Point((sample.TimeUtc - run.Samples[0].TimeUtc).TotalSeconds / duration * canvas.ActualWidth, 116 - Math.Clamp(value / maximum, 0, 1) * 110));
                }
            }
        }
        canvas.SizeChanged += (_, _) => Draw();
        panel.Children.Add(canvas);
        panel.Children.Add(Text("Elapsed run time, aligned by recording start; vertical scale 0 to " + baseline.Samples.Concat(changed.Samples).Select(metric).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max().ToString("0.#"), Ui.Supporting, "#A9BAD4"));
        return panel;
    }
    private sealed record FrameChoice(FrameSummary Frame)
    {
        public override string ToString() => $"{Frame.Metric} / {Frame.SwapChain} / {Frame.Frames:N0} frames";
    }
}
