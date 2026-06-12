// ============================================================================
// HoneyGrid — moduł RBAC: macierz najmniejszych uprawnień (Tydzień 4: DCR Sentinela)
//
// MACIERZ RBAC (least privilege) — faktyczne przypisania po Tygodniu 4:
//
// | Tożsamość (principal)            | Rola                                      | Zakres              | Po co                                          |
// |----------------------------------|-------------------------------------------|---------------------|------------------------------------------------|
// | MI sensora (id-sensor)           | Azure Event Hubs Data Sender              | namespace Event Hubs| sensory wysyłają telemetrię do honeypot-events |
// | MI sensora (id-sensor)           | Storage Blob Data Contributor             | konto Storage       | zapis raw/tty/downloads (artefakty honeypota)  |
// | MI sensora (id-sensor)           | AcrPull                                   | rejestr ACR         | Container Apps ciągną obrazy sensorów (T2)     |
// | MI workera (id-worker)           | Azure Event Hubs Data Receiver            | namespace Event Hubs| worker CZYTA telemetrię (EventProcessorClient) |
// | MI workera (id-worker)           | Storage Blob Data Contributor             | konto Storage       | checkpointy EH + zapis surowych zdarzeń (raw)  |
// | MI workera (id-worker)           | Azure Service Bus Data Sender             | namespace Service Bus| worker wysyła zlecenia do kolejki ai-classify |
// | MI workera (id-worker)           | AcrPull                                   | rejestr ACR (app.bicep)| pull obrazu honeygrid-ingestion (kolejność) |
// | MI workera (id-worker)           | Cosmos DB Built-in Data Contributor       | konto Cosmos (data.bicep)| zapis dokumentów — rola PŁASZCZYZNY DANYCH (sqlRoleAssignments), nie ARM |
// | MI workera (id-worker)           | Monitoring Metrics Publisher              | DCR cowrie (sentinel.bicep)| wysyłka Cowrie_CL przez Logs Ingestion API (T4) |
// | Analityk / pipeline CI           | Microsoft Sentinel Contributor            | Resource Group      | zarządzanie regułami analitycznymi, watchlisty |
// | Sentinel (tożsamość usługi)      | Microsoft Sentinel Automation Contributor | Resource Group      | uruchamianie playbooków z automation rules     |
// | Analityk / pipeline CI           | Logic App Contributor                     | RG (playbooki)      | tworzenie i edycja playbooków                  |
// | MI playbooka (id-playbook)       | Microsoft Sentinel Responder              | Resource Group      | aktualizacja incydentów (status, komentarze)   |
// | MI playbooka (id-playbook)       | Network Contributor                       | NSG dmz (KONKRETNY) | playbook dopisuje regułę blokującą IP do NSG   |
//
// Zasada: ŻADNYCH kluczy ani connection stringów — wyłącznie Managed Identity
// + powyższe role. Role spoza modelu ARM żyją w innych modułach: Cosmos DB
// Built-in Data Contributor (sqlRoleAssignments) w data.bicep, AcrPull
// w app.bicep (kolejność wdrożenia). Pozostałe TODO na dole pliku
// (Cognitive Services OpenAI User — Track B).
//
// Przypisania są warunkowe: `if (!empty(principalId) && !empty(nazwaZasobu))` —
// puste parametry => nic się nie wdraża, moduł kompiluje się i działa "pusto".
// ============================================================================

@description('principalId tożsamości zarządzanej sensora (Cowrie / tcp-listener).')
param sensorPrincipalId string = ''

@description('principalId analityka lub service principala CI/CD.')
param analystPrincipalId string = ''

@description('principalId tożsamości usługi Microsoft Sentinel (automation).')
param automationPrincipalId string = ''

@description('principalId tożsamości zarządzanej playbooka (Logic App).')
param playbookPrincipalId string = ''

@description('principalId tożsamości zarządzanej workera Ingestion (Tydzień 3).')
param workerPrincipalId string = ''

@description('Typ principala (ServicePrincipal dla MI, User dla analityka).')
@allowed(['ServicePrincipal', 'User', 'Group'])
param principalType string = 'ServicePrincipal'

