[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
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
    @{ Name = "chromium"; Manifest = "chrome.json"; Archive = "still-website-blocker-chromium.zip" },
    @{ Name = "firefox"; Manifest = "firefox.json"; Archive = "still-website-blocker-firefox.xpi" }
)

foreach ($target in $targets) {
    $destination = Join-Path $output $target.Name
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item (Join-Path $source "*") $destination -Recurse -Force
    Copy-Item (Join-Path $manifests $target.Manifest) (Join-Path $destination "manifest.json") -Force
    $archive = Join-Path $output $target.Archive
    if ([IO.Path]::GetExtension($archive) -eq ".xpi") {
        # Compress-Archive only accepts .zip. An XPI is a normal ZIP archive.
        [IO.Compression.ZipFile]::CreateFromDirectory($destination, $archive)
    } else {
        Compress-Archive -Path (Join-Path $destination "*") -DestinationPath $archive -Force
    }
    Write-Output "Built $destination"
    Write-Output "Built $archive"
}
