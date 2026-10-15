# Apex Desktop

**APX Studios · Windows desktop toolkit · 0.4.11 preview**

Apex Desktop brings local file tools, Windows settings, gaming preparation and media tools into three workspaces. The interface uses a dark blue theme with cyan/violet accents and a static flame mark.

## Workspaces

| Workspace | Tools |
| --- | --- |
| **System** | Storage, Cleanup, Organize and File Actions. |
| **Performance** | Windows settings, Diagnostics and Game Booster. |
| **Media** | Compress images/videos and convert MP4 audio to MP3. |

Each workspace opens a landing page with glass cards, slightly smaller than the Home cards. Cards keep a consistent height when the window is maximized. Choose a tool to open it; entering System never starts in Storage. Clickable breadcrumbs such as **Home / System / Storage** keep Home and the parent workspace available on every tool page. The previous tool tabs have been removed.

Media's **Convert** tool is deliberately small: browse/drop one MP4, then select **Convert to MP3**. It writes a 192 kbps MP3 beside the source, keeps the original untouched and uses a numbered filename when needed. Progress/cancellation and Open/Show in folder are available. Videos without audio and unsupported codecs are reported explicitly. Conversion runs locally through [Windows media transcoding](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/transcode-media-files); no additional converter, online service or codec download is used. Other output formats can be added later without expanding today's controls.

Game Booster reads CPU/RAM usage and available GPU counters automatically when opened. It groups processes by executable, offers search and resource sorting, and keeps system, game and protected processes out of the selection list. **Scan network usage** requests administrator permission for a five-second Windows TCP/UDP sample. Network figures include local traffic; they are a recent sample, not a continuous internet meter. Preparation reviews the selected apps and cleanup before acting. Normal closure can leave tray-only apps running; results identify that outcome and offer a separately confirmed force-close action where allowed. Resource readings do not establish FPS gains.

## Run

Use **Launch Apex Desktop.cmd**, or open `artifacts/apex-portable/Apex Desktop.exe` after building. Keep the complete portable folder together. The development launcher selects the newest complete launch copy; it is not a customer update system.

Windows 11 x64 is the current target. The portable build includes its .NET and Windows App SDK runtime. Hardware temperatures require supported hardware and an explicitly installed sensor driver; an unavailable reading is never presented as zero.

This is a **preview**, not a signed public release. See [release validation](RELEASE-VALIDATION.md) for evidence and [the release checklist](RELEASE-TEST-CHECKLIST.md) for remaining live tests and distribution gates.

## Build and verify

From the repository root in PowerShell:

```powershell
./scripts/setup.ps1
./scripts/build.ps1
./scripts/build.ps1 -Verify -Bounded -OutputDirectory artifacts/candidate
./scripts/build.ps1 -Package
```

Setup installs a workspace-local SDK under `.tools`; restoring dependencies requires internet access. `-Package` creates an **unsigned** MSIX with a provisional package identity. Configure the final publisher identity and signing channel before distribution.

Bounded verification uses disposable files, fake settings/closure providers, real Windows media encoding and screenshots of Apex's own interface at several sizes. It does not clean personal files, close real apps, install drivers or apply Windows settings. See the checklist for the separately approved live tests. The network integration test is recorded separately from these checks.

Optional network integration checks: `./scripts/verify-network.ps1` and `./scripts/verify-network.ps1 -Cancel`. Each opens an administrator prompt for the network helper; the sample generates loopback traffic, and the cancellation case verifies that an interrupted helper exits.

## Data and permissions

- Preferences, app approvals, move/restore journals and recordings are stored under `%LOCALAPPDATA%\ApexDesktop`. Missing records are imported once from the previous application data folder; existing Apex data is not replaced and the legacy copy remains available for recovery.
- Files selected for cleanup are rechecked before recycling. Emptying the existing Recycle Bin is permanent and requires explicit review. Combined cleanup empties the old Bin first, then recycles newly selected files.
- Executable approvals use the path and SHA-256 identity. Process IDs and start times are checked again before closure. Reopening an app cannot restore unsaved work.
- Optional administrator helpers have bounded purposes. The network helper reads process IDs and byte counts from an owned tracing session, returns aggregate rates and stops the session; it does not retain packet contents or endpoint addresses.
- Scans retain at most 100,000 file/directory entries and bounded error text. A limit warning means results are partial. Displaying 1,000 rows is a separate UI limit.
- Direct Windows changes use original-value journals and conflict-aware restoration. Manual guidance is shown separately and is never reported as applied automatically.

## Source layout

- `MainWindow*.cs` — WinUI shell, workspaces and controls.
- `Services/`, `Models/` — application logic, persisted records and verification checks.
- `Platform/` — Windows integration and process/file safeguards.
- `Recovery/`, `SettingsHelper/`, `Sensors/`, `CaptureHelper/`, `NetworkHelper/` — narrowly scoped helpers.
- `Assets/`, `Vendor/` — visual/audio assets and pinned third-party dependencies/notices.
- `scripts/` — setup, build, launch and generated-output maintenance.

Generated builds, tool caches and private key files are excluded from source control. The root/project names use Apex. The few legacy-brand references in code preserve existing data and protect old running instances; old build bundles and historical notes are not part of the source upload.

## GitHub source check

The local pre-push hook checks selected source files and Git history for obsolete names and build outputs. Only the two pinned `Vendor/` executables are allowed in the source tree. Run the check explicitly before preparing a commit:

```powershell
./scripts/check-source.ps1
```

For a fresh clone, enable the same repository hook with `git config core.hooksPath .githooks`. The hook uses PowerShell 7 (`pwsh.exe`). Build and package outputs stay local; distribute a verified package separately. Historical validation notes are kept only under ignored `artifacts/`.

## Before public distribution

Complete the approved live Windows workflows, supported-runtime work, clean-machine installation/update testing, crash/support tools, dependency audit, third-party redistribution review and signing. The repository's own source license has not yet been selected; bundled dependencies retain their individual notices in `Vendor/`.

Windows tracing reference: [Microsoft — starting a private system trace session](https://learn.microsoft.com/en-us/windows/win32/etw/configuring-and-starting-a-systemtraceprovider-session).
