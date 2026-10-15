param([switch]$Cancel)
$ErrorActionPreference = 'Stop'
$apxWorkspace = Split-Path -Parent $PSScriptRoot
$apxDotnet = Join-Path $apxWorkspace '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $apxDotnet)) { throw 'Run scripts/setup.ps1 first.' }
$env:DOTNET_CLI_HOME = Join-Path $apxWorkspace '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $apxWorkspace '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$apxOutput = Join-Path $apxWorkspace 'artifacts/network-check'
foreach ($apxProject in @('NetworkHelper/Apex.Network.csproj', 'scripts/NetworkCheck/Apex.NetworkCheck.csproj')) {
    & $apxDotnet publish (Join-Path $apxWorkspace $apxProject) -c Release -o $apxOutput
    if ($LASTEXITCODE -ne 0) { throw 'Network verification build failed.' }
}
$apxResult = Join-Path $apxOutput $(if ($Cancel) { 'cancellation-results.json' } else { 'sample-results.json' })
$apxArguments = '"{0}"' -f $apxResult
if ($Cancel) { $apxArguments += ' --cancel' }
Write-Host 'Approve the Apex network helper prompt. Only generated loopback traffic is used; no apps or files are changed.'
$apxProcess = Start-Process -FilePath (Join-Path $apxOutput 'Apex.NetworkCheck.exe') -ArgumentList $apxArguments -WindowStyle Hidden -PassThru -Wait
$apxChecks = Get-Content -LiteralPath $apxResult -Raw | ConvertFrom-Json
$apxChecks | ConvertTo-Json -Depth 5
if ($apxProcess.ExitCode -ne 0 -or -not $apxChecks.passed) { throw 'Network verification failed; inspect the result above.' }
