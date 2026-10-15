# Apex Desktop — APX Studios release checklist

Candidate: **1.0.0**. Unchecked items remain pending; simulated tests do not imply live Windows tests passed. Record candidate version, Windows version, hardware, steps, expected/actual outcome and evidence. No public release with recovery or data-loss failures.

User-reported on 0.4.6: navigation, cleanup, compression, organizing/undo, Game Booster and completion sound work. Hardware readings work except CPU temperature; sensor driver was not installed or is uncertain. Retest revised workflows on 1.0.0. These reports do not cover installation, restore/rollback, tray-only Discord, signing or large/network datasets. Automated checks and the separate network integration test are recorded in RELEASE-VALIDATION.md.

## 1. Appearance and navigation

- [ ] Launch `artifacts/apex-portable/Apex Desktop.exe`; verify version 1.0.0 and APX Studios.
- [ ] Compare absent. Main navigation: Home, System, Performance, Media, Settings. System cards: Storage, Cleanup, Organize, File Actions. Performance cards: Windows Settings, Diagnostics, Game Booster. Media cards: Compress, Convert.
- [ ] System/Performance/Media always open their landing pages, including after visiting a tool. Cards match Home's glass style, stay a compact consistent height and remain smaller than the Home cards; no horizontal tool-tab strip remains. Root directory is ApexDesktop and launcher paths still resolve.
- [ ] Every page has the universal Home breadcrumb/icon. Tool pages show Home / workspace / tool; Home and workspace links navigate correctly. Keyboard and screen-reader names remain clear.
- [ ] Flame stays static on hover and navigation: blue-purple gradient, white bolt, no glow, sparks or timer. Animation was removed at the user’s request for performance.
- [ ] Windows reduced motion still respected by other transient effects; actions remain usable.
- [ ] Check consistent dark blue glass, cyan/violet accents, icons, labels, selected/disabled/hover/pressed states across every page and popup.
- [ ] Widgets react immediately on click while hovered, and Space/Enter work. State is understandable without color alone.
- [ ] Minimum window, maximized, 100%, 125%, 150%, 200% scaling: no clipped controls or horizontal scrolling.
- [ ] Keyboard: visible focus, Tab/Shift+Tab, popup focus trapping, Escape, focus restoration. Screen-reader labels and notifications.
- [ ] Transparency off, light/dark desktop backgrounds and high contrast: readable controls and text.
- [ ] Notification success/partial/error/canceled states correct; fade pauses on hover/focus. Only full success gets a chime and sparks.
- [ ] Sound enabled/disabled: one quiet cue, no unexpected or repeated playback.

- [ ] Cleanup: Cancel during a scan stops it. Cancel beside Clean discards results, keeps the chosen categories/folders and allows a fresh scan without changing any files.

## 2. Local data and delivery

- [ ] First real launch imports legacy application records into `%LOCALAPPDATA%/ApexDesktop`; Settings opens the new folder.
- [ ] Preferences, Custom presets, app approvals, manual steps, recordings and move/restoration journals survive migration.
- [ ] Repeated launches do not reimport stale data; existing Apex records are never overwritten. Legacy folder remains a recovery copy.
- [ ] Access denial, linked folders/files and interrupted imports fail clearly without erasing original records.
- [ ] Launcher opens the exact new executable; no obsolete main Apex.exe remains in delivered portable copies. Keep the full folder together.

## 3. Windows Tweaks — approved live tests

Save original values and test one category at a time, then restore.

- [ ] Preset selection only prepares changes; Review/Confirm is required. Direct, Manual, Unavailable and Needs selection are distinct.
- [ ] Appearance: thumbnails and font smoothing enabled; every other requested effect disabled directly or explicitly labeled Manual where unsupported.
- [ ] Apply/restore appearance and pointer acceleration; original thresholds/speed and font settings preserved. Raw input explanation accurate.
- [ ] Existing High performance scheme selected; missing scheme explained without creating/substituting one. Restore original scheme.
- [ ] Display: unchanged resolution, highest supported refresh, per-monitor Keep confirmation, timeout rollback; helper recovers after app closure.
- [ ] HDR/mirroring/unsupported displays get guidance rather than unsafe direct changes.
- [ ] Selected DNS/adapter only, permission denial and restoration; no automatic provider choice.
- [ ] Selected supported startup entries only; disable/restore separate from app closure.
- [ ] Priority rechecks process identity; exited process/PID reuse does not affect another process or block unrelated actions.
- [ ] External setting changes after apply create restoration conflicts; Apex does not overwrite them.
- [ ] Cancel/deny elevation partway: completed, skipped, failed and unattempted actions accurate. Journals survive relaunch.
- [ ] **Release gate:** verified settings applied and manual steps remaining shown separately. Success tick never claims manual settings changed.
- [ ] Windows/game guidance has no Done/Skip/check-in controls; it never claims Apex applied or verified those steps. Existing saved instructions remain readable.
- [ ] Repeat the same preset: already-applied values do not fail or create another restore record. Settings changed outside Apex still require resolution.
- [ ] Startup review shows Registered at sign-in → Disabled, never raw JSON or command-line data.
- [ ] Visual effects: one appearance choice plus Windows controls; Advanced is the final compact category.
- [ ] Background-app management opens Game Booster; Advanced process priority lists eligible running apps automatically.
- [ ] Error notice appears above review, with full detail available on hover; no duplicate error paragraph beneath the action.

