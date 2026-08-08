#Requires -Version 5.1
<#
.SYNOPSIS
  Publish MobiHymn Blazor WASM and deploy to Firebase Hosting (+ API proxy functions).
#>
param(
  [switch]$HostingOnly,
  [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$staging = Join-Path $root "artifacts\firebase-hosting"
$webProj = Join-Path $root "MobiHymn4.Web\MobiHymn4.Web.csproj"
$assetlinksSrc = Join-Path $root "artifacts\pwa-android\assetlinks.json"

if (-not $SkipPublish) {
  Write-Host "Publishing Blazor WASM (Release)..."
  if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $staging | Out-Null
  dotnet publish $webProj -c Release -o (Join-Path $root "artifacts\web-publish") -nologo -v q
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

  $www = Join-Path $root "artifacts\web-publish\wwwroot"
  Copy-Item -Path (Join-Path $www "*") -Destination $staging -Recurse -Force
}

if (-not (Test-Path (Join-Path $staging "index.html"))) {
  throw "Missing published site at $staging - run without -SkipPublish."
}

# TWA Digital Asset Links
$wellKnown = Join-Path $staging ".well-known"
New-Item -ItemType Directory -Force -Path $wellKnown | Out-Null
if (Test-Path $assetlinksSrc) {
  Copy-Item $assetlinksSrc (Join-Path $wellKnown "assetlinks.json") -Force
} else {
  Write-Warning "assetlinks.json not found at $assetlinksSrc - TWA verification will fail until added."
}

# Ensure start_url is relative for Hosting
$manifestPath = Join-Path $staging "manifest.webmanifest"
if (Test-Path $manifestPath) {
  try {
    $man = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $man.start_url = "./"
    $man.id = "./"
    ($man | ConvertTo-Json -Depth 8) | Set-Content $manifestPath -Encoding utf8
  } catch {
    Write-Warning "Could not normalize manifest.webmanifest: $_"
  }
}

$targets = if ($HostingOnly) { "hosting" } else { "hosting,functions:hymnProxy,functions:midiProxy" }
Write-Host "Deploying Firebase ($targets)..."
npx --yes firebase-tools deploy --only $targets --project mobihymn
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Live: https://mobihymn.web.app"
Write-Host "Asset links: https://mobihymn.web.app/.well-known/assetlinks.json"
