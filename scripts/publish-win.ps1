[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "dist\$Runtime"
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

Write-Host "Still wird für $Runtime veröffentlicht ..." -ForegroundColor Cyan

dotnet publish (Join-Path $root "src\Still.App\Still.App.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained $selfContainedValue `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "Die App konnte nicht veröffentlicht werden."
}

dotnet publish (Join-Path $root "src\Still.Watchdog\Still.Watchdog.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained $selfContainedValue `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "Der Watchdog konnte nicht veröffentlicht werden."
}

Write-Host ""
Write-Host "Fertig: $output" -ForegroundColor Green
Write-Host "Starte Still.App.exe als Administrator, damit Websites gesperrt und der geschützte Autostart eingerichtet werden können."
