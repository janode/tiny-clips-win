<#
.SYNOPSIS
    Builds TinyClips from an ASCII-safe path to work around a XAML compiler bug
    with Unicode characters in file paths.

.DESCRIPTION
    The WinUI 3 XAML compiler (XamlCompiler.exe, net472) crashes silently when
    XAML source files are under a path containing non-ASCII characters.
    This script copies the project to C:\BuildTest and builds from there.

.PARAMETER Platform
    Target platform: x64 or arm64 (default: arm64)

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Debug)

.PARAMETER Run
    If set, runs the app after a successful build.
#>
param(
    [ValidateSet("x64", "arm64")]
    [string]$Platform = "arm64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$Run
)

$ErrorActionPreference = "Stop"
$buildRoot = "C:\BuildTest"
$sourceDir = "$PSScriptRoot\TinyClips"
$buildDir  = "$buildRoot\TinyClips"

Write-Host "=== TinyClips Build ===" -ForegroundColor Cyan
Write-Host "Platform: $Platform | Configuration: $Configuration"

# Clean and copy
if (Test-Path $buildRoot) { Remove-Item $buildRoot -Recurse -Force }
Copy-Item $sourceDir $buildDir -Recurse

# Copy global.json if present
$globalJson = Join-Path $PSScriptRoot "global.json"
if (Test-Path $globalJson) { Copy-Item $globalJson $buildRoot }

Write-Host "Copied sources to $buildDir" -ForegroundColor Green

# Build
$csproj = "$buildDir\TinyClips.csproj"
$rid = "win-$Platform"
dotnet build $csproj -c $Configuration -r $rid

if ($LASTEXITCODE -eq 0) {
    $outputDir = "$buildDir\bin\$Configuration\net10.0-windows10.0.22621.0\$rid"
    Write-Host "`nBuild succeeded! Output: $outputDir" -ForegroundColor Green

    # Copy output back to source tree for convenience
    $localOutput = "$sourceDir\bin\$Configuration\$rid"
    if (-not (Test-Path $localOutput)) { New-Item -ItemType Directory -Path $localOutput -Force | Out-Null }
    Copy-Item "$outputDir\*" $localOutput -Recurse -Force
    Write-Host "Output copied to: $localOutput" -ForegroundColor DarkGreen

    if ($Run) {
        $exe = "$outputDir\TinyClips.exe"
        if (Test-Path $exe) {
            Write-Host "Launching $exe..." -ForegroundColor Yellow
            Start-Process $exe
        } else {
            Write-Host "Executable not found at $exe" -ForegroundColor Red
        }
    }
} else {
    Write-Host "`nBuild FAILED." -ForegroundColor Red
    exit 1
}
