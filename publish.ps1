# Builds ASMForge for sharing: a stand-alone folder, a .zip of it, and an installer.
#
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#
# Output in publish\:
#   ASMForge-<version>-win-x64\ASMForge.App.exe   the app (runs without Visual Studio or an installed .NET)
#   ASMForge-<version>-win-x64.zip                that folder, zipped
#   ASMForge-Setup-<version>.exe                  installer (needs Inno Setup 6 on this PC to build;
#                                                 the person installing does not need it)
# The app is a folder rather than a single .exe on purpose: running C# files inside ASMForge needs the .NET
# libraries as real files on disk, which a single-file build would hide inside the .exe.
param([string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$publishDir = Join-Path $root "publish"
$name = "ASMForge-$version-$Runtime"
$out = Join-Path $publishDir $name
$zip = "$out.zip"
$setup = Join-Path $publishDir "ASMForge-Setup-$version.exe"

foreach ($path in $out, $zip, $setup) { if (Test-Path $path) { Remove-Item $path -Recurse -Force } }

# 1. Self-contained app folder.
dotnet publish (Join-Path $root "src\ASMForge.App\ASMForge.App.csproj") -c Release -r $Runtime --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 2. Zip.
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

# 3. Installer (Inno Setup).
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }

if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$out" "/DOutputDir=$publishDir" (Join-Path $root "installer\ASMForge.iss")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
    Write-Warning "Inno Setup 6 was not found, so no installer was built. Install it with: winget install JRSoftware.InnoSetup"
}

Write-Host ""
Write-Host "ASMForge $version is ready in $publishDir"
Write-Host "  Run:     $name\ASMForge.App.exe"
Write-Host "  Zip:     $name.zip"
if (Test-Path $setup) { Write-Host "  Install: ASMForge-Setup-$version.exe   <- send this one" }