// --- Nazwy zasobów do zawężania zakresów (Tydzień 1, Track A) ---------------
// Puste => odpowiednie przypisanie się nie wdraża (warunek w zasobie).

@description('Nazwa namespace Event Hubs (zakres roli Event Hubs Data Sender).')
param eventHubNamespaceName string = ''

@description('Nazwa konta Storage (zakres roli Storage Blob Data Contributor).')
param storageAccountName string = ''

@description('Nazwa namespace Service Bus (zakres roli Service Bus Data Sender workera).')
param serviceBusNamespaceName string = ''

// UWAGA: rola AcrPull dla sensora jest przypisywana w module app.bicep (musi
// powstać PRZED Container Apps, a rbac.bicep wykonuje się po app). Dlatego nie
// ma tu parametru ACR ani przypisania AcrPull.

@description('Nazwa NSG strefy DMZ (zakres roli Network Contributor playbooka).')
param dmzNsgName string = ''

// ---------------------------------------------------------------------------
// Dobrze znane GUID-y wbudowanych ról Azure (stałe globalne platformy)
// ---------------------------------------------------------------------------
var roleMonitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb' // Monitoring Metrics Publisher
var roleSentinelContributor = 'ab8e14d6-4a74-4a29-9ba8-549422addade' // Microsoft Sentinel Contributor
var roleSentinelAutomationContributor = 'f4c81013-99ee-4d62-a7ee-b3f1f648599a' // Microsoft Sentinel Automation Contributor
var roleLogicAppContributor = '87a39d53-fc1b-424a-814c-f7e04687dc9e' // Logic App Contributor
var roleSentinelResponder = '3e150937-b8fe-4cfb-8069-0eaf05ecd056' // Microsoft Sentinel Responder
var roleNetworkContributor = '4d97b98b-1d4f-4787-a291-c67834d212e7' // Network Contributor
var roleEventHubsDataSender = '2b629674-e913-4c01-ae53-ef4638d8f975' // Azure Event Hubs Data Sender
var roleEventHubsDataReceiver = 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde' // Azure Event Hubs Data Receiver
var roleStorageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
var roleServiceBusDataSender = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39' // Azure Service Bus Data Sender
var roleAcrPull = '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull

// Pełne resourceId definicji ról (wymagane przez roleAssignments).
var roleDefinitionIds = {
  monitoringMetricsPublisher: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleMonitoringMetricsPublisher)
  sentinelContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelContributor)
  sentinelAutomationContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelAutomationContributor)
  logicAppContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleLogicAppContributor)
  sentinelResponder: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelResponder)
  networkContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleNetworkContributor)
  eventHubsDataSender: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleEventHubsDataSender)
  eventHubsDataReceiver: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleEventHubsDataReceiver)
  storageBlobDataContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
  serviceBusDataSender: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleServiceBusDataSender)
  acrPull: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
}

// ---------------------------------------------------------------------------
// Odwołania do istniejących zasobów — cele zawężonych zakresów ról.
// Deklaracja `existing` niczego nie wdraża; nazwy mogą być puste, bo każde
// przypisanie korzystające z zakresu jest warunkowane niepustą nazwą.
// ---------------------------------------------------------------------------
resource eventHubNamespace 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: eventHubNamespaceName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

resource dmzNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' existing = {
  name: dmzNsgName
}

// ---------------------------------------------------------------------------
// Przypisania ról — zakresy zawężone do konkretnych zasobów tam, gdzie zasób
// już istnieje (Tydzień 1); RG zostaje tylko tam, gdzie cel (DCR, playbooki)
// powstanie w późniejszych tygodniach.
// Nazwy `guid(...)` zawierają zakres (id RG lub nazwę zasobu docelowego) —
// deterministyczne i bez kolizji między przypisaniami tej samej roli.
// ---------------------------------------------------------------------------

// ZREALIZOWANE TODO z Tygodnia 0 (Tydzień 4): stare, szerokie przypisanie
// "sensor -> Monitoring Metrics Publisher na CAŁEJ RG" USUNIĘTE. Do DCE/DCR
// nie wysyła sensor, tylko WORKER Ingestion — a jego rola Monitoring Metrics
// Publisher żyje teraz w module sentinel.bicep, zawężona do KONKRETNEGO DCR
// (hg-{env}-dcr-cowrie), bo tam powstaje zasób-zakres. Sensor MI nie ma
// żadnych uprawnień Azure Monitor — least privilege domknięte.

