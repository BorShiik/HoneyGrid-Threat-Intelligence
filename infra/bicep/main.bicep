// ============================================================================
// HoneyGrid — orkiestrator główny (Tydzień 1: sieć + płaszczyzna bezpieczeństwa)
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

@description('''Czy wdrażać zasoby Track B wrażliwe na region/limit: Azure OpenAI + Azure Maps
(moduł ai) oraz Static Web App. Te usługi są dostępne tylko w wybranych regionach
i/lub wymagają zatwierdzenia limitu (OpenAI). Gdy polityka subskrypcji wymusza region
bez ich wsparcia (Szwecja/Norwegia/Polska), zostaw false — Track A (sieć, sensory,
Event Hub, Cosmos, Sentinel) wdraża się w pełni. Track B włączy ten flag w swoim regionie.''')
param deployTrackB bool = false

// ---------------------------------------------------------------------------
// Zmienne wspólne
// ---------------------------------------------------------------------------
// Wspólne tagi — nakładane na KAŻDY zasób (wymóg konwencji projektu).
var tags = {
  project: 'HoneyGrid'
  environment: environment
  track: 'week1-network'
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
// Moduły (kolejność: sieć -> bezpieczeństwo -> monitoring/Sentinel -> dane
// -> aplikacje -> AI -> Private Link -> RBAC)
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

// Płaszczyzna bezpieczeństwa (Track A): tożsamości zarządzane (sensor, playbook)
// + Key Vault w trybie RBAC (przeniesiony z ai.bicep w Tygodniu 1).
module security 'modules/security.bicep' = {
  scope: rg
  name: 'security-${environment}'
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
    // Private Endpoints dla Cosmos/Blob/Key Vault tworzy moduł privatelink
    // (niżej). TODO (Tydzień 2, Track A+B): publicNetworkAccess: 'Disabled'
    // na Cosmos/Storage po zweryfikowaniu działania Private Endpoints.
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
    // Tydzień 2, Track A: integracja VNet (snet-dmz), tożsamość sensorów i
    // wpięcie telemetrii do Event Hubs. Brak cykli: app zależy od network /
    // security / data (które nie zależą od app).
    dmzSubnetId: network.outputs.dmzSubnetId
    sensorIdentityId: security.outputs.sensorIdentityId
    sensorIdentityClientId: security.outputs.sensorIdentityClientId
    // Principal ID do przypisania roli AcrPull w module app (przed Container Apps).
    sensorIdentityPrincipalId: security.outputs.sensorIdentityPrincipalId
    eventHubNamespaceName: data.outputs.eventHubNamespaceName
    eventHubName: data.outputs.eventHubName
    // Static Web App tylko gdy region Track B na to pozwala (patrz deployTrackB).
    deployStaticWebApp: deployTrackB
  }
}

// Warstwa AI: Azure OpenAI (gpt-4o-mini), Azure Maps (Track B). Wdrażane warunkowo —
// dostępne tylko w wybranych regionach / wymagają zatwierdzenia limitu OpenAI.
module ai 'modules/ai.bicep' = if (deployTrackB) {
  scope: rg
  name: 'ai-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// Private Link (Track A, Tydzień 1): strefy Private DNS + Private Endpoints
// w snet-data dla Cosmos DB / Blob Storage / Key Vault. Event Hubs (Basic)
// i Service Bus (Basic) świadomie BEZ PE — uzasadnienie w privatelink.bicep.
module privatelink 'modules/privatelink.bicep' = {
  scope: rg
  name: 'privatelink-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
    vnetId: network.outputs.vnetId
    vnetName: network.outputs.vnetName
    dataSubnetId: network.outputs.dataSubnetId
    cosmosAccountName: data.outputs.cosmosAccountName
    storageAccountName: data.outputs.storageAccountName
    keyVaultName: security.outputs.keyVaultName
  }
}

// RBAC — macierz najmniejszych uprawnień. Od Tygodnia 1 principalId sensorów
// i playbooka pochodzą z modułu security (User-Assigned MI), a zakresy ról
// danych zawężamy do konkretnych zasobów (namespace EH, Storage, ACR, NSG dmz).
module rbac 'modules/rbac.bicep' = {
  scope: rg
  name: 'rbac-${environment}'
  params: {
    sensorPrincipalId: security.outputs.sensorIdentityPrincipalId
    playbookPrincipalId: security.outputs.playbookIdentityPrincipalId
    // TODO (Tydzień 4-5, Track A+B): analityk i tożsamość Sentinela są
    // specyficzne dla tenanta — ustawiane per wdrożenie (param / az cli).
    analystPrincipalId: ''
    automationPrincipalId: ''
    // Zawężone zakresy ról danych (Tydzień 1, Track A):
    eventHubNamespaceName: data.outputs.eventHubNamespaceName
    storageAccountName: data.outputs.storageAccountName
    // AcrPull jest w module app (kolejność wdrożenia) — tu nie przekazujemy ACR.
    dmzNsgName: network.outputs.dmzNsgName
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

// Sensory honeypot (moduł app — Tydzień 2, Track A): FQDN do weryfikacji/demo.
output cowrieAppFqdn string = app.outputs.cowrieAppFqdn
output webHoneypotAppFqdn string = app.outputs.webHoneypotAppFqdn
output tcpListenerAppFqdn string = app.outputs.tcpListenerAppFqdn

// Wyjścia Track B — puste gdy deployTrakB=false (moduł ai nie wdrożony).
output openAiEndpoint string = deployTrackB ? ai!.outputs.openAiEndpoint : ''
output mapsAccountName string = deployTrackB ? ai!.outputs.mapsAccountName : ''

// Płaszczyzna bezpieczeństwa (moduł security — Tydzień 1, Track A).
output keyVaultUri string = security.outputs.keyVaultUri
output keyVaultName string = security.outputs.keyVaultName
output sensorIdentityClientId string = security.outputs.sensorIdentityClientId
output sensorIdentityPrincipalId string = security.outputs.sensorIdentityPrincipalId
output playbookIdentityPrincipalId string = security.outputs.playbookIdentityPrincipalId

// Private Endpoints (moduł privatelink — Tydzień 1, Track A).
output cosmosPrivateEndpointId string = privatelink.outputs.cosmosPrivateEndpointId
output blobPrivateEndpointId string = privatelink.outputs.blobPrivateEndpointId
output keyVaultPrivateEndpointId string = privatelink.outputs.keyVaultPrivateEndpointId
