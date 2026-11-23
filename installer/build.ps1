# Publishes the self-contained single-file Island.exe and wraps it in an Inno Setup installer.
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 1.2.3]
# Output: publish\win-x64\Island.exe and publish\installer\Island-Setup-<version>.exe
# Needs Inno Setup 6 (https://jrsoftware.org/isinfo.php, or: winget install JRSoftware.InnoSetup).
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\Island\Island.csproj'

if (-not $Version) {
    [xml]$xml = Get-Content $csproj
    $Version = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No <Version> in $csproj; pass -Version." }
}

dotnet publish $csproj -p:PublishProfile=SingleFile "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'Island.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

Get-Item (Join-Path $root "publish\installer\Island-Setup-$Version.exe")
