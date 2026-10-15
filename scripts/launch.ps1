param([switch]$ResolveOnly)
$ErrorActionPreference='Stop'
$apexWorkspace=Split-Path -Parent $PSScriptRoot
$apexRank=0
$apexChoices=foreach($apexFolder in @('artifacts/apex-portable','artifacts/apex-candidate','artifacts/apex-preview','artifacts/apex-release')) {
    $apexRank++
    $apexExecutable=Join-Path $apexWorkspace ($apexFolder+'/Apex Desktop.exe')
    if((Test-Path -LiteralPath $apexExecutable) -and (Test-Path -LiteralPath (Join-Path (Split-Path $apexExecutable) 'Apex Desktop.dll'))) {
        [pscustomobject]@{Path=$apexExecutable;Version=[version]([Diagnostics.FileVersionInfo]::GetVersionInfo($apexExecutable).ProductVersion);Order=$apexRank}
    }
}
$apexLatest=$apexChoices | Sort-Object @{Expression='Version';Descending=$true},@{Expression='Order';Descending=$false} | Select-Object -First 1
if(-not $apexLatest){throw 'Build Apex first.'}
if($ResolveOnly){$apexLatest.Path;exit}
Start-Process -FilePath $apexLatest.Path -WorkingDirectory (Split-Path $apexLatest.Path)
