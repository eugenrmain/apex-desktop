# Candidate validation — 2026-10-15

## Navigation card sizing and cleanup cancellation — version 0.4.11

- Home navigation cards now have a 160px minimum height; workspace tool cards use 144px. Glass styling, typography and breadcrumbs are preserved. Workspace rows size to content instead of stretching to the viewport, including maximized windows. The final Performance card keeps the same column width as its neighbors.
- Cleanup has a secondary Cancel beside Clean after a scan, including empty scans. It discards findings, selections and scan issues while keeping category/folder choices and returning to Scan. It replaces the previous Scan again action. Cancel during active work remains available; a canceled scan checks its token before publishing results. Discarding results does not touch files or empty the Recycle Bin.
- Final isolated interface verification passed **121 checks**, recorded in `artifacts/apx-verification-0.4.11-final/results.json`. All fourteen pages render at 800x600, 1100x760, 1600x1000, constrained widths and maximized layouts. Card heights are checked at every size, including maximized; Home and inner cards retain their intended sizes. Existing navigation, breadcrumbs, state retention and modal checks pass.
- A real disposable cleanup fixture is scanned, canceled and scanned again. Results disappear, categories/folders remain selected and the fixture file's contents stay unchanged. No cleaning, Recycle Bin operation or personal-file scan is performed. Active-work cancellation is existing behavior; this run validates the newly added result-discard action. Initial verification exposed a collapsed-section assumption in the new test setup; the test expands that section before selection and the final rerun passes.
- Home, Media and minimum-size System screenshots were inspected. Earlier 0.4.10 service validation passed 153 functional checks; those unchanged service checks were not rerun for this presentation/state update.
- Final Release build and unsigned MSIX packaging succeeded. `artifacts/apex-preview` matches all **572 files** in `artifacts/apx-release-0.4.11` by SHA-256; the launcher selects that verified 0.4.11 preview copy. Portable 0.4.10 and candidate 0.4.9 are in use and were preserved; refresh after they close is queued. Delivery evidence: `artifacts/delivery-hashes-0.4.11.json`.
- The source guard passes for 135 files. Generated output, local evidence and private keys are excluded from the source upload.

## Source preparation

- The source repository is [eugenrmain/apex-desktop](https://github.com/eugenrmain/apex-desktop), with `main` as its primary branch. The initial source upload contains the current preview and its release checklist; it is separate from publishing a signed application release.
- `bin/`, `obj/`, `artifacts/`, `dist/`, SDK caches and private key files are excluded by `.gitignore`. The rule applies to helper build folders as well as the main project.
- `.githooks/pre-push` runs `scripts/check-source.ps1`. It checks tracked/pending files and recorded Git history, rejecting generated outputs, obsolete legacy-named paths and unexpected compiled binaries. The two pinned tools in `Vendor/` are intentional build dependencies.
- The source guard was tested with a deliberately staged inert legacy executable and rejected it. The fixture was then removed from both the index and filesystem. The clean hook passes.
- Old generated build/cache folders and historical legacy application bundles were removed. Current Apex builds and validation fixtures remain local under ignored `artifacts/`. Historical release notes are also retained there, outside the source file list.
- Remaining legacy-brand references in code are limited to data migration, its verification fixture and process protection. They preserve old preferences/undo records and protect an old running instance; they do not name the app or its outputs.

## Remaining public release gates

- Live Windows setting apply/restore and display rollback, Discord/tray closure, sensor driver/CPU-temperature validation and broader workloads.
- Supported runtime, clean-machine installation/update testing and basic crash/support tools.
- Source license, vendor redistribution review, final package/signing identity and signed-artifact testing. The current MSIX is unsigned.

Use [the release checklist](RELEASE-TEST-CHECKLIST.md) for the detailed test plan. Local test evidence and historical notes live under ignored `artifacts/`; they are not source files intended for GitHub.
