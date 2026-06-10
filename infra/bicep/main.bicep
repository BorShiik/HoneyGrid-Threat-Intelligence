// ============================================================================
// HoneyGrid — orkiestrator główny (Tydzień 0: szkielet)
// Zakres: subskrypcja — tworzy Resource Group i wpina wszystkie moduły.
// Wdrożenie: az deployment sub create -l <region> -f main.bicep -p main.dev.bicepparam
// ============================================================================
targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parametry
// ---------------------------------------------------------------------------
@description('Środowisko wdrożenia (dev = praca bieżąca, prod = demo/zaliczenie).')
@allowed([
  'dev'
  'prod'
])
param environment string

@description('Region Azure. Domyślnie West Europe (dostępny w Azure for Students).')
param location string = 'westeurope'

@description('Prefiks nazw zasobów.')
@minLength(2)
@maxLength(5)
param namePrefix string = 'hg'

// ---------------------------------------------------------------------------
// Zmienne wspólne
// ---------------------------------------------------------------------------
// Wspólne tagi — nakładane na KAŻDY zasób (wymóg konwencji projektu).
var tags = {
  project: 'HoneyGrid'
  environment: environment
  track: 'week0-skeleton'
}

var resourceGroupName = '${namePrefix}-${environment}-rg'

// ---------------------------------------------------------------------------
// Resource Group
// ---------------------------------------------------------------------------
resource rg 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Moduły (kolejność: sieć -> monitoring/Sentinel -> dane -> aplikacje -> AI -> RBAC)
// ---------------------------------------------------------------------------

// Sieć hub-and-spoke: dmz (sensory) / logic (przetwarzanie) / data (Private Link).
module network 'modules/network.bicep' = {
  scope: rg
  name: 'network-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// Log Analytics + Microsoft Sentinel + DCE (DCR = stub na Tydzień 4).
module sentinel 'modules/sentinel.bicep' = {
  scope: rg
  name: 'sentinel-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
    // Sentinel free tier: 5 GB/dzień — twardy limit kosztowy dla subskrypcji studenckiej.
    dailyQuotaGb: 5
  }
}

// Warstwa danych: Cosmos DB (serverless), Storage, Event Hubs, Service Bus.
module data 'modules/data.bicep' = {
  scope: rg
  name: 'data-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
    // TODO (Tydzień 2, Track B): przekazać network.outputs.dataSubnetId
    // i włączyć Private Endpoints dla Cosmos/Storage/Service Bus.
  }
}

// Warstwa aplikacyjna: Container Apps (scale-to-zero), ACR, Static Web App, App Insights.
module app 'modules/app.bicep' = {
  scope: rg
  name: 'app-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
    logAnalyticsWorkspaceName: sentinel.outputs.logAnalyticsWorkspaceName
  }
}

// Warstwa AI: Azure OpenAI (gpt-4o-mini), Azure Maps, Key Vault (RBAC, bezkluczowo).
module ai 'modules/ai.bicep' = {
  scope: rg
  name: 'ai-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// RBAC — macierz najmniejszych uprawnień. W Tygodniu 0 same definicje GUID-ów ról
// i warunkowe przypisania (puste principalId => nic się nie wdraża).
module rbac 'modules/rbac.bicep' = {
  scope: rg
  name: 'rbac-${environment}'
  params: {
    // TODO (Tydzień 2-5, Track A+B): wstawić principalId tożsamości zarządzanych
    // (sensory, playbooki, Logic Apps) po ich utworzeniu.
    sensorPrincipalId: ''
    analystPrincipalId: ''
    automationPrincipalId: ''
    playbookPrincipalId: ''
  }
}

// ---------------------------------------------------------------------------
// Wyjścia — przepinamy najważniejsze identyfikatory z modułów
// ---------------------------------------------------------------------------
output resourceGroupName string = rg.name

output vnetId string = network.outputs.vnetId
output dmzSubnetId string = network.outputs.dmzSubnetId
output logicSubnetId string = network.outputs.logicSubnetId
output dataSubnetId string = network.outputs.dataSubnetId

output logAnalyticsWorkspaceId string = sentinel.outputs.logAnalyticsWorkspaceId
output dataCollectionEndpointId string = sentinel.outputs.dataCollectionEndpointId

output cosmosAccountName string = data.outputs.cosmosAccountName
output cosmosEndpoint string = data.outputs.cosmosEndpoint
output storageAccountName string = data.outputs.storageAccountName
output eventHubNamespaceName string = data.outputs.eventHubNamespaceName
output serviceBusNamespaceName string = data.outputs.serviceBusNamespaceName

output containerAppsEnvironmentId string = app.outputs.containerAppsEnvironmentId
output containerRegistryLoginServer string = app.outputs.containerRegistryLoginServer
output staticWebAppDefaultHostname string = app.outputs.staticWebAppDefaultHostname
output appInsightsConnectionString string = app.outputs.appInsightsConnectionString

output openAiEndpoint string = ai.outputs.openAiEndpoint
output keyVaultUri string = ai.outputs.keyVaultUri
output mapsAccountName string = ai.outputs.mapsAccountName
