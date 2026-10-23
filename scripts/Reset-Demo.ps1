[CmdletBinding()]
param([switch]$Confirm)
$ErrorActionPreference = 'Stop'
if (-not $Confirm) { throw 'Confirma expresamente con scripts\Reset-Demo.ps1 -Confirm. Solo datos sintéticos ORD-1001..1004 y claves demo:.' }
$root = Split-Path $PSScriptRoot -Parent
$oldKey = $env:DEMO_AUTH_KEY
$oldDb = $env:DEMO_DB_PATH
try {
    $env:DEMO_AUTH_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:DEMO_DB_PATH = Join-Path $root '.local\techriders.db'
    dotnet (Join-Path $root 'src\TechRiders.AgentDemo\bin\Debug\net10.0\TechRiders.AgentDemo.dll') --reset --confirm
    if ($LASTEXITCODE -ne 0) { throw 'Reset fallido. Compila primero la solución.' }
} finally {
    $env:DEMO_AUTH_KEY = $oldKey
    $env:DEMO_DB_PATH = $oldDb
}
