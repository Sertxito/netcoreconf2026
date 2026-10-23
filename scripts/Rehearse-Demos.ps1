[CmdletBinding()]
param([ValidateSet('Aspire','Standalone')][string]$Mode = 'Standalone', [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$watch = [Diagnostics.Stopwatch]::StartNew()
# This dedicated command explicitly opts into resetting and cancelling synthetic demo data.
& "$PSScriptRoot\Start-Demos.ps1" -Mode $Mode -Run Rehearsal -Reset -Confirm -NoBuild:$NoBuild
if ($LASTEXITCODE -ne 0) { throw 'El ensayo no ha terminado correctamente.' }
Write-Host ("Ensayo offline correcto en {0:N1}s. Incluye 400 REST, MCP real y cancelación confirmada con retry idempotente." -f $watch.Elapsed.TotalSeconds)
