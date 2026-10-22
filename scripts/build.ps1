param([switch]$Package, [switch]$Verify, [switch]$Bounded, [string]$OutputDirectory = "artifacts/apx-release-1.0.0")
$ErrorActionPreference = 'Stop'
$apexRoot = Split-Path -Parent $PSScriptRoot
$apexDotnet = Join-Path $apexRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $apexDotnet)) {
    throw 'Local .NET SDK missing. Run scripts/setup.ps1 first.'
}
$env:DOTNET_CLI_HOME = Join-Path $apexRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $apexRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$apexOutput = [IO.Path]::GetFullPath((Join-Path $apexRoot $OutputDirectory))
if (-not $apexOutput.StartsWith($apexRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain in the Apex workspace.' }
$apexRelease = [IO.Path]::GetFullPath((Join-Path $apexRoot 'artifacts/apx-release-1.0.0'))
if (-not $apexOutput.Equals($apexRelease, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output must be artifacts/apx-release-1.0.0.' }
Get-Process -Name 'Apex Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
function Remove-ApexGeneratedDirectory([string]$relative) {
    $generatedRoot = [IO.Path]::GetFullPath($apexRoot) + [IO.Path]::DirectorySeparatorChar
    $generatedTarget = [IO.Path]::GetFullPath((Join-Path $apexRoot $relative))
    if (-not $generatedTarget.StartsWith($generatedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated directory escapes the Apex workspace.' }
    if (Test-Path -LiteralPath $generatedTarget) {
        if ((Get-Item -LiteralPath $generatedTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove a linked directory.' }
        Remove-Item -LiteralPath $generatedTarget -Recurse -Force
    }
}
$apexVendorPins = @{ 'PresentMon.exe' = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'; 'PawnIOSetup.exe' = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032' }
foreach ($apexVendor in $apexVendorPins.Keys) {
    $apexVendorPath = Join-Path $apexRoot ('Vendor/' + $apexVendor)
    if ((Get-FileHash -LiteralPath $apexVendorPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $apexVendorPins[$apexVendor]) { throw ('Bundled dependency checksum mismatch: ' + $apexVendor) }
}
if ((Get-AuthenticodeSignature -LiteralPath (Join-Path $apexRoot 'Vendor/PawnIOSetup.exe')).Status -ne 'Valid') { throw 'Sensor installer signature verification failed.' }

& $apexDotnet publish (Join-Path $apexRoot 'Apex.csproj') -c Release -p:Platform=x64 -p:BaseOutputPath=.tools/app-build/ -o $apexOutput
if ($LASTEXITCODE -ne 0) { throw 'Apex build failed.' }
foreach ($apexHelper in @('Recovery/Apex.Recovery.csproj', 'Sensors/Apex.Sensors.csproj', 'SettingsHelper/Apex.Settings.csproj', 'CaptureHelper/Apex.Capture.csproj', 'NetworkHelper/Apex.Network.csproj')) {
    $apexHelperOutput = Join-Path $apexRoot '.tools/helper-stage'
    Remove-ApexGeneratedDirectory '.tools/helper-stage'
    & $apexDotnet publish (Join-Path $apexRoot $apexHelper) -c Release -o $apexHelperOutput
    if ($LASTEXITCODE -ne 0) { throw ('Helper build failed: ' + $apexHelper) }
    foreach ($apexHelperFile in Get-ChildItem -LiteralPath $apexHelperOutput -File) {
        $apexSharedTarget = Join-Path $apexOutput $apexHelperFile.Name
        if ($apexHelperFile.Name.StartsWith([IO.Path]::GetFileNameWithoutExtension($apexHelper) + '.', [StringComparison]::OrdinalIgnoreCase)) { Copy-Item -LiteralPath $apexHelperFile.FullName -Destination $apexSharedTarget -Force }
        elseif (Test-Path -LiteralPath $apexSharedTarget) {
            if ((Get-FileHash -LiteralPath $apexHelperFile.FullName).Hash -ne (Get-FileHash -LiteralPath $apexSharedTarget).Hash) { throw ('Shared dependency mismatch: ' + $apexHelperFile.Name) }
        } else { Copy-Item -LiteralPath $apexHelperFile.FullName -Destination $apexSharedTarget }
    }
    foreach ($apexHelperDirectory in Get-ChildItem -LiteralPath $apexHelperOutput -Directory) {
        Copy-Item -LiteralPath $apexHelperDirectory.FullName -Destination $apexOutput -Recurse -Force
    }
    Remove-ApexGeneratedDirectory '.tools/helper-stage'
}
if ($Verify) {
    $apexVerification = Join-Path $apexRoot 'artifacts/apx-release-1.0.0/verification'
    $apexArguments = if ($Bounded) { '--verify-bounded "{0}"' -f $apexVerification } else { '--verify "{0}"' -f $apexVerification }
    $apexPreviousData = $env:APX_DATA_ROOT
    try {
        $env:APX_DATA_ROOT = Join-Path $apexVerification 'isolated-data'
        New-Item -ItemType Directory -Path $env:APX_DATA_ROOT -Force | Out-Null
        $apexProcess = Start-Process -FilePath (Join-Path $apexOutput 'Apex Desktop.exe') -ArgumentList $apexArguments -WindowStyle Hidden -PassThru -Wait
    } finally {
        $env:APX_DATA_ROOT = $apexPreviousData
    }
    $apexResults = Get-Content -LiteralPath (Join-Path $apexVerification 'results.json') -Raw | ConvertFrom-Json
    if ($apexProcess.ExitCode -ne 0 -or -not $apexResults.passed) { throw 'Verification failed. Inspect artifacts/apx-release-1.0.0/verification/results.json.' }
    Write-Host 'All screen verification checks passed.'
}
if ($Package) {
    $apexStage = Join-Path $apexRoot '.tools/package-stage'
    Remove-ApexGeneratedDirectory '.tools/package-stage'
    New-Item -ItemType Directory -Path $apexStage | Out-Null
    Copy-Item -Path (Join-Path $apexOutput '*') -Destination $apexStage -Recurse
    Copy-Item -LiteralPath (Join-Path $apexRoot 'Packaging/AppxManifest.xml') -Destination (Join-Path $apexStage 'AppxManifest.xml')
    $apexMakeAppx = Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $apexMakeAppx) { throw 'Windows SDK packaging tool missing.' }
    $apexPackageLog = Join-Path $apexRoot 'artifacts/package.log'
    & $apexMakeAppx.FullName pack /d $apexStage /p (Join-Path $apexRoot 'artifacts/Apex-Desktop-x64.msix') /o *> $apexPackageLog
    if ($LASTEXITCODE -ne 0) { throw 'MSIX validation or packaging failed.' }
    Remove-ApexGeneratedDirectory '.tools/package-stage'
    Write-Host 'Unsigned MSIX created. Store identity and signing must be configured before distribution.'
}
& (Join-Path $PSScriptRoot 'refresh-launch-copies.ps1') -SourceDirectory $apexOutput
Write-Host ('Launch: ' + (& (Join-Path $PSScriptRoot 'launch.ps1') -ResolveOnly))
