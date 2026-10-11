# Closes every running Lethal Company like clicking the window's X, so settings and saves finish writing.
# Only games still running after -TimeoutSeconds are force-killed.
param([int]$TimeoutSeconds = 10)

$games = @(Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue)
if ($games.Count -eq 0) { exit 0 }
foreach ($g in $games) { [void]$g.CloseMainWindow() }
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline -and @(Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue).Count -gt 0) {
    Start-Sleep -Milliseconds 250
}
$left = @(Get-Process -Name 'Lethal Company' -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) {
    Write-Warning "$($left.Count) game(s) didn't close in ${TimeoutSeconds}s; force-killing."
    $left | Stop-Process -Force
}
# The DLL stays locked for a moment after the process goes.
Start-Sleep -Milliseconds 500
