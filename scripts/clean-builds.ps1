param([switch]$LegacyBuilds)
$ErrorActionPreference = 'Stop'
$apexRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$apexBoundary = $apexRoot + [IO.Path]::DirectorySeparatorChar
$apexGenerated = @('bin', 'obj', 'Recovery/bin', 'Recovery/obj', 'Sensors/bin', 'Sensors/obj', 'SettingsHelper/bin', 'SettingsHelper/obj', 'CaptureHelper/bin', 'CaptureHelper/obj', 'NetworkHelper/bin', 'NetworkHelper/obj', 'scripts/NetworkCheck/bin', 'scripts/NetworkCheck/obj', 'artifacts/helper-stage', 'artifacts/package-stage', 'artifacts/recovery-stage')
$apexArtifacts = Join-Path $apexRoot 'artifacts'
foreach ($apexDirectory in Get-ChildItem -LiteralPath $apexArtifacts -Directory) {
    if ($apexDirectory.Name -match '^package-stage-[a-f0-9]{32}$') { $apexGenerated += 'artifacts/' + $apexDirectory.Name }
    if ($LegacyBuilds -and ((Test-Path -LiteralPath (Join-Path $apexDirectory.FullName 'Ember.exe')) -or (Test-Path -LiteralPath (Join-Path $apexDirectory.FullName 'Ember.dll')))) { $apexGenerated += 'artifacts/' + $apexDirectory.Name }
}
if ($LegacyBuilds) { $apexGenerated += 'artifacts/app-build' }
$apexRemoved = 0
$apexSkipped = @()
foreach ($apexRelative in $apexGenerated) {
    $apexTarget = [IO.Path]::GetFullPath((Join-Path $apexRoot $apexRelative))
    if (-not $apexTarget.StartsWith($apexBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target escapes Apex.' }
    if (Test-Path -LiteralPath $apexTarget) {
        if ((Get-Item -LiteralPath $apexTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked generated directory.' }
        if (@(Get-ChildItem -LiteralPath $apexTarget -Recurse -Force -Attributes ReparsePoint).Count) { throw 'Refusing a generated directory containing links.' }
        # Check loaded binaries before deleting anything from this directory.
        $apexHandles = [Collections.Generic.List[IDisposable]]::new()
        $apexBusy = $false
        try {
            foreach ($apexBinary in Get-ChildItem -LiteralPath $apexTarget -File -Recurse | Where-Object { $_.Extension -in @('.exe', '.dll') }) {
                $apexHandles.Add([IO.File]::Open($apexBinary.FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None))
            }
        } catch { $apexBusy = $true }
        finally { foreach ($apexHandle in $apexHandles) { $apexHandle.Dispose() } }
        if ($apexBusy) { $apexSkipped += $apexRelative; continue }
        Remove-Item -LiteralPath $apexTarget -Recurse -Force
        $apexRemoved++
    }
}
$apexOldPackage = [IO.Path]::GetFullPath((Join-Path $apexRoot 'artifacts/Apex-Desktop-Preview-x64.msix'))
if (-not $apexOldPackage.StartsWith($apexBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid old package path.' }
if (Test-Path -LiteralPath $apexOldPackage) { Remove-Item -LiteralPath $apexOldPackage -Force }
Write-Host ('Removed {0} generated directories. Source, current Apex builds, MSIX, SDK and user records are retained.' -f $apexRemoved)
if ($apexSkipped.Count) { Write-Warning ('In-use directories preserved: ' + ($apexSkipped -join ', ')) }
