// Помощник: вычисляет тот же uniqueString, что main.bicep использует для имён.
// Ресурсов не создаёт — только output. Запуск (RG-scope):
//   az deployment group create -g hg-dev-rg -f infra/bicep/suffix.bicep \
//     --query properties.outputs.suffix.value -o tsv
targetScope = 'resourceGroup'
output suffix string = uniqueString(resourceGroup().id)
output acrName string = toLower('hgdevacr${uniqueString(resourceGroup().id)}')
output functionAppName string = 'hg-dev-func-${uniqueString(resourceGroup().id)}'
