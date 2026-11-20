[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "extension\src"
$manifests = Join-Path $root "extension\manifests"
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $root "dist\extension"
} else {
    [IO.Path]::GetFullPath($OutputDirectory)
}

if (-not (Test-Path (Join-Path $source "background.js"))) {
    throw "Extension-Quellen fehlen: $source"
}

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

$targets = @(
    @{ Name = "chrome"; Manifest = "chrome.json"; Archive = "still-website-blocker-chrome.zip" },
    @{ Name = "firefox"; Manifest = "firefox.json"; Archive = "still-website-blocker-firefox.zip" }
)

foreach ($target in $targets) {
    $destination = Join-Path $output $target.Name
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item (Join-Path $source "*") $destination -Recurse -Force
    Copy-Item (Join-Path $manifests $target.Manifest) (Join-Path $destination "manifest.json") -Force
    $archive = Join-Path $output $target.Archive
    Compress-Archive -Path (Join-Path $destination "*") -DestinationPath $archive -Force
    Write-Output "Built $destination"
    Write-Output "Built $archive"
}
