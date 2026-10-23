$ErrorActionPreference = 'Stop'

function Read-DemoSession {
    $path = Join-Path (Split-Path $PSScriptRoot -Parent) '.local\orders-session.json'
    if (-not (Test-Path $path)) { throw 'Primero ejecuta scripts\Start-Demos.ps1.' }
    $session = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ([DateTimeOffset]$session.expiresAt -le [DateTimeOffset]::UtcNow) { throw 'Sesión caducada: reinicia Start-Demos.ps1.' }
    foreach ($value in @($session.apiUrl, $session.mcpUrl)) {
        $uri = [uri]$value
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'http' -or $uri.Host -ne '127.0.0.1' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
            throw 'Las credenciales de demo solo se envían a HTTP loopback.'
        }
    }
    return $session
}

function Protect-DemoDirectory([string]$Path) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls $Path /inheritance:r /grant:r "${identity}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo proteger el directorio local de credenciales.' }
}
