# Имя ACR берём из группы ресурсов — детерминированный суффикс зависит от подписки,
# поэтому не хардкодим (у разных подписок/участников команды он разный).
$ResourceGroup = "hg-dev-rg"
$AcrName  = az acr list -g $ResourceGroup --query "[0].name" -o tsv
$Registry = az acr list -g $ResourceGroup --query "[0].loginServer" -o tsv

Write-Host "Logowanie do Azure Container Registry ($Registry)..."
az acr login -n $AcrName

Write-Host "1. Budowanie i wysylanie: honeygrid-cowrie"
docker build -t $Registry/honeygrid-cowrie:latest -f sensors/cowrie/Dockerfile sensors/cowrie
docker push $Registry/honeygrid-cowrie:latest

Write-Host "2. Budowanie i wysylanie: honeygrid-cowrie-shipper"
docker build -t $Registry/honeygrid-cowrie-shipper:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/Dockerfile .
docker push $Registry/honeygrid-cowrie-shipper:latest

Write-Host "3. Budowanie i wysylanie: honeygrid-tcp"
docker build -t $Registry/honeygrid-tcp:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/Dockerfile .
docker push $Registry/honeygrid-tcp:latest

Write-Host "4. Budowanie i wysylanie: honeygrid-web"
docker build -t $Registry/honeygrid-web:latest -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/Dockerfile .
docker push $Registry/honeygrid-web:latest

Write-Host "5. Budowanie i wysylanie: honeygrid-ingestion"
docker build -t $Registry/honeygrid-ingestion:latest -f src/HoneyGrid.Ingestion/Dockerfile .
docker push $Registry/honeygrid-ingestion:latest

Write-Host "6. Budowanie i wysylanie: honeygrid-api"
docker build -t $Registry/honeygrid-api:latest -f src/HoneyGrid.Api/Dockerfile .
docker push $Registry/honeygrid-api:latest

Write-Host "Wszystkie obrazy zostaly wyslane! Mozesz teraz zaktualizowac bicep i Container Apps."