// Sensor -> Azure Event Hubs Data Sender (zakres: KONKRETNY namespace, nie RG)
// Sensory mogą TYLKO wysyłać do honeypot-events — bez odczytu, bez zarządzania.
resource sensorEventHubsDataSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(sensorPrincipalId) && !empty(eventHubNamespaceName)) {
  name: guid(resourceGroup().id, eventHubNamespaceName, sensorPrincipalId, roleEventHubsDataSender)
  scope: eventHubNamespace
  properties: {
    principalId: sensorPrincipalId
    roleDefinitionId: roleDefinitionIds.eventHubsDataSender
    principalType: principalType
    description: 'HoneyGrid: sensor wysyla telemetrie do Event Hubs (honeypot-events).'
  }
}

// Sensor -> Storage Blob Data Contributor (zakres: KONKRETNE konto Storage)
// Zapis artefaktów honeypota: raw / tty / downloads.
resource sensorBlobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(sensorPrincipalId) && !empty(storageAccountName)) {
  name: guid(resourceGroup().id, storageAccountName, sensorPrincipalId, roleStorageBlobDataContributor)
  scope: storageAccount
  properties: {
    principalId: sensorPrincipalId
    roleDefinitionId: roleDefinitionIds.storageBlobDataContributor
    principalType: principalType
    description: 'HoneyGrid: sensor zapisuje bloby (raw/tty/downloads) bez kluczy.'
  }
}

// Sensor -> AcrPull: przeniesione do modułu app.bicep (kolejność wdrożenia —
// rola musi istnieć przed utworzeniem Container Apps; rbac wykonuje się za późno).

// ---------------------------------------------------------------------------
// Worker Ingestion (Tydzień 3, Track A) — role ARM płaszczyzny danych.
// Asymetria względem sensora jest CELOWA (least privilege):
// sensor = Sender (tylko pisze do strumienia), worker = Receiver (tylko czyta).
// Rola Cosmos NIE jest tutaj — to zasób sqlRoleAssignments w data.bicep,
// AcrPull workera jest w app.bicep (kolejność wdrożenia).
// ---------------------------------------------------------------------------

// Worker -> Azure Event Hubs Data RECEIVER (zakres: KONKRETNY namespace)
// Worker tylko CZYTA telemetrię — nie może niczego wstrzykiwać do strumienia.
resource workerEventHubsDataReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerPrincipalId) && !empty(eventHubNamespaceName)) {
  name: guid(resourceGroup().id, eventHubNamespaceName, workerPrincipalId, roleEventHubsDataReceiver)
  scope: eventHubNamespace
  properties: {
    principalId: workerPrincipalId
    roleDefinitionId: roleDefinitionIds.eventHubsDataReceiver
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: worker Ingestion czyta telemetrie z Event Hubs (EventProcessorClient).'
  }
}

// Worker -> Storage Blob Data Contributor (zakres: KONKRETNE konto Storage)
// Checkpointy EventProcessorClient (kontener checkpoints) + zapis surowych
// zdarzeń (kontener raw).
resource workerBlobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerPrincipalId) && !empty(storageAccountName)) {
  name: guid(resourceGroup().id, storageAccountName, workerPrincipalId, roleStorageBlobDataContributor)
  scope: storageAccount
  properties: {
    principalId: workerPrincipalId
    roleDefinitionId: roleDefinitionIds.storageBlobDataContributor
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: worker zapisuje checkpointy i surowe zdarzenia do blobow (bez kluczy).'
  }
}

// Worker -> Azure Service Bus Data SENDER (zakres: KONKRETNY namespace)
// Worker tylko WYSYŁA zlecenia klasyfikacji do kolejki ai-classify —
// odbiorcą będzie klasyfikator AI (Receiver, Track B).
resource workerServiceBusDataSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerPrincipalId) && !empty(serviceBusNamespaceName)) {
  name: guid(resourceGroup().id, serviceBusNamespaceName, workerPrincipalId, roleServiceBusDataSender)
  scope: serviceBusNamespace
  properties: {
    principalId: workerPrincipalId
    roleDefinitionId: roleDefinitionIds.serviceBusDataSender
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: worker wysyla zlecenia klasyfikacji do kolejki ai-classify.'
  }
}

