using Apex.Platform;
using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;
using System.Diagnostics;

namespace Apex;
public sealed partial class MainWindow
{
    private WindowsMonitoring? monitoring;
    private readonly SensorSession sensorSession = new();
    private readonly RecordingStore recordings = new();
    private readonly PerformancePlanStore performanceStore = new();
    private GameCapture? gameCapture;
    private CancellationTokenSource? diagnosticLifetime;
    private readonly Queue<MonitorSample> monitorHistory = new();
    private Action<MonitorSample>? renderDiagnostics;
    private MonitorSample? lastMonitor;
    private bool performanceLoaded;
    private string? performanceLoadError;
    private bool verificationMode => Environment.GetCommandLineArgs().Any(a => a is "--verify" or "--verify-layout" or "--verify-bounded");

    private void EnsurePerformanceLoaded()
    {
        if (performanceLoaded)
            return;
        performanceLoaded = true;
        try
        {
            performanceStore.Load();
        }
        catch (Exception e) { performanceLoadError = e.Message; }
    }
    private void StartDiagnosticsLoop()
    {
        if (verificationMode || diagnosticLifetime is not null)
            return;
        monitoring ??= new();
        gameCapture ??= new(monitoring, recordings);
        diagnosticLifetime = new();
        _ = DiagnosticsLoop(diagnosticLifetime.Token);
    }
    private async Task DiagnosticsLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                bool visible = currentPage == "Diagnostics" && (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter p || p.State != Microsoft.UI.Windowing.OverlappedPresenterState.Minimized);
                bool recording = gameCapture?.IsRecording == true;
                sensorSession.SetPaused(!visible && !recording);
                if (visible && !recording)
                {
                    monitoring!.Sensors = sensorSession.Latest;
                    var sample = await monitoring.SampleAsync(token);
                    lastMonitor = sample;
                    monitorHistory.Enqueue(sample);
                    while (monitorHistory.Count > 120)
                        monitorHistory.Dequeue();
                    if (currentPage == "Diagnostics")
                        renderDiagnostics?.Invoke(sample);
                }
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { if (currentPage == "Diagnostics") status.Text = "Monitoring unavailable: " + e.Message; await Task.Delay(1000, CancellationToken.None); }
        }
    }
    private UIElement Metric(string title, string unit, Func<MonitorSample, double?> value, out Action<MonitorSample> update, double? ceiling = null, bool inner = false)
    {
        var body = new StackPanel { Spacing = 10 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var health = Text("", 18, "#83DDFF");
        titleRow.Children.Add(health);
        titleRow.Children.Add(Text(title, Ui.WidgetTitle, "#EDF3FF", true));
        body.Children.Add(titleRow);
        var number = Text("Waiting for reading", 24, "#83DDFF", true);
        body.Children.Add(number);
        var reason = Text("", Ui.Supporting, "#A9BAD4");
        body.Children.Add(reason);
        var graph = new Canvas { Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false };
        body.Children.Add(graph);
        void Draw(MonitorSample sample)
        {
            double? current = value(sample);
            bool thermal = title.Contains("temperature", StringComparison.OrdinalIgnoreCase), percentage = unit == "%";
            var level = MetricHealth.Classify(current, thermal, percentage);
            string color = level switch
            {
                "High" => "#FF8294",
                "Elevated" => "#F4C17B",
                _ => "#83DDFF"
            };
            health.Text = current.HasValue && (thermal || percentage) ? (level == "Cool" ? "❄" : level == "High" ? "▲" : "•") : "";
            number.Foreground = health.Foreground = Brush(color);
            ToolTipService.SetToolTip(health, level + " reading · visual indicator, not a hardware limit");
            number.Text = current is double v ? v.ToString("0.#") + " " + unit : "Unavailable";
            reason.Text = current.HasValue ? "Last 2 minutes" : title.Contains("temperature", StringComparison.OrdinalIgnoreCase) ? sensorSession.Latest?.Error ?? (sensorSession.Enabled ? "CPU sensor not reported. Check driver setup, then enable sensors again." : "Enable sensors for temperature") : "Waiting for a supported Windows counter";
            graph.Children.Clear();
            var history = monitorHistory.Select(value).ToArray();
            if (history.Length < 2 || graph.ActualWidth <= 0)
                return;
            double max = ceiling ?? Math.Max(1, history.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(1).Max());
            Polyline? line = null;
            for (int i = 0; i < history.Length; i++)
            {
                if (history[i] is not double point)
                {
                    line = null;
                    continue;
                }
                if (line is null)
                {
                    line = new Polyline { Stroke = Brush(color), StrokeThickness = 2 };
                    graph.Children.Add(line);
                }
                line.Points.Add(new Windows.Foundation.Point(i / 119d * graph.ActualWidth, 62 - Math.Clamp(point / max, 0, 1) * 58));
            }
        }
        graph.SizeChanged += (_, _) => { if (lastMonitor is not null) Draw(lastMonitor); };
        update = Draw;
        return inner ? body : Glass(body);
    }
    private static double? Temperature(MonitorSample sample, string kind) => SensorValidation.Temperature(sample.Sensors, kind);
    private void Diagnostics()
    {
        StartDiagnosticsLoop();
        EnsurePerformanceLoaded();
        var tabs = new ComboBox { Header = "View", Items = { "Overview", "Processes", "Recordings" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = Ui.Gap };
        page.Children.Add(tabs);
        page.Children.Add(content);
        void Show()
        {
            content.Children.Clear();
            renderDiagnostics = null;
            if (tabs.SelectedIndex == 2)
            {
                RecordingPage(content);
                return;
            }
            if (tabs.SelectedIndex == 1)
            {
                ProcessPage(content);
                return;
            }
            var updates = new List<Action<MonitorSample>>();
            UIElement Card(string title, string unit, Func<MonitorSample, double?> read, double? max = null)
            {
                var card = Metric(title, unit, read, out var update, max, true);
                updates.Add(update);
                return card;
            }
            void Row(string title, string description, UIElement metrics)
            {
                var section = Section(title);
                if (description.Length > 0)
                    section.Children.Add(Text(description, Ui.Supporting, "#A9BAD4"));
                section.Children.Add(metrics);
                content.Children.Add(Glass(section));
            }
            Row("CPU", WindowsMonitoring.CpuName, SelectionPair(Card("Usage", "%", s => s.Cpu, 100), Card("CPU temperature", "C", s => Temperature(s, "Cpu"), 110)));
            Row("GPU", WindowsMonitoring.GpuNames, SelectionPair(Card("Utilization", "%", s => s.Gpu, 100), Card("GPU temperature", "C", s => Temperature(s, "Gpu"), 110)));
            var ramIdentity = RamHardware.Identity.Value;
            var ramStats = Section("Memory used / installed");
            var ramValue = Text("Waiting for reading", 24, "#83DDFF", true);
            var ramUsable = Text("", Ui.Supporting, "#A9BAD4");
            ramStats.Children.Add(ramValue);
            ramStats.Children.Add(ramUsable);
            updates.Add(sample => { long installed = ramIdentity.InstalledBytes > 0 ? ramIdentity.InstalledBytes : sample.MemoryTotal; ramValue.Text = $"{sample.MemoryUsed / 1073741824d:0.##} / {installed / 1073741824d:0.##} GB"; ramUsable.Text = $"{sample.MemoryTotal / 1073741824d:0.##} GB usable by Windows"; });
            Row("RAM", ramIdentity.Name, SelectionPair(Card("Usage", "%", s => s.MemoryTotal > 0 ? 100d * s.MemoryUsed / s.MemoryTotal : null, 100), ramStats));
            Row("Disk", "All disks", SelectionPair(Card("Read", "MB/s", s => s.DiskRead / 1_000_000), Card("Write", "MB/s", s => s.DiskWrite / 1_000_000)));
            Row("Network", "All active adapters", SelectionPair(Card("Receive", "MB/s", s => s.NetworkRead / 1_000_000), Card("Send", "MB/s", s => s.NetworkWrite / 1_000_000)));
            var uptime = Text("", Ui.Body, "#A9BAD4");
            var uptimePanel = Section("Uptime");
            uptimePanel.Children.Add(uptime);
            content.Children.Insert(0, Glass(uptimePanel));
            void Uptime()
            {
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64);
                uptime.Text = $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m" + (elapsed.TotalDays >= 1 ? " · Consider restarting soon when convenient." : "");
            }
            Uptime();
            var sensorDetails = Text("Enable advanced sensors to see hardware readings.", Ui.Supporting, "#A9BAD4");
            content.Children.Add(new Expander { Header = "Hardware sensor readings", Content = sensorDetails, HorizontalAlignment = HorizontalAlignment.Stretch });
            var sensorControls = new StackPanel { Spacing = 8 };
            content.Children.Insert(0, sensorControls);
            sensorControls.Children.Add(ActionButton("\uE9D9", "Enable advanced sensors", async (_, _) =>
            {
                try
                {
                    if (!await Confirm("Enable advanced sensors?", "Start Apex's bundled hardware-monitoring helper? Windows may ask for administrator permission. Sensor availability depends on your hardware and installed driver.", "Enable"))
                        return;
                    await sensorSession.StartAsync();
                    ShowTaskNotice("Sensors enabled", "Readings appear when the hardware reports them.", NoticeKind.Success);
                }
                catch (Exception e) { ShowTaskNotice("Sensors unavailable", e.Message, NoticeKind.Error); }
            }));
            sensorControls.Children.Add(ActionButton("\uE769", "Disable advanced sensors", (_, _) => { sensorSession.Dispose(); if (monitoring is not null) monitoring.Sensors = null; }));
            sensorControls.Children.Add(ActionButton("\uE713", "Sensor driver setup", async (_, _) =>
            {
                if (!await Confirm("Install the sensor driver?", "Open the bundled signed PawnIO 2.2.0 installer. Choose the Official edition. Windows will request administrator permission. Driver installation is separate from monitoring; use Windows Installed apps to uninstall it.", "Open installer"))
                    return;
                try
                {
                    string installer = Path.Combine(AppContext.BaseDirectory, "Vendor", "PawnIOSetup.exe");
                    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(installer))).ToLowerInvariant() != "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032")
                        throw new IOException("Sensor installer checksum mismatch.");
                    Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                }
                catch (Exception e) { ShowTaskNotice("Could not open setup", e.Message, NoticeKind.Error); }
            }));
            content.Children.Remove(sensorControls);
            var sensorButtons = sensorControls.Children.ToArray();
            sensorControls.Children.Clear();
            content.Children.Insert(0, WidgetGrid(sensorButtons));
            renderDiagnostics = sample =>
            {
                foreach (var update in updates)
                    update(sample);
                sensorDetails.Text = sample.Sensors is { Readings.Count: > 0 } packet ? string.Join("\n", packet.Readings.Select(r => r.Hardware + " / " + r.Name + ": " + (r.Value is double v ? v.ToString("0.#") + " " + r.Unit : "Unavailable"))) : sample.Sensors?.Error ?? "No supported sensor readings. Enable advanced sensors or check driver setup.";
                Uptime();
            };
            if (lastMonitor is not null)
                renderDiagnostics(lastMonitor);
        }
        tabs.SelectionChanged += (_, _) => Show();
        Show();
    }
    private void ProcessPage(StackPanel content)
    {
        var sort = new ComboBox { Header = "Sort processes", Items = { "CPU (highest first)", "RAM (highest first)", "Name", "PID" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(sort);
        content.Children.Add(Text("Memory is resident working set; CPU is normalized to total logical processors. Some protected processes cannot be read.", Ui.Supporting, "#A9BAD4"));
        var list = new ListView { MaxHeight = 520, SelectionMode = ListViewSelectionMode.Single };
        var header = new Grid { ColumnSpacing = 12 };
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(70), new GridLength(105), new GridLength(65) })
            header.ColumnDefinitions.Add(new()
            {
                Width = width
            });
        int column = 0;
        foreach (string label in new[] { "Application", "CPU", "RAM", "PID" })
        {
            var text = Text(label, Ui.Supporting, "#A9BAD4");
            Grid.SetColumn(text, column++);
            header.Children.Add(text);
        }
        content.Children.Add(header);
        content.Children.Add(list);
        var rows = new System.Collections.ObjectModel.ObservableCollection<ProcessRow>();
        list.ItemsSource = rows;
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid ColumnSpacing='12' Padding='0,8'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='70'/><ColumnDefinition Width='105'/><ColumnDefinition Width='65'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis' FontSize='14'/><TextBlock Grid.Column='1' Text='{Binding Cpu}' FontSize='14'/><TextBlock Grid.Column='2' Text='{Binding Memory}' FontSize='14'/><TextBlock Grid.Column='3' Text='{Binding Pid}' FontSize='12'/></Grid></DataTemplate>");

        void Render(MonitorSample sample)
        {
            var ordered = sort.SelectedIndex switch
            {
                0 => sample.Processes.OrderByDescending(p => p.Cpu),
                2 => sample.Processes.OrderBy(p => p.Name),
                3 => sample.Processes.OrderBy(p => p.Id),
                _ => sample.Processes.OrderByDescending(p => p.Memory)
            };
            var target = ordered.ToArray();
            var keys = target.Select(p => (p.Id, p.Started)).ToHashSet();
            for (int i = rows.Count - 1; i >= 0; i--)
                if (!keys.Contains((rows[i].Process.Id, rows[i].Process.Started)))
                    rows.RemoveAt(i);
            for (int i = 0; i < target.Length; i++)
            {
                var process = target[i];
                int previous = rows.ToList().FindIndex(r => r.Process.Id == process.Id && r.Process.Started == process.Started);
                if (previous < 0)
                    rows.Insert(i, new(process));
                else
                {
                    rows[previous].Update(process);
                    if (previous != i)
                        rows.Move(previous, i);
                }
            }
        }
        renderDiagnostics = Render;
        sort.SelectionChanged += (_, _) => { if (lastMonitor is not null) Render(lastMonitor); };
        if (lastMonitor is not null)
            Render(lastMonitor);
    }
    private sealed class ProcessRow(ProcessUsage process) : System.ComponentModel.INotifyPropertyChanged
    {
        public ProcessUsage Process { get; private set; } = process;
        public string Name => Process.Name; public string Cpu => Process.Cpu.ToString("0.0") + "%"; public string Memory => FileToolkit.Size(Process.Memory); public string Pid => Process.Id.ToString();
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void Update(ProcessUsage value)
        {
            Process = value;
            PropertyChanged?.Invoke(this, new(null));
        }
    }
}
