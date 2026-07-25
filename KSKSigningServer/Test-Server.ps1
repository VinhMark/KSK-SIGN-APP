param(
    [string]$ServerUrl = "http://127.0.0.1:7443",
    [Parameter(Mandatory=$true)][string]$ApiKey
)
$headers = @{ "X-API-Key" = $ApiKey }
Invoke-RestMethod -Method Get -Uri "$ServerUrl/api/status" -Headers $headers | ConvertTo-Json -Depth 8
Invoke-RestMethod -Method Post -Uri "$ServerUrl/api/test-token" -Headers $headers | ConvertTo-Json -Depth 8
