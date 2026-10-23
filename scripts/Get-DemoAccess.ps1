[CmdletBinding()]
param(
    [ValidateSet('Swagger','Aspire')][string]$Target = 'Swagger',
    [switch]$Open
)
. (Join-Path $PSScriptRoot 'Local-Session.ps1')
$session = Read-DemoSession
if ($Target -eq 'Swagger') {
    Set-Clipboard -Value $session.bearer
    Write-Host 'Token temporal copiado al portapapeles. Pégalo en Authorize de Swagger (sin escribir Bearer). No lo pegues en Chat.'
    if ($Open) { Start-Process "$($session.apiUrl)/swagger" }
} else {
    $path = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\aspire-login.url'
    if (-not (Test-Path $path)) { throw 'Enlace no disponible: inicia en modo Aspire.' }
    $url = (Get-Content -LiteralPath $path -Raw).Trim()
    if ($url -notmatch '^http://(127\.0\.0\.1|localhost):15888/login\?t=[^\s]+$') { throw 'Enlace Aspire local no válido.' }
    Write-Host "Aspire (enlace completo, privado y temporal): $url"
    if ($Open) { Start-Process $url }
}