## 4. Game Booster — approved live tests

Save work. Begin with disposable apps and approve each executable.

- [ ] Nothing selected on first use; all five cleanup categories off. Previously approved, unchanged apps can be remembered.
- [ ] Apps load automatically; one row per executable, aggregate CPU/RAM and available GPU/network usage. Search/sort/refresh work; remembered approvals never blanket-approve another same-name executable.
- [ ] Scan network usage is visible. Approve/deny UAC, retry, cancel before/during sampling. Missing GPU/network readings remain unavailable rather than fabricated. Network includes local TCP/UDP traffic and is a five-second sample.
- [ ] Network helper and its owned tracing session stop after completion/cancellation; other monitoring sessions remain untouched. No addresses, ports or packet contents are saved.
- [ ] Protected-app count opens its list; keeping an app open removes its closure approval, and a user protection can be removed.
- [ ] Apex, games, dependencies, system and security processes protected; unknown apps not automatically closed.
- [ ] Review actual running instances; identity rechecked before action, exited/reused PIDs and new instances handled safely.
- [ ] Normal close confirms real exit. Unsaved prompts/refused requests skipped without automatic force termination.
- [ ] **Discord:** open, minimized and tray-only. Remaining tray processes report skipped/Quit guidance rather than success.
- [ ] **Browser:** multiple windows/child processes, active downloads and unsaved forms. Grouped results accurate.
- [ ] Separate force confirmation affects only reviewed/revalidated instances; no blanket process-tree kill.
- [ ] Cancellation retains completed closures; cleanup cancellation never implies apps were reopened.
- [ ] Temporary/application/game/browser cleanup and existing Recycle Bin are optional and explicitly reviewed. Active caches remain skipped if their app does not actually exit. Game saves, shaders and installed content remain excluded.
- [ ] Combined preparation empties the reviewed old Bin before recycling new files. Per-file review/select-all, scan limits, changed files, cancellation and partial category totals remain accurate.
- [ ] Reopen optional, never automatic; no claim to recover unsaved work.
- [ ] Results distinguish completed/skipped/failed/canceled; resource readings never presented as measured FPS gains.

## 5. System Clean — disposable fixtures first

- [ ] Categories unchecked initially; selecting/scanning does not delete.
- [ ] Disposable caches distinguished from personal files, backups and older files. Age alone does not imply junk.
- [ ] Browser cache only: cookies, history, passwords untouched. Active app/browser cache locations skipped safely.
- [ ] Game discovery read-only; saves, shaders and installed content excluded. Unknown folders not presumed disposable.
- [ ] Folder selection shows name/path immediately; clear/remove resets it.
- [ ] Duplicate cleanup keeps an original; inaccessible/cloud/linked files handled safely.
- [ ] Changed files revalidated and skipped before mutation.
- [ ] Recycle fixtures, restore through Recycle Bin and verify contents.
- [ ] One Clean action. Recycle Bin participates only when its category is included. The combined confirmation clearly states permanent deletion of existing Bin contents.
- [ ] Existing Bin contents are emptied first; newly selected files are recycled afterward and remain recoverable. Cancel/skip counts reflect both parts accurately.
- [ ] Select all/Clear selection work on findings. Above 1,000 rows, Select all shown is clearly limited to displayed items.
- [ ] Select all categories exposes any required folders; absent folders are reported as skipped rather than aborting other categories.
- [ ] Scan/clean cancellation and partial totals accurate; empty/error/success results understandable.

## 6. Diagnostics, file tools and media

