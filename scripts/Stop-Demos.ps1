[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pidFile = Join-Path $root '.local\processes.json'
Remove-Item (Join-Path $root '.local\orders-session.json'), (Join-Path $root '.local\aspire-login.url') -Force -ErrorAction SilentlyContinue
if (-not (Test-Path $pidFile)) { Write-Host 'No hay procesos registrados por el launcher.'; return }
$entries = @(Get-Content $pidFile -Raw | ConvertFrom-Json)
foreach ($entry in $entries) {
    $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $entry.StartTicks) {
        Write-Host "Deteniendo $($entry.Role) PID $($entry.Id)"
        $process.Kill($true)
        $process.WaitForExit(10000) | Out-Null
    }
}
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $remaining = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in @(5101,5102,5103,15888,18889,18890) }
    if (-not $remaining) { break }
    Start-Sleep -Milliseconds 250
}
if ($remaining) { Write-Warning 'Un puerto de demo continúa ocupado; no se detienen procesos ajenos.' }
# The waiting launcher also removes its registration during shutdown.
Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
