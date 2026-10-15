# Proposed large-data tests — awaiting approval

No large fixtures have been generated and no large scan, benchmark, game capture, driver install or live settings change has been run for this milestone.

## Phase A: file tools

- Disposable root: `artifacts/large-fixtures` inside the ApexDesktop workspace.
- Source folders: `storage`, `compare-left`, `compare-right`, `organize`, `cleanup` under that root; output: `outputs` under the same root.
- 10,000 files in 500 nested folders; approximately 1 GiB source data plus up to 1 GiB comparison copies. Include Unicode names, empty folders, duplicates, collisions and disposable inaccessible/link cases.
- Test Storage scan/search/cancel, Compare metadata/hash/cancel, Organize preview and confirmed fixture-only moves/undo conflicts, Cleanup preview and fixture-only recycling. Deletion/cleanup remains a separately confirmed step in the app.
- Estimated 2–10 minutes, one core for scan/hashing, roughly 100–500 MB additional RAM, temporary SSD read/write load. These are estimates, not measured guarantees.

## Phase B: mixed-media queue

- Source: `large-fixtures/media`; output: `large-fixtures/outputs/media`.
- 30 disposable images (about 150 MB total) and three disposable 1080p videos of 60 seconds each (about 300–600 MB total). Size varies by codec/content. Original sources retained.
- Sequential compression, mixed formats, explicit/automatic output folders, collision numbering, size verification, one canceled queue. Estimated 5–15 minutes and up to 1 GiB extra disk space.
- Software encoding may use 60–100% CPU; hardware encoding may use a GPU encoder and reduce CPU load. Expected extra RAM roughly 200–800 MB, with temporary disk/encoder activity. Your other applications remain running.

Require at least 5 GiB free disk space. Stop/Cancel is available throughout; no new queued item starts after cancellation. Keep all sources/results for inspection, then remove only the verified disposable root after approval. Never close other apps, change live settings, restart Windows, install a driver, or launch/capture a game as part of these phases.

Approve Phase A and/or B and choose a time when the temporary CPU/disk load is acceptable. Driver, installed-MSIX, real-game recording and actual plan-apply/restore testing require their own separate approval and target machine.

## Separate live Windows Tweaks / Game Booster proposal

Not started. These checks need explicit approval and selected targets:

- Tweaks: choose which power, mouse and appearance controls to apply/restore on this PC. Review originals first; display refresh changes need their own 15-second Keep test. No DNS or startup changes unless you select a specific adapter/entry and requested value. Estimated 5–10 minutes, negligible disk use; display may briefly blank. Restoration conflicts remain untouched.
- Booster: launch only a disposable test app with no unsaved work and approve that exact executable. Test normal/refused closure, cancellation, manual reopening and separately confirmed force closure only on that fixture. Estimated 5 minutes. Other apps remain unapproved. Do not test against browsers, games, editors or your current work.
- Recycling: review individually selected disposable, aged temporary files, excluding browser/game/shader cache paths. Confirm recycling separately; restore using Windows Recycle Bin. No broad unattended cleanup.

Real game captures, sensor-driver installation, installed-MSIX checks and 125–200% Windows scaling changes remain separate approvals. No tests change security settings, terminate your work or restart Windows.
