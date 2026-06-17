$resourceGroup = "hg-dev-rg"
# Имя Function App берём из RG (суффикс зависит от подписки — не хардкодим).
$functionAppName = az functionapp list -g $resourceGroup --query "[0].name" -o tsv

Write-Host "=========================================" -ForegroundColor Cyan
$resourceGroup = "hg-dev-rg"
# Имя Function App берём из RG (суффикс зависит от подписки — не хардкодим).
$functionAppName = az functionapp list -g $resourceGroup --query "[0].name" -o tsv

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "🚀 DEPLOYING AZURE FUNCTION (AI & SIGNALR)" -ForegroundColor Cyan
Write-Host "   Target: $functionAppName" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# func publish сам собирает, пакует и разворачивает (.NET isolated). Работает и для
# Flex Consumption, и обходит баг legacy `config-zip` ("Invalid version 10.0" → тихий 503).
Push-Location src\HoneyGrid.Functions
npx func azure functionapp publish $functionAppName --dotnet-isolated
Pop-Location

Write-Host "GOTOWE! Funkcja dziala w Azure." -ForegroundColor Green