- [ ] CPU/RAM plausible, no duplicate RAM display; missing temperatures show unavailable rather than 0°C.
- [ ] Uptime sensible, restart suggestion above one day; no automatic reboot.
- [ ] Sensors: no driver, supported hardware, missing readings, permission denial, explicit enable/disable/install. For this machine: Sensor driver setup → install → Enable advanced sensors again, then confirm CPU temperature.
- [ ] Re-enabling rebuilds the helper after installation. A failed CPU update does not discard otherwise available GPU readings.
- [ ] Cool readings show a snowflake; elevated/high temperature or usage uses warmer colors. Unavailable never looks like a valid low reading. Indicators do not claim device-specific thermal limits.
- [ ] Recordings basic flow clear, advanced options collapsed, permission/capture failures actionable.
- [ ] Sorting Pictures/Documents/Videos/Music/Archives/Code show selected states; deselecting removes the corresponding rule.
- [ ] Add a named custom extension group, restart, verify it persists. Invalid extensions/names fail without changing saved groups.
- [ ] Move automatically previews in its confirmation; Cancel leaves files untouched. Collisions, cancellation and Undo preserve data. No separate Preview action.
- [ ] File Actions fills name/size/modified/attributes on browse/drop. Clearing or switching selections discards stale metadata. No Inspect button.
- [ ] Locking/stuck apps is always visible, loads running apps automatically and refreshes locking apps after file selection. One Close action; force remains explicit and separate from normal closure.
- [ ] Media → Convert card: choose/drop one MP4, verify filename and size, then Convert to MP3. No extra quality/format menus. Open/Show in folder appear after completion.
- [ ] Verify MP3 playback, audible content, duration and 192 kbps encoding using short, long, mono/stereo and supported multi-channel MP4 sources. Original bytes remain unchanged.
- [ ] MP4 without audio, malformed/protected input, unsupported codecs and read-only output folders fail clearly without a completed/partial MP3.
- [ ] Cancel before/during conversion; verify no temporary MP3 remains. Existing files/folders and filenames created during conversion are never overwritten.
- [ ] Navigate away/back: Media opens its landing page; choosing Convert again retains the input and converted output. A changed/cleared selection resets stale results.
- [ ] Compression preserves originals, validates output size and reports unsupported formats.
- [ ] Browsing/dropping queues files immediately; no second checkbox/settings-apply action. Changing settings updates ready files immediately.
- [ ] Default save is beside each original. One-line Change folder control has no expander, updates all ready destinations and can be reset.
- [ ] Remove/reset, mixed image/video batches, retry, cancellation and existing output-name collisions remain correct.

## 7. Performance and resilience

Use disposable folders first. The recorded 1k/10k benchmark covers the scan service; UI responsiveness, network/cloud storage and larger workloads still need separate validation.

- [ ] Record idle CPU/private memory after settling on each main page. No flame animation timer exists.
- [ ] Repeated navigation/popups/notifications: memory/timers settle without progressive growth.
- [ ] Scan 1k then 10k files; record time, peak private memory, results and cancellation latency. UI remains responsive.
- [ ] Agreed larger datasets, deep trees, long paths, access denial, links and cloud placeholders.
- [ ] **Release gate:** scans stop at 100,000 retained file/directory entries and cap retained error text. Reaching the limit explicitly reports partial results and asks for a smaller folder. Cleanup/organizing never claim the full folder was processed. The 1,000-row display limit is separate.
- [ ] Unexpected exit/restart preserves journals and incomplete-task status without corruption or false success.
- [ ] Local crash logs and reviewable diagnostic export; sensitive paths visible before sharing.

## 8. Runtime, installation and signing

- [ ] Supported runtime migration; rerun affected tests on the final build.
- [ ] Clean supported Windows install launches without developer SDK/tools; dependencies included.
- [ ] One installation/shortcut, visible version; upgrade preserves data, uninstall behavior defined.
- [ ] About/support contact/privacy/third-party notices complete.
- [ ] Bundled capture, sensors and driver redistribution/license review complete.
- [ ] Choose Store or independent signing; obtain validated package/certificate identity.
- [ ] APX Studios display name; MSIX Publisher must exactly match signing certificate subject or assigned Store identity.
- [ ] Sign final verified candidate, timestamp and verify trust on clean Windows. Package remains unsigned before this step.
- [ ] Exact delivered artifact tested for install, launch, update/uninstall; verify version and file hashes after signing.

Official signing reference: https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview

Suggested order: visual/keyboard review → isolated data and disposable fixtures → approved live apply/restore and display rollback → Discord/browser and sensors → agreed large scans → runtime/install/support gates → signing and final package tests.
