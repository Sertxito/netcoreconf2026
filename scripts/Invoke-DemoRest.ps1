[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path
)
. (Join-Path $PSScriptRoot 'Local-Session.ps1')
$session = Read-DemoSession
if ($Path -notmatch '^/(orders(?:[/?]|$)|capabilities/|swagger/v1/swagger\.json$)' -or
    $Path -match '[\\#\r\n]' -or $Path.Contains('..')) {
    throw 'Usa una ruta relativa de orders, capabilities o swagger; este helper solo permite GET.'
}
$traceId = [guid]::NewGuid().ToString('N')
$spanId = [guid]::NewGuid().ToString('N').Substring(0,16)
$response = Invoke-WebRequest -Uri "$($session.apiUrl)$Path" -Method Get -TimeoutSec 30 `
    -Headers @{ Authorization = "Bearer $($session.bearer)"; traceparent = "00-$traceId-$spanId-01" } `
    -MaximumRedirection 0 -SkipHttpErrorCheck
$content = if ($response.Content -is [byte[]]) {
    [Text.Encoding]::UTF8.GetString($response.Content)
} else { $response.Content }
[pscustomobject]@{
    method = 'GET'
    path = $Path
    status = [int]$response.StatusCode
    traceId = $traceId
    body = $content | ConvertFrom-Json
} | ConvertTo-Json -Depth 40
