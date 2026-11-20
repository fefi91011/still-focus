[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root "artifacts\installer-staging"
$installerOutput = Join-Path $root "dist\installer"
$iss = Join-Path $root "installer\Still.iss"

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 fehlt. Installiere es z. B. mit: winget install --id JRSoftware.InnoSetup -e"
}

if (Test-Path $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

Write-Host "Still wird selbststaendig für $Runtime veröffentlicht ..." -ForegroundColor Cyan
& $dotnet publish (Join-Path $root "src\Still.App\Still.App.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false `
    -o $staging
if ($LASTEXITCODE -ne 0) {
    throw "Die App konnte nicht veröffentlicht werden."
}

& $dotnet publish (Join-Path $root "src\Still.Watchdog\Still.Watchdog.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false `
    -o $staging
if ($LASTEXITCODE -ne 0) {
    throw "Der Watchdog konnte nicht veröffentlicht werden."
}

Write-Host "Installer wird kompiliert ..." -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup konnte den Installer nicht erstellen."
}

$setup = Join-Path $installerOutput "StillSetup-0.1.0.exe"
if (-not (Test-Path $setup)) {
    throw "Der Installer wurde nicht gefunden: $setup"
}

Write-Host ""
Write-Host "Fertig: $setup" -ForegroundColor Green
Write-Host "Doppelklick -> installieren. Die .NET-Runtime ist enthalten."