// Analityk / CI -> Microsoft Sentinel Contributor (RG)
resource analystSentinelContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(analystPrincipalId)) {
  name: guid(resourceGroup().id, analystPrincipalId, roleSentinelContributor)
  properties: {
    principalId: analystPrincipalId
    roleDefinitionId: roleDefinitionIds.sentinelContributor
    principalType: principalType
    description: 'HoneyGrid: zarzadzanie regulami analitycznymi i watchlistami Sentinela.'
  }
}

// Analityk / CI -> Logic App Contributor (RG)
resource analystLogicAppContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(analystPrincipalId)) {
  name: guid(resourceGroup().id, analystPrincipalId, roleLogicAppContributor)
  properties: {
    principalId: analystPrincipalId
    roleDefinitionId: roleDefinitionIds.logicAppContributor
    principalType: principalType
    description: 'HoneyGrid: tworzenie i edycja playbookow (Logic Apps).'
  }
}

// Tożsamość Sentinela -> Microsoft Sentinel Automation Contributor (RG)
// Wymagane, by automation rules mogly uruchamiac playbooki w tej RG.
resource sentinelAutomation 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(automationPrincipalId)) {
  name: guid(resourceGroup().id, automationPrincipalId, roleSentinelAutomationContributor)
  properties: {
    principalId: automationPrincipalId
    roleDefinitionId: roleDefinitionIds.sentinelAutomationContributor
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: Sentinel uruchamia playbooki przez automation rules.'
  }
}

// MI playbooka -> Microsoft Sentinel Responder (RG)
resource playbookSentinelResponder 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(playbookPrincipalId)) {
  name: guid(resourceGroup().id, playbookPrincipalId, roleSentinelResponder)
  properties: {
    principalId: playbookPrincipalId
    roleDefinitionId: roleDefinitionIds.sentinelResponder
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: playbook aktualizuje incydenty (status, komentarze).'
  }
}

// MI playbooka -> Network Contributor (zakres: KONKRETNY NSG dmz, nie RG)
// Zrealizowane TODO z Tygodnia 0: playbook "block-attacker-ip" dopisuje
// regułę Deny WYŁĄCZNIE w NSG strefy DMZ — nie może ruszać reszty sieci.
resource playbookNetworkContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(playbookPrincipalId) && !empty(dmzNsgName)) {
  name: guid(resourceGroup().id, dmzNsgName, playbookPrincipalId, roleNetworkContributor)
  scope: dmzNsg
  properties: {
    principalId: playbookPrincipalId
    roleDefinitionId: roleDefinitionIds.networkContributor
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: playbook blokuje IP atakujacego regula w NSG dmz (tylko ten NSG).'
  }
}

// ZREALIZOWANE w Tygodniu 3 (Track A): Cosmos DB Built-in Data Contributor
// dla workera (sqlRoleAssignments w data.bicep) + Service Bus Data Sender (wyżej).
// TODO (Track B): pozostałe role płaszczyzny danych:
//   - Azure Service Bus Data Receiver dla klasyfikatora AI (odbiór z ai-classify)
//   - Cognitive Services OpenAI User (5e0bd9bd-7b93-4f28-af87-19fc36ad61bd) dla klasyfikatora

// ---------------------------------------------------------------------------
// Wyjścia — identyfikatory GUID ról (przydatne w skryptach az cli)
// ---------------------------------------------------------------------------
output roleGuids object = {
  monitoringMetricsPublisher: roleMonitoringMetricsPublisher
  sentinelContributor: roleSentinelContributor
  sentinelAutomationContributor: roleSentinelAutomationContributor
  logicAppContributor: roleLogicAppContributor
  sentinelResponder: roleSentinelResponder
  networkContributor: roleNetworkContributor
  eventHubsDataSender: roleEventHubsDataSender
  eventHubsDataReceiver: roleEventHubsDataReceiver
  storageBlobDataContributor: roleStorageBlobDataContributor
  serviceBusDataSender: roleServiceBusDataSender
  acrPull: roleAcrPull
}
