#Requires -Version 5.1
<#
.SYNOPSIS
  Tell installed MobiHymn web apps to re-download the hymn catalog.
.DESCRIPTION
  Use after editing hymn data on the hymn server (e.g. lyrics.db) when the server's
  catalogHash does not change on its own (metadata-only edits like metre/author/key).
  Publishes /catalog-policy.json to Firebase Hosting, reusing the last site build in
  artifacts\firebase-hosting (no rebuild). Installed apps check it on launch and when
  returning to the foreground (at most every 15 minutes).
.PARAMETER Mode
  optional  - "Hymn updates available" with Later / Update now (shown once)
  mandatory - "Hymn catalog update required" with only Update now (default)
.PARAMETER Message
  Optional custom text for the dialog, e.g. "Hymn 796 metre corrected."
.EXAMPLE
  .\tools\push-catalog-update.ps1 -Message "Hymn 796 metre corrected."
.EXAMPLE
  .\tools\push-catalog-update.ps1 -Mode optional
#>
param(
  [ValidateSet("optional", "mandatory")]
  [string]$Mode = "mandatory",
  [string]$Message = ""
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$staging = Join-Path $root "artifacts\firebase-hosting"
$liveOrigin = "https://mobihymn.web.app"

# Redeploying Hosting uploads the whole staging folder, so it must be the build that is live.
$stagedPolicyPath = Join-Path $staging "update-policy.json"
if (-not (Test-Path $stagedPolicyPath)) {
  throw "No staged site at $staging - run .\tools\deploy-web-hosting.ps1 first."
}
$stagedBuild = [string](Get-Content $stagedPolicyPath -Raw | ConvertFrom-Json).build
try {
  $liveBuild = [string](Invoke-RestMethod -Uri "$liveOrigin/update-policy.json?t=$([DateTime]::UtcNow.Ticks)" -TimeoutSec 15).build
} catch {
  throw "Could not read the live update-policy.json: $_"
}
if (-not $stagedBuild -or $stagedBuild -ne $liveBuild) {
  throw "Staged build '$stagedBuild' is not the live build '$liveBuild'. Run .\tools\deploy-web-hosting.ps1 (or -SkipPublish) first so this push doesn't roll the site back."
}

$id = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
$policy = [ordered]@{
  id         = $id
  mode       = $Mode
  message    = $Message
  pushedAt   = (Get-Date).ToUniversalTime().ToString("o")
}
[System.IO.File]::WriteAllText((Join-Path $staging "catalog-policy.json"), ($policy | ConvertTo-Json), (New-Object System.Text.UTF8Encoding $false))
Write-Host "Catalog policy: id=$id mode=$Mode"

npx --yes firebase-tools deploy --only hosting --project mobihymn
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Pushed. Installed apps will prompt on next launch / foreground."
