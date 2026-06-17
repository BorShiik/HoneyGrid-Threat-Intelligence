param(
    [string]$ResourceGroup = "hg-dev-rg"
)

$appsToPause = @(
    "hg-dev-ca-cowrie",
    "hg-dev-ca-tcp",
    "hg-dev-ca-web",
    "hg-dev-ca-ingestion"
)

Write-Host "⏸️  Ставим HoneyGrid на паузу (остановка потребления бюджета)..." -ForegroundColor Cyan

foreach ($app in $appsToPause) {
    Write-Host "Останавливаю $app..." -ForegroundColor Yellow
    az containerapp stop --name $app --resource-group $ResourceGroup --output none
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ $app остановлен!" -ForegroundColor Green
    } else {
        Write-Host "❌ Ошибка при остановке $app" -ForegroundColor Red
    }
}

Write-Host "😴 Все прожорливые контейнеры остановлены! Бюджет в безопасности." -ForegroundColor Cyan
