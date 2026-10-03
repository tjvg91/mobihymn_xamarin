#Requires -Version 5.1
<#
.SYNOPSIS
  Publish MobiHymn Blazor WASM and deploy to Firebase Hosting (+ API proxy functions).
.PARAMETER HostingOnly
  Deploy Hosting plus the share-link functions (skips the hymn/MIDI proxy functions).
.PARAMETER SiteOnly
  Deploy Hosting only - no Cloud Functions at all.
.PARAMETER SkipPublish
  Reuse the existing artifacts\firebase-hosting build instead of publishing again.
.PARAMETER Update
  How installed apps pick up this deploy:
    auto      - reload silently when the app next comes to the foreground (default)
    optional  - show "Update available" with Later / Update now
    mandatory - block the app with "Update required" until the user updates
  A mandatory deploy stays mandatory for anyone still older than it, even after
  later auto/optional deploys.
.PARAMETER UpdateMessage
  Optional custom text for the update dialog.
.EXAMPLE
  .\tools\deploy-web-hosting.ps1 -Update mandatory -UpdateMessage "Fixes board sync. Please update."
#>
param(
  [switch]$HostingOnly,
  [switch]$SiteOnly,
  [switch]$SkipPublish,
  [ValidateSet("auto", "optional", "mandatory")]
  [string]$Update = "auto",
  [string]$UpdateMessage = ""
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$staging = Join-Path $root "artifacts\firebase-hosting"
$webProj = Join-Path $root "MobiHymn4.Web\MobiHymn4.Web.csproj"
$assetlinksSrc = Join-Path $root "artifacts\pwa-android\assetlinks.json"
$publishDir = Join-Path $root "artifacts\web-publish"
$buildIdFile = Join-Path $publishDir "build-id.txt"
$liveOrigin = "https://mobihymn.web.app"

if (-not $SkipPublish) {
  # Sortable UTC stamp; the app compares its own stamp against update-policy.json.
  $buildId = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
  Write-Host "Publishing Blazor WASM (Release, build $buildId)..."
  if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $staging | Out-Null
  dotnet publish $webProj -c Release -o $publishDir -nologo -v q "-p:MobiHymnBuildId=$buildId"
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Set-Content -Path $buildIdFile -Value $buildId -NoNewline

  $www = Join-Path $publishDir "wwwroot"
  Copy-Item -Path (Join-Path $www "*") -Destination $staging -Recurse -Force
} else {
  $buildId = if (Test-Path $buildIdFile) { (Get-Content $buildIdFile -Raw).Trim() } else { "" }
  if (-not $buildId) { Write-Warning "No build id from the previous publish - mandatory escalation disabled for this deploy." }
}

if (-not (Test-Path (Join-Path $staging "index.html"))) {
  throw "Missing published site at $staging - run without -SkipPublish."
}

# Update policy read by installed apps when a new version is waiting.
$prevMandatorySince = ""
try {
  $live = Invoke-RestMethod -Uri "$liveOrigin/update-policy.json?t=$([DateTime]::UtcNow.Ticks)" -TimeoutSec 15
  if ($live.mandatorySince) { $prevMandatorySince = [string]$live.mandatorySince }
} catch {
  Write-Host "No live update-policy.json yet (first policy deploy)."
}
$mandatorySince = if ($Update -eq "mandatory" -and $buildId) { $buildId } else { $prevMandatorySince }

$appVersion = ""
$releaseHistory = Join-Path $root "MobiHymn4.Shared\ReleaseHistory.cs"
if (Test-Path $releaseHistory) {
  $m = Select-String -Path $releaseHistory -Pattern 'CurrentVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
  if ($m) { $appVersion = $m.Matches[0].Groups[1].Value }
}

$policy = [ordered]@{
  build          = $buildId
  version        = $appVersion
  mode           = $Update
  mandatorySince = $mandatorySince
  message        = $UpdateMessage
  deployedAt     = (Get-Date).ToUniversalTime().ToString("o")
}
$policyJson = $policy | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $staging "update-policy.json"), $policyJson, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Update policy: mode=$Update build=$buildId mandatorySince=$(if ($mandatorySince) { $mandatorySince } else { '(none)' })"

# Carry the live catalog push (tools/push-catalog-update.ps1) across site deploys.
$catalogPolicyPath = Join-Path $staging "catalog-policy.json"
try {
  $livePolicy = Invoke-RestMethod -Uri "$liveOrigin/catalog-policy.json?t=$([DateTime]::UtcNow.Ticks)" -TimeoutSec 15
  if ($livePolicy.id) {
    [System.IO.File]::WriteAllText($catalogPolicyPath, ($livePolicy | ConvertTo-Json), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Catalog policy carried over: id=$($livePolicy.id) mode=$($livePolicy.mode)"
  }
} catch {
  # None published yet.
}

# TWA Digital Asset Links
$wellKnown = Join-Path $staging ".well-known"
New-Item -ItemType Directory -Force -Path $wellKnown | Out-Null
if (Test-Path $assetlinksSrc) {
  Copy-Item $assetlinksSrc (Join-Path $wellKnown "assetlinks.json") -Force
} else {
  Write-Warning "assetlinks.json not found at $assetlinksSrc - TWA verification will fail until added."
}

$targets = if ($SiteOnly) { "hosting" } elseif ($HostingOnly) { "hosting,functions:hymnShare,functions:hymnShareOg" } else { "hosting,functions:hymnProxy,functions:midiProxy,functions:hymnPdf,functions:hymnShare,functions:hymnShareOg" }
Write-Host "Deploying Firebase ($targets)..."
npx --yes firebase-tools deploy --only $targets --project mobihymn
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Live: https://mobihymn.web.app"
Write-Host "Asset links: https://mobihymn.web.app/.well-known/assetlinks.json"
