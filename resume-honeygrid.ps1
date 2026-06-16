param(
    [string]$ResourceGroup = "hg-dev-rg"
)

$appsToStart = @(
    "hg-dev-ca-cowrie",
    "hg-dev-ca-tcp",
    "hg-dev-ca-web",
    "hg-dev-ca-ingestion"
)

Write-Host "▶️  Запускаем HoneyGrid (возвращаем реплики)..." -ForegroundColor Cyan

foreach ($app in $appsToStart) {
    Write-Host "Запускаю $app..." -ForegroundColor Yellow
    az containerapp update --name $app --resource-group $ResourceGroup --min-replicas 1 --max-replicas 1 --output none
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ $app запущен!" -ForegroundColor Green
    } else {
        Write-Host "❌ Ошибка при запуске $app" -ForegroundColor Red
    }
}

Write-Host "🚀 Все контейнеры снова в строю! Ждем хакеров." -ForegroundColor Cyan
