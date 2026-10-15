$ErrorActionPreference = 'Stop'
$apxRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$apxGitRoot = & git -C $apxRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or -not [IO.Path]::GetFullPath($apxGitRoot).Equals($apxRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Run this check in the ApexDesktop Git repository.' }
$apxCurrent = @(& git -c core.quotepath=false -C $apxRoot ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the source file list.' }
$apxObjects = @(& git -c core.quotepath=false -C $apxRoot rev-list --objects --all)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect repository history.' }
$apxHistory = @($apxObjects | Where-Object { $_ -match '^[a-f0-9]+ ' } | ForEach-Object { $_ -replace '^[a-f0-9]+ ', '' })
$apxFiles = @($apxCurrent + $apxHistory | Sort-Object -Unique)
$apxAllowedBinaries = @('Vendor/PresentMon.exe', 'Vendor/PawnIOSetup.exe')
$apxRejected = @()
foreach ($apxName in $apxFiles) {
    $apxPath = $apxName.Replace('\', '/')
    if ($apxPath -match '(?i)(^|/)(bin|obj|artifacts|dist|\.tools|\.vs)(/|$)' -or
        $apxPath -match '(?i)(^|/)ember[^/]*' -or
        ($apxPath -match '(?i)\.(exe|dll|pdb|msix|appx)$' -and $apxPath -notin $apxAllowedBinaries) -or
        $apxPath -match '(?i)\.(pfx|p12|pem|key|snk|publishsettings)$' -or
        ($apxPath -match '(?i)(^|/)\.env($|\.)' -and [IO.Path]::GetFileName($apxPath) -ne '.env.example')) {
        $apxRejected += $apxPath
    }
}
if ($apxRejected.Count) {
    throw ("Source check failed. Remove these generated, legacy-named or private files from the repository/index/history before pushing:`n" + ($apxRejected -join "`n"))
}
Write-Host ('Source check passed: {0} current source files; no obsolete build files or Ember-named paths in the selected files or Git history.' -f @($apxCurrent | Sort-Object -Unique).Count)
Write-Host 'The two pinned Vendor installers/tools are intentional. Legacy-data migration strings are intentional source code, not old build outputs.'
