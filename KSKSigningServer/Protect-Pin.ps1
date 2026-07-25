param(
    [Parameter(Mandatory=$true)][string]$Pin,
    [string]$ServerUrl = "http://127.0.0.1:7443",
    [Parameter(Mandatory=$true)][string]$ApiKey
)
$headers = @{ "X-API-Key" = $ApiKey }
$body = @{ pin = $Pin } | ConvertTo-Json
$result = Invoke-RestMethod -Method Post -Uri "$ServerUrl/api/protect-pin" -Headers $headers -ContentType "application/json" -Body $body
Write-Host "EncryptedPin:" -ForegroundColor Green
Write-Output $result.encryptedPin
