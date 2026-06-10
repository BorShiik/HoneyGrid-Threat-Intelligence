// ============================================================================
// HoneyGrid — moduł RBAC: macierz najmniejszych uprawnień (Tydzień 0: szkielet)
//
// MACIERZ RBAC (least privilege) — dokumentacja docelowych przypisań:
//
// | Tożsamość (principal)            | Rola                                      | Zakres            | Po co                                          |
// |----------------------------------|-------------------------------------------|-------------------|------------------------------------------------|
// | MI sensora (cowrie/tcp-listener) | Monitoring Metrics Publisher              | DCR (cowrie)      | wysyłka logów przez Logs Ingestion API         |
// | Analityk / pipeline CI           | Microsoft Sentinel Contributor            | Resource Group    | zarządzanie regułami analitycznymi, watchlisty |
// | Sentinel (tożsamość usługi)      | Microsoft Sentinel Automation Contributor | Resource Group    | uruchamianie playbooków z automation rules     |
// | Analityk / pipeline CI           | Logic App Contributor                     | RG (playbooki)    | tworzenie i edycja playbooków                  |
// | MI playbooka (Logic App)         | Microsoft Sentinel Responder              | Resource Group    | aktualizacja incydentów (status, komentarze)   |
// | MI playbooka (Logic App)         | Network Contributor                       | NSG dmz           | playbook dopisuje regułę blokującą IP do NSG   |
//
// Zasada: ŻADNYCH kluczy ani connection stringów — wyłącznie Managed Identity
// + powyższe role (plus role danych: Cosmos DB Built-in Data Contributor,
// Azure Service Bus Data Sender/Receiver, Storage Blob Data Contributor,
// Cognitive Services OpenAI User — przypisywane w tygodniach feature'owych).
//
// W Tygodniu 0 principalId są puste — warunki `if (!empty(...))` sprawiają,
// że moduł kompiluje się i wdraża "pusto", a struktura jest gotowa.
// ============================================================================

@description('principalId tożsamości zarządzanej sensora (Cowrie / tcp-listener).')
param sensorPrincipalId string = ''

@description('principalId analityka lub service principala CI/CD.')
param analystPrincipalId string = ''

@description('principalId tożsamości usługi Microsoft Sentinel (automation).')
param automationPrincipalId string = ''

@description('principalId tożsamości zarządzanej playbooka (Logic App).')
param playbookPrincipalId string = ''

@description('Typ principala (ServicePrincipal dla MI, User dla analityka).')
@allowed(['ServicePrincipal', 'User', 'Group'])
param principalType string = 'ServicePrincipal'

// ---------------------------------------------------------------------------
// Dobrze znane GUID-y wbudowanych ról Azure (stałe globalne platformy)
// ---------------------------------------------------------------------------
var roleMonitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb' // Monitoring Metrics Publisher
var roleSentinelContributor = 'ab8e14d6-4a74-4a29-9ba8-549422addade' // Microsoft Sentinel Contributor
var roleSentinelAutomationContributor = 'f4c81013-99ee-4d62-a7ee-b3f1f648599a' // Microsoft Sentinel Automation Contributor
var roleLogicAppContributor = '87a39d53-fc1b-424a-814c-f7e04687dc9e' // Logic App Contributor
var roleSentinelResponder = '3e150937-b8fe-4cfb-8069-0eaf05ecd056' // Microsoft Sentinel Responder
var roleNetworkContributor = '4d97b98b-1d4f-4787-a291-c67834d212e7' // Network Contributor

// Pełne resourceId definicji ról (wymagane przez roleAssignments).
var roleDefinitionIds = {
  monitoringMetricsPublisher: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleMonitoringMetricsPublisher)
  sentinelContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelContributor)
  sentinelAutomationContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelAutomationContributor)
  logicAppContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleLogicAppContributor)
  sentinelResponder: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSentinelResponder)
  networkContributor: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleNetworkContributor)
}

// ---------------------------------------------------------------------------
// Przypisania ról — zakres: Resource Group (zakresy węższe, np. konkretny DCR
// czy NSG, dojdą w tygodniach feature'owych — patrz TODO niżej).
// ---------------------------------------------------------------------------

// Sensor -> Monitoring Metrics Publisher
// TODO (Tydzień 4, Track B): zawęzić zakres z RG do konkretnego DCR
// (scope: cowrieDcr) — sensor ma prawo TYLKO pchać logi do swojego strumienia.
resource sensorMetricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(sensorPrincipalId)) {
  name: guid(resourceGroup().id, sensorPrincipalId, roleMonitoringMetricsPublisher)
  properties: {
    principalId: sensorPrincipalId
    roleDefinitionId: roleDefinitionIds.monitoringMetricsPublisher
    principalType: principalType
    description: 'HoneyGrid: sensor wysyla logi przez Logs Ingestion API (DCR).'
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

// MI playbooka -> Network Contributor
// TODO (Tydzień 5, Track A): zawęzić zakres z RG do KONKRETNEGO NSG dmz
// (scope: dmzNsg) — playbook "block-attacker-ip" dopisuje regułę Deny,
// ale nie może ruszać reszty sieci.
resource playbookNetworkContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(playbookPrincipalId)) {
  name: guid(resourceGroup().id, playbookPrincipalId, roleNetworkContributor)
  properties: {
    principalId: playbookPrincipalId
    roleDefinitionId: roleDefinitionIds.networkContributor
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: playbook blokuje IP atakujacego regula w NSG dmz.'
  }
}

// TODO (Tydzień 1-3, Track A+B): role płaszczyzny danych:
//   - Cosmos DB Built-in Data Contributor (sqlRoleAssignments na koncie Cosmos)
//     dla MI api/tcp-listener — to NIE jest rola ARM, wymaga osobnego zasobu
//     Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments
//   - Azure Event Hubs Data Sender (2b629674-e913-4c01-ae53-ef4638d8f975) dla sensorów
//   - Azure Service Bus Data Sender/Receiver dla api i klasyfikatora AI
//   - Storage Blob Data Contributor (ba92f5b4-2d11-453d-a403-e96b0029c9fe) dla tcp-listener
//   - Cognitive Services OpenAI User (5e0bd9bd-7b93-4f28-af87-19fc36ad61bd) dla klasyfikatora
//   - AcrPull (7f951dda-4ed3-4680-a7ca-43fe172d538d) dla Container Apps

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
}
