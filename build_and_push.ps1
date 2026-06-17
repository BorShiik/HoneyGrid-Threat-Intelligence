# Имя ACR берём из группы ресурсов — детерминированный суффикс зависит от подписки,
# поэтому не хардкодим (у разных подписок/участников команды он разный).
$ResourceGroup = "hg-dev-rg"
$AcrName  = az acr list -g $ResourceGroup --query "[0].name" -o tsv
$Registry = az acr list -g $ResourceGroup --query "[0].loginServer" -o tsv

Write-Host "Logowanie do Azure Container Registry ($Registry)..." -ForegroundColor Cyan
az acr login -n $AcrName

Write-Host "1. Budowanie i wysylanie: honeygrid-cowrie" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-cowrie:latest -f sensors/cowrie/Dockerfile sensors/cowrie
docker push $Registry/honeygrid-cowrie:latest
Write-Host "   -> Aktualizacja kontenera hg-dev-ca-cowrie..."
az containerapp revision copy -n hg-dev-ca-cowrie -g $ResourceGroup

Write-Host "2. Budowanie i wysylanie: honeygrid-cowrie-shipper" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-cowrie-shipper:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/Dockerfile .
docker push $Registry/honeygrid-cowrie-shipper:latest
# Cowrie shipper runs inside the cowrie container app, so updating cowrie app updates shipper too.

Write-Host "3. Budowanie i wysylanie: honeygrid-tcp" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-tcp:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/Dockerfile .
docker push $Registry/honeygrid-tcp:latest
Write-Host "   -> Aktualizacja kontenera hg-dev-ca-tcp..."
az containerapp revision copy -n hg-dev-ca-tcp -g $ResourceGroup

Write-Host "4. Budowanie i wysylanie: honeygrid-web" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-web:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/Dockerfile .
docker push $Registry/honeygrid-web:latest
Write-Host "   -> Aktualizacja kontenera hg-dev-ca-web..."
az containerapp revision copy -n hg-dev-ca-web -g $ResourceGroup

Write-Host "5. Budowanie i wysylanie: honeygrid-ingestion" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-ingestion:latest -f src/HoneyGrid.Ingestion/Dockerfile .
docker push $Registry/honeygrid-ingestion:latest
Write-Host "   -> Aktualizacja kontenera hg-dev-ca-ingestion..."
az containerapp revision copy -n hg-dev-ca-ingestion -g $ResourceGroup

Write-Host "6. Budowanie i wysylanie: honeygrid-api" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-api:latest -f src/HoneyGrid.Api/Dockerfile .
docker push $Registry/honeygrid-api:latest
Write-Host "   -> Aktualizacja kontenera hg-dev-ca-api..."
az containerapp revision copy -n hg-dev-ca-api -g $ResourceGroup

Write-Host "7. Budowanie i wysylanie: honeygrid-node-metrics" -ForegroundColor Cyan
docker build -t $Registry/honeygrid-node-metrics:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/Dockerfile .
docker push $Registry/honeygrid-node-metrics:latest

Write-Host "8. Wdrażanie: Azure Function (AI & SignalR)" -ForegroundColor Cyan
$functionAppName = az functionapp list -g $ResourceGroup --query "[0].name" -o tsv
Write-Host "   Target: $functionAppName"
Push-Location src\HoneyGrid.Functions
npx func azure functionapp publish $functionAppName --dotnet-isolated
Pop-Location

Write-Host "GOTOWE! Wszystkie obrazy wyslane, kontenery zaktualizowane i funkcja wdrożona." -ForegroundColor Green
