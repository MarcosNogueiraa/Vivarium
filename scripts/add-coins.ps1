# Adiciona fichas (moeda soft) a uma conta, pra teste.
# Requer a API rodando em Development (endpoint /api/dev/coins só existe lá).
#
# Uso:
#   .\scripts\add-coins.ps1 -User marco -Password trocar-esta-senha-123
#   .\scripts\add-coins.ps1 -User marco -Password ... -Amount 5000 -Api http://localhost:5258

param(
    [Parameter(Mandatory = $true)][string]$User,
    [Parameter(Mandatory = $true)][string]$Password,
    [int]$Amount = 1000,
    [string]$Api = "http://localhost:5258"
)

$ErrorActionPreference = "Stop"

$login = Invoke-RestMethod -Method Post "$Api/api/auth/login" -ContentType "application/json" `
    -Body (@{ usernameOrEmail = $User; password = $Password } | ConvertTo-Json)

$headers = @{ Authorization = "Bearer $($login.token)" }
$result = Invoke-RestMethod -Method Post "$Api/api/dev/coins?amount=$Amount" -Headers $headers

Write-Host "Creditado $($result.credited) fichas em '$User'. Saldo agora: $($result.balance) soft." -ForegroundColor Green
