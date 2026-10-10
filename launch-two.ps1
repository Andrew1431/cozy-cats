# Starts a LAN host plus clients on this PC, windowed side by side, using the CatDev profile.
# Debug builds of CozyCats skip the menus (DevLaunch.cs); -Manual leaves the menus to you.
param(
    [int]$Count = 2,
    [string]$Profile = 'CatDev',
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Lethal Company',
    [switch]$Manual
)

$preloader = Join-Path $env:APPDATA "r2modmanPlus-local\LethalCompany\profiles\$Profile\BepInEx\core\BepInEx.Preloader.dll"
if (-not (Test-Path -LiteralPath $preloader)) { throw "No BepInEx in profile '$Profile' ($preloader)" }
$exe = Join-Path $GameDir 'Lethal Company.exe'

for ($i = 0; $i -lt $Count; $i++) {
    $gameArgs = @('--doorstop-enabled', 'true', '--doorstop-target-assembly', "`"$preloader`"", '--cozy-slot', $i)
    if (-not $Manual) { $gameArgs += @('--cozy-dev', $(if ($i -eq 0) { 'host' } else { 'join' })) }
    Start-Process -FilePath $exe -WorkingDirectory $GameDir -ArgumentList $gameArgs
    Start-Sleep -Seconds 3
}
