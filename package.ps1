# Builds Release and zips a Thunderstore package into dist\. Upload the zip at
# https://thunderstore.io/c/lethal-company/create/, or pass -Upload to publish it with tcli
# (dotnet tool install -g tcli) using a team service account token in $env:TCLI_AUTH_TOKEN.
[CmdletBinding()]
param(
    [string]$Team = 'Andrew1431',
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'

# GitHub release v<version> on the current commit, with this version's CHANGELOG section as notes and the zip attached.
# Runs after a successful Thunderstore upload; a failure here only warns, since Thunderstore already has the version.
function Publish-GitHubRelease {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Write-Warning 'gh not found; skipped the GitHub release.'; return }
    $commit = git -C $root rev-parse HEAD
    if (-not (git -C $root branch -r --contains $commit)) { Write-Warning "HEAD isn't pushed; skipped the GitHub release. Push, then: gh release create v$version"; return }
    $changelog = Get-Content -Raw (Join-Path $ts 'CHANGELOG.md')
    $m = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($version))\s*$\s*(.*?)(?=^## |\z)")
    $notes = if ($m.Success) { $m.Groups[1].Value.Trim() } else { "CozyCats $version" }
    $notes += "`n`nAlso on Thunderstore: https://thunderstore.io/c/lethal-company/p/$Team/$($manifest.name)/"
    $notesFile = Join-Path $root 'dist\release-notes.md'
    Set-Content -LiteralPath $notesFile -Value $notes -Encoding utf8
    gh release create "v$version" $zip --repo "$Team/cozy-cats" --target $commit --title "CozyCats $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { Write-Warning "GitHub release failed; retry with: gh release create v$version" }
    Remove-Item -LiteralPath $notesFile -ErrorAction SilentlyContinue
}
$root = $PSScriptRoot
$ts = Join-Path $root 'thunderstore'

$manifest = Get-Content -Raw (Join-Path $ts 'manifest.json') | ConvertFrom-Json
$version = $manifest.version_number
[xml]$proj = Get-Content -Raw (Join-Path $root 'CozyCats\CozyCats.csproj')
$projVersion = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($projVersion -ne $version) { throw "manifest.json says $version but CozyCats.csproj <Version> is $projVersion. Bump both." }

$bundle = Join-Path $root 'unity\CozyCatsAssets\AssetBundles\cozycats'
if (-not (Test-Path $bundle)) { throw "Asset bundle missing at $bundle. Build it with Unity (BuildCat.Build) first." }

dotnet build (Join-Path $root 'CozyCats\CozyCats.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $root 'CozyCats\bin\Release\netstandard2.1\CozyCats.dll'
# Dev-only code (#if DEBUG) must never ship.
if ([IO.File]::ReadAllText($dll).Contains('DebugKeys')) { throw "CozyCats.dll contains debug keys; it wasn't built in Release." }

$staging = Join-Path $root 'dist\staging'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null

foreach ($f in 'manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md') {
    Copy-Item (Join-Path $ts $f) $staging
}
Copy-Item $dll, $bundle $staging

$zip = Join-Path $root "dist\$Team-$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging
Write-Host "Package ready: $zip"

if ($Upload) {
    if (-not $env:TCLI_AUTH_TOKEN) { throw 'Set $env:TCLI_AUTH_TOKEN to a Thunderstore service account token (team settings > Service Accounts).' }
    if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { throw 'tcli not found: dotnet tool install -g tcli' }
    $published = try { Invoke-RestMethod "https://thunderstore.io/api/experimental/package/$Team/$($manifest.name)/" } catch { $null }
    if ($published.latest.version_number -eq $version) { throw "$version is already on Thunderstore; versions can't be re-uploaded. Bump it." }
    tcli publish --file $zip --config-path (Join-Path $ts 'thunderstore.toml')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Uploaded ${version}: https://thunderstore.io/c/lethal-company/p/$Team/$($manifest.name)/"
    Publish-GitHubRelease
}
exit 0
