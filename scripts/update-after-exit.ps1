param([Parameter(Mandatory=$true)][string]$SourceDirectory)
$ErrorActionPreference='Stop'
$apexWorkspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$apexSource=[IO.Path]::GetFullPath($SourceDirectory)
$apexTargets=@('artifacts/apex-portable','artifacts/apex-candidate','artifacts/apex-preview') | ForEach-Object { Join-Path $apexWorkspace $_ }
if(-not $apexSource.StartsWith($apexWorkspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source escapes workspace.' }
$apexExpected=(Get-FileHash -LiteralPath (Join-Path $apexSource 'Apex Desktop.dll')).Hash
$apexNeedsUpdate=@($apexTargets | Where-Object {(Get-FileHash -LiteralPath (Join-Path $_ 'Apex Desktop.dll')).Hash -ne $apexExpected})
if($apexNeedsUpdate.Count -eq 0) { exit 0 }
$apexDeadline=[DateTime]::UtcNow.AddMinutes(30)
$apexLog=Join-Path $apexWorkspace 'artifacts/pending-update.log'
while([DateTime]::UtcNow -lt $apexDeadline) {
    $apexHandles=[Collections.Generic.List[IDisposable]]::new(); $apexReady=$true
    try {
        foreach($apexBinary in $apexNeedsUpdate | ForEach-Object {Get-ChildItem -LiteralPath $_ -File -Recurse} | Where-Object {$_.Extension -in @('.dll','.exe')}) {
            $apexHandles.Add([IO.File]::Open($apexBinary.FullName,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None))
        }
    } catch { $apexReady=$false }
    finally { foreach($apexHandle in $apexHandles) {$apexHandle.Dispose()} }
    if($apexReady) {
        & (Join-Path $PSScriptRoot 'refresh-launch-copies.ps1') -SourceDirectory $apexSource *> $apexLog
        if(@($apexTargets | Where-Object {(Get-FileHash -LiteralPath (Join-Path $_ 'Apex Desktop.dll')).Hash -ne $apexExpected}).Count -eq 0) { Add-Content -LiteralPath $apexLog -Value 'All launch copies updated.'; exit 0 }
    }
    Start-Sleep -Seconds 2
}
'Launch-copy update still needs Apex to close. Run refresh-launch-copies.ps1 again.' | Set-Content -LiteralPath $apexLog
exit 1
