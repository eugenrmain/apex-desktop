param([Parameter(Mandatory=$true)][string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$apexWorkspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$apexSource = [IO.Path]::GetFullPath($SourceDirectory)
$apexPrefix = $apexWorkspace + [IO.Path]::DirectorySeparatorChar
if (-not $apexSource.StartsWith($apexPrefix,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $apexSource 'Apex Desktop.dll'))) { throw 'Source must be a complete Apex build within the workspace.' }
$apexSourceVersion=[version]([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $apexSource "Apex Desktop.exe")).ProductVersion)
$apexFiles = @(Get-ChildItem -LiteralPath $apexSource -File -Recurse)
$apexTargets = @('artifacts/apex-portable','artifacts/apex-candidate','artifacts/apex-preview')
foreach ($apexRelativeTarget in $apexTargets) {
    $apexTarget = [IO.Path]::GetFullPath((Join-Path $apexWorkspace $apexRelativeTarget))
    if ($apexTarget.Equals($apexSource,[StringComparison]::OrdinalIgnoreCase)) { continue }
    if (-not $apexTarget.StartsWith($apexPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Launch target escapes the workspace.' }
    $apexProbe = $apexTarget
    while ($apexProbe -and $apexProbe.StartsWith($apexPrefix,[StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $apexProbe) -and ((Get-Item -LiteralPath $apexProbe).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw ('Refusing a linked launch directory: '+$apexProbe) }
        $apexProbe = Split-Path -Parent $apexProbe
    }
    $apexTargetExe=Join-Path $apexTarget 'Apex Desktop.exe'
    if((Test-Path -LiteralPath $apexTargetExe) -and [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($apexTargetExe).ProductVersion) -gt $apexSourceVersion) { Write-Warning ('Skipping newer launch copy: '+$apexTarget);continue }
    # Test loaded binaries before replacing any files. Never stop a running app.
    $apexHandles = [Collections.Generic.List[IDisposable]]::new()
    $apexBusy = $false
    try {
        if (Test-Path -LiteralPath $apexTarget) {
            foreach ($apexBinary in Get-ChildItem -LiteralPath $apexTarget -File -Recurse | Where-Object { $_.Extension -in @('.exe','.dll') }) {
                $apexHandles.Add([IO.File]::Open($apexBinary.FullName,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None))
            }
        }
    } catch { $apexBusy = $true; Write-Warning ('Could not refresh '+$apexTarget+'. Close Apex there and rerun this script. '+$_.Exception.Message) }
    finally { foreach ($apexHandle in $apexHandles) { $apexHandle.Dispose() } }
    if ($apexBusy) { continue }
    New-Item -ItemType Directory -Path $apexTarget -Force | Out-Null
    foreach ($apexFile in $apexFiles) {
        $apexRelativeFile = [IO.Path]::GetRelativePath($apexSource,$apexFile.FullName)
        $apexDestination = [IO.Path]::GetFullPath((Join-Path $apexTarget $apexRelativeFile))
        if (-not $apexDestination.StartsWith($apexTarget+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'File destination escapes the launch directory.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $apexDestination) -Force | Out-Null
        Copy-Item -LiteralPath $apexFile.FullName -Destination $apexDestination -Force
    }
    # The main app was renamed; leave helper binaries intact and remove only known obsolete main files.
    foreach ($apexObsolete in @('Apex.exe','Apex.dll','Apex.pdb','Apex.pri','Apex.deps.json','Apex.runtimeconfig.json')) {
        $apexObsoletePath = [IO.Path]::GetFullPath((Join-Path $apexTarget $apexObsolete))
        if (-not $apexObsoletePath.StartsWith($apexTarget+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Obsolete file escapes launch directory.' }
        if (Test-Path -LiteralPath $apexObsoletePath) { Remove-Item -LiteralPath $apexObsoletePath -Force }
    }
    Write-Host ('Updated launch copy: '+$apexTarget)
}
