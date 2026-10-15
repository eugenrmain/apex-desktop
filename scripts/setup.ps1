$ErrorActionPreference = 'Stop'
$apexRoot = Split-Path -Parent $PSScriptRoot
$apexTools = Join-Path $apexRoot '.tools'
New-Item -ItemType Directory -Force -Path $apexTools | Out-Null
$apexInstaller = Join-Path $apexTools 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $apexInstaller
& $apexInstaller -Version '8.0.425' -InstallDir (Join-Path $apexTools 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw 'Official .NET SDK installation failed.' }
Write-Host 'Local SDK installed. Run scripts/build.ps1 next.'
