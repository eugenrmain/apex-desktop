using System.IO.Pipes;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

if (args.Length != 4 || args[0] != "--pipe" || !args[1].StartsWith("Apex.Sensor.", StringComparison.Ordinal) || args[2] != "--token" || args[3].Length != 64 || !args[3].All(Uri.IsHexDigit)) return;
try
{
    using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(15000);
    using var reader = new StreamReader(pipe, leaveOpen: true); var command = reader.ReadLineAsync(); bool paused = false;
    using var writer = new StreamWriter(pipe) { AutoFlush = true };
    var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = false, IsMotherboardEnabled = false, IsStorageEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false };
    computer.Open();
    try {
    while (pipe.IsConnected)
    {
        if (command.IsCompleted) { var message = await command; if (message is null) break; paused = message == "Pause"; command = reader.ReadLineAsync(); }
        if (paused) { await Task.Delay(1000); continue; }
        var readings = new List<object>(); string? error = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled ? null : "Install the sensor driver, then enable sensors again for CPU temperature.";
        try
        {
            foreach (var hardware in computer.Hardware)
            {
                try { hardware.Update(); } catch (Exception e) { error = hardware.Name + ": " + e.Message; continue; }
                foreach (var sub in hardware.SubHardware) sub.Update();
                foreach (var sensor in hardware.Sensors.Concat(hardware.SubHardware.SelectMany(h => h.Sensors)))
                {
                    if (sensor.SensorType is not (SensorType.Temperature or SensorType.Load or SensorType.Clock or SensorType.Power or SensorType.Fan)) continue;
                    string kind = hardware.HardwareType.ToString();
                    string unit = sensor.SensorType switch { SensorType.Temperature => "C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Power => "W", _ => "RPM" };
                    readings.Add(new { Hardware = hardware.Name, Name = sensor.Name, Kind = kind, Value = sensor.Value is float v && float.IsFinite(v) && (sensor.SensorType != SensorType.Temperature || v is >0 and <130) ? (double?)v : null, Unit = unit });
                }
            }
        }
        catch (Exception e) { error = e.Message; }
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { Token = args[3], Packet = new { Readings = readings, Error = error } }));
        await Task.Delay(1000);
    }
    } finally { computer.Close(); }
}
catch { Environment.ExitCode = 1; }
