# Apex Desktop 1.0

Developer kit for Apex Desktop by **APX Studios**, built for Windows 11 x64.

## Get started

Double-click **Apex Desktop.exe** in the application folder to launch. Keep the accompanying files in the same folder.

### Build from source

Open PowerShell in the project folder and run:

```powershell
.\scripts\setup.ps1
.\scripts\build.ps1
```

Then open **Apex Desktop.exe** in `artifacts/apex-portable`, or use **Launch Apex Desktop.cmd**.

## Features

- **System** — storage, cleanup, file organization and file actions.
- **Performance** — Windows settings, hardware diagnostics and Game Booster.
- **Media** — image/video compression and MP4-to-MP3 conversion.

## Technology

- **C# / .NET 8** with **WinUI 3 / Windows App SDK 1.8**.
- **LibreHardwareMonitorLib 0.9.6** and **PawnIO 2.2.0** for hardware sensors.
- **PresentMon 2.6.0** for performance capture.
- **Windows media APIs** for local compression and conversion.
