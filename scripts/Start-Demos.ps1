[CmdletBinding()]
param(
    [ValidateSet('Aspire','Standalone')][string]$Mode = 'Aspire',
    [ValidateSet('None','Rest','Mcp','Rehearsal')][string]$Run = 'None',
    [switch]$Confirm,
    [switch]$Reset,
    [switch]$NoBuild,
    [switch]$OpenDashboard,
    [switch]$IncludeAgentDemo
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Local-Session.ps1')
Push-Location $root
$started = @()
$ownsSession = $false
$includeRehearsal = $IncludeAgentDemo -or $Run -ne 'None'
$servicePorts = @(5101,5102) + $(if ($includeRehearsal) { @(5103) } else { @() })
$environmentNames = @('DEMO_AUTH_KEY','DEMO_DB_PATH','DEMO_API_URL','DEMO_MCP_URL',
    'ASPNETCORE_URLS','ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL','ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL',
    'ASPIRE_ALLOW_UNSECURED_TRANSPORT','DOTNET_ENVIRONMENT','OTEL_EXPORTER_OTLP_ENDPOINT','DEMO_INCLUDE_AGENT_DEMO')
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    New-Item -ItemType Directory -Path '.local' -Force | Out-Null
    $pidFile = Join-Path $root '.local\processes.json'
    if (Test-Path $pidFile) { throw 'Existe .local\processes.json. Ejecuta scripts\Stop-Demos.ps1 antes de iniciar otra sesión.' }
    Protect-DemoDirectory (Join-Path $root '.local')
    $ownsSession = $true
    Remove-Item '.local\orders-session.json', '.local\aspire-login.url' -Force -ErrorAction SilentlyContinue
    foreach ($port in $servicePorts + $(if ($Mode -eq 'Aspire') { @(15888,18889,18890) } else { @() })) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
            throw "El puerto $port está ocupado. No se detendrá ningún proceso ajeno."
        }
    }
    if (-not $NoBuild) {
        dotnet build TechRiders.slnx --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
    }
    $env:DEMO_AUTH_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:DEMO_DB_PATH = Join-Path $root '.local\techriders.db'
    $env:DEMO_API_URL = 'http://127.0.0.1:5101'
    $env:DEMO_MCP_URL = 'http://127.0.0.1:5102/mcp'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:DEMO_INCLUDE_AGENT_DEMO = $includeRehearsal.ToString()
    $agent = Join-Path $root 'src\TechRiders.AgentDemo\bin\Debug\net10.0\TechRiders.AgentDemo.dll'
    if ($Reset) {
        dotnet $agent --reset --confirm
        if ($LASTEXITCODE -ne 0) { throw 'Falló el reset local.' }
    }
    function Start-OwnedProcess([string]$Role, [string]$Dll) {
        $process = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$Dll`"") -WorkingDirectory $root -PassThru `
            -RedirectStandardOutput (Join-Path $root ".local\$Role.stdout.log") `
            -RedirectStandardError (Join-Path $root ".local\$Role.stderr.log")
        [pscustomobject]@{ Id = $process.Id; StartTicks = $process.StartTime.ToUniversalTime().Ticks.ToString(); Role = $Role }
    }
    if ($Mode -eq 'Aspire') {
        $env:ASPNETCORE_URLS = 'http://127.0.0.1:15888'
        $env:ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL = 'http://127.0.0.1:18889'
        $env:ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL = 'http://127.0.0.1:18890'
        $env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = 'true'
        $started += Start-OwnedProcess 'apphost' (Join-Path $root 'src\TechRiders.AppHost\bin\Debug\net10.0\TechRiders.AppHost.dll')
    } else {
        $services = @(@('api','Api',5101), @('mcp','Mcp',5102))
        if ($includeRehearsal) { $services += ,@('agent','AgentDemo',5103) }
        foreach ($service in $services) {
            $env:ASPNETCORE_URLS = "http://127.0.0.1:$($service[2])"
            $started += Start-OwnedProcess $service[0] (Join-Path $root "src\TechRiders.$($service[1])\bin\Debug\net10.0\TechRiders.$($service[1]).dll")
        }
    }
    $started | ConvertTo-Json -AsArray | Set-Content -LiteralPath $pidFile -Encoding utf8
    foreach ($port in $servicePorts) {
        $ready = $false
        for ($attempt = 0; $attempt -lt 90; $attempt++) {
            foreach ($entry in $started) {
                if (-not (Get-Process -Id $entry.Id -ErrorAction SilentlyContinue)) {
                    throw "$($entry.Role) terminó antes de estar preparado. Revisa .local\$($entry.Role).stderr.log y stdout.log."
                }
            }
            try {
                $health = Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 2
                if ($health.status -eq 'Healthy') { $ready = $true; break }
            } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { throw "Timeout de salud en puerto $port. Consulta los logs de .local." }
    }
    $bearer = (dotnet $agent --token | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $bearer) { throw 'No se pudo emitir la identidad local.' }
    @{
        apiUrl = $env:DEMO_API_URL; mcpUrl = $env:DEMO_MCP_URL
        bearer = $bearer; expiresAt = [DateTimeOffset]::UtcNow.AddHours(4).AddSeconds(-5).ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath '.local\orders-session.json' -Encoding utf8
    Write-Host 'DEMO local preparada: Swagger http://127.0.0.1:5101/swagger · MCP http://127.0.0.1:5102/mcp'
    Write-Host 'Copilot: servidor orders configurado. Swagger: scripts\Get-DemoAccess.ps1 copia el bearer sin mostrarlo.'
    if ($Mode -eq 'Aspire') {
        $dashboard = Invoke-WebRequest 'http://127.0.0.1:15888' -TimeoutSec 10 -SkipHttpErrorCheck
        if ([int]$dashboard.StatusCode -ge 500) { throw 'Dashboard Aspire no disponible.' }
        $loginUrl = $null
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            $log = Get-Content '.local\apphost.stdout.log' -Raw
            $match = [regex]::Match($log, 'http://(?:127\.0\.0\.1|localhost):15888/login\?t=[^\s"<>]+')
            if ($match.Success) { $loginUrl = $match.Value; break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $loginUrl) { throw 'Aspire no publicó su enlace protegido /login?t=. Revisa .local\apphost.stdout.log.' }
        Set-Content -LiteralPath '.local\aspire-login.url' -Value $loginUrl -Encoding utf8
        Write-Host "Aspire — abre este enlace COMPLETO (privado y temporal; no hay contraseña que inventar):`n$loginUrl"
        if ($OpenDashboard) { Start-Process $loginUrl }
    }
    if ($Run -ne 'None') {
        Write-Host 'ENSAYO OPCIONAL DETERMINISTA — NO es Copilot ni usa un modelo.'
        if ($Mode -eq 'Aspire') {
            if ($Run -in @('Rest','Rehearsal')) {
                $rest = Invoke-RestMethod -Method Post 'http://127.0.0.1:5103/demo/rest' -ContentType 'application/json' -Body '{}'
                $rest | ConvertTo-Json -Depth 12
                if ($rest.status -ne 400) { throw 'No se reprodujo el fallo REST esperado.' }
            }
            if ($Run -in @('Mcp','Rehearsal')) {
                $review = Invoke-RestMethod -Method Post 'http://127.0.0.1:5103/demo/mcp/review' -ContentType 'application/json' -Body '{}'
                $review | ConvertTo-Json -Depth 12
                $confirmation = ''
                if ($Confirm) { $confirmation = "CANCELAR $($review.candidate.id)" }
                elseif ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
                    $confirmation = Read-Host "Escribe CANCELAR $($review.candidate.id) para confirmar; cualquier otra entrada deniega"
                }
                $body = @{runId=$review.runId;confirmation=$confirmation} | ConvertTo-Json
                if ($confirmation -eq "CANCELAR $($review.candidate.id)") {
                    $result = Invoke-RestMethod -Method Post 'http://127.0.0.1:5103/demo/mcp/confirm' -ContentType 'application/json' -Body $body
                    $result | ConvertTo-Json -Depth 12
                    if ($result.results[0].alreadyApplied -or -not $result.results[1].alreadyApplied) { throw 'Retry idempotente incorrecto.' }
                    Write-Host "TRACE_ID=$($result.traceId)"
                } else {
                    Invoke-RestMethod -Method Post 'http://127.0.0.1:5103/demo/mcp/deny' -ContentType 'application/json' -Body $body | ConvertTo-Json
                    Write-Host 'DENEGADO / SIN CONFIRMACIÓN: no se ha llamado a cancel_order.'
                }
            }
            Start-Sleep -Seconds 3
        } else {
            $flag = switch ($Run) { Rest {'--rest'} Mcp {'--mcp'} Rehearsal {'--rehearse'} }
            $cliArgs = @($agent, $flag)
            if ($Confirm) { $cliArgs += '--confirm' }
            dotnet @cliArgs
            if ($LASTEXITCODE -ne 0) { throw 'Falló la ejecución de la demo.' }
        }
    } else {
        if ($includeRehearsal) { Write-Host 'AgentDemo opcional (ensayo determinista, NO Copilot): http://127.0.0.1:5103' }
        Write-Host 'Ctrl+C o scripts\Stop-Demos.ps1 para detener. Reset: scripts\Reset-Demo.ps1 -Confirm'
        Wait-Process -Id $started[0].Id
    }
} finally {
    if ($ownsSession) {
        Remove-Item '.local\orders-session.json', '.local\aspire-login.url' -Force -ErrorAction SilentlyContinue
    }
    foreach ($entry in $started) {
        $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $entry.StartTicks) {
            $process.Kill($true)
            $process.WaitForExit(10000) | Out-Null
        }
    }
    if ($started.Count -gt 0) {
        # DCP children can take a moment to release sockets after the AppHost exits.
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            $remaining = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
                Where-Object { $_.LocalPort -in @(5101,5102,5103,15888,18889,18890) }
            if (-not $remaining) { break }
            Start-Sleep -Milliseconds 250
        }
        if ($remaining) { Write-Warning 'Algún puerto de demo aún está ocupado. No se detendrá ningún proceso no registrado.' }
        Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
    }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Pop-Location
}
