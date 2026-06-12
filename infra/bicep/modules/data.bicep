// ============================================================================
// HoneyGrid — moduł warstwy danych (Tydzień 0: szkielet)
//
// Zasoby:
//   - Cosmos DB (SERVERLESS — płacimy za RU faktycznie zużyte; kluczowe
//     dla budżetu Azure for Students) + baza 'honeygrid' + 5 kontenerów
//   - Storage Account: blob (raw / tty / downloads) + Azure Files 'cowrie'
//     (persystencja SMB dla sensora Cowrie)
//   - Event Hubs (Basic) — strumień telemetrii z sensorów
//   - Service Bus (Basic) — kolejka 'ai-classify' do klasyfikacji LLM
//
// Architektura bezkluczowa: disableLocalAuth wszędzie, gdzie się da —
// dostęp przez Managed Identity + RBAC (moduł rbac.bicep).
// Wyjątek: klucz Storage zostaje włączony, bo Azure Files po SMB
// (persystencja Cowrie) wymaga uwierzytelnienia kluczem.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('''principalId tożsamości workera Ingestion (z modułu security.bicep) —
przypisanie roli płaszczyzny danych Cosmos. Pusty => przypisanie pominięte
(moduł kompiluje się i wdraża samodzielnie).''')
param workerPrincipalId string = ''

// Sufiks unikalności globalnej (Cosmos / Storage / EH / SB mają nazwy globalne).
var suffix = uniqueString(resourceGroup().id)

var cosmosAccountName = '${namePrefix}-${environment}-cosmos-${suffix}'
// Storage: bez myślników, max 24 znaki, tylko małe litery i cyfry.
var storageAccountName = toLower('${namePrefix}${environment}st${suffix}')
var eventHubNamespaceName = '${namePrefix}-${environment}-ehns-${suffix}'
var serviceBusNamespaceName = '${namePrefix}-${environment}-sbns-${suffix}'

// ---------------------------------------------------------------------------
// Definicje kontenerów Cosmos: nazwa / klucz partycji / TTL (sekundy).
// TTL steruje kosztem składowania: surowe zdarzenia i sesje żyją 180 dni,
// agregaty 30 dni, a profile aktorów i IOC są trwałe (null = brak TTL).
// ---------------------------------------------------------------------------
var cosmosContainers = [
  {
    name: 'events'
    partitionKey: '/attackerIp'
    defaultTtl: 15552000 // 180 dni
  }
  {
    name: 'actors'
    partitionKey: '/id'
    defaultTtl: null // bez TTL — profile aktorów trzymamy bezterminowo
  }
  {
    name: 'sessions'
    partitionKey: '/sessionId'
    defaultTtl: 15552000 // 180 dni
  }
  {
    name: 'iocs'
    partitionKey: '/type'
    defaultTtl: null // bez TTL — wskaźniki kompromitacji trzymamy bezterminowo
  }
  {
    name: 'aggregates'
    partitionKey: '/bucket'
    defaultTtl: 2592000 // 30 dni — dane pod dashboard, szybko się starzeją
  }
]

// ---------------------------------------------------------------------------
// Cosmos DB — konto serverless
// ---------------------------------------------------------------------------
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // SERVERLESS — brak stałej opłaty za przepustowość (kluczowe: budżet studencki).
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    // Bezkluczowo: tylko AAD / Managed Identity (RBAC płaszczyzny danych Cosmos).
    disableLocalAuth: true
    minimalTlsVersion: 'Tls12'
    // TODO (Tydzień 2, Track B): publicNetworkAccess: 'Disabled' po podpięciu
    // Private Endpoint w podsieci snet-data (na razie otwarte, żeby dało się rozwijać).
    publicNetworkAccess: 'Enabled'
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: 'honeygrid'
  properties: {
    resource: {
      id: 'honeygrid'
    }
  }
}

// Pętla po definicjach kontenerów — jedna deklaracja, pięć kontenerów.
resource cosmosContainerResources 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for container in cosmosContainers: {
    parent: cosmosDatabase
    name: container.name
    properties: {
      // defaultTtl dołączamy TYLKO dla kontenerów z TTL — Cosmos odrzuca
      // jawny `defaultTtl: null` (BadRequest). Dla actors/iocs (bezterminowe)
      // właściwość musi być całkowicie pominięta, stąd union z warunkiem.
      resource: union(
        {
          id: container.name
          partitionKey: {
            paths: [
              container.partitionKey
            ]
            kind: 'Hash'
          }
        },
        container.defaultTtl != null ? { defaultTtl: container.defaultTtl } : {}
      )
    }
  }
]

// ---------------------------------------------------------------------------
// Cosmos DB — rola PŁASZCZYZNY DANYCH dla workera Ingestion (Tydzień 3)
//
// UWAGA (częsta pułapka): to NIE jest rola ARM (Microsoft.Authorization/
// roleAssignments) — Cosmos ma WŁASNY model RBAC płaszczyzny danych
// (sqlRoleAssignments). Bez tego przypisania SDK dostaje 403 mimo posiadania
// ról ARM na koncie; `disableLocalAuth: true` (wyżej) wymusza wyłącznie AAD,
// więc nie ma awaryjnej furtki przez klucze.
// 00000000-0000-0000-0000-000000000002 = wbudowana rola
// "Cosmos DB Built-in Data Contributor" (odczyt + zapis dokumentów).
// ---------------------------------------------------------------------------
resource workerCosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = if (!empty(workerPrincipalId)) {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, workerPrincipalId, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: workerPrincipalId
    scope: cosmosAccount.id
  }
}

// ---------------------------------------------------------------------------
// Storage Account — blob (raw/tty/downloads/checkpoints) + Azure Files (cowrie)
// ---------------------------------------------------------------------------
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS' // LRS — najtańsza replikacja, wystarczy na coursework
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // UWAGA: zostaje 'true' wyłącznie dla Azure Files po SMB (persystencja Cowrie);
    // dostęp do blobów i tak idzie przez Managed Identity + RBAC.
    allowSharedKeyAccess: true
    accessTier: 'Hot'
    // TODO (Tydzień 2, Track B): networkAcls z defaultAction 'Deny' po
    // wdrożeniu Private Endpoint dla blob/file w snet-data.
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// Kontenery blob: raw (surowe zdarzenia), tty (nagrania sesji Cowrie),
// downloads (złapane przez honeypot artefakty/malware — NIE uruchamiać!),
// checkpoints (offsety EventProcessorClient workera Ingestion — Tydzień 3).
resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for containerName in ['raw', 'tty', 'downloads', 'checkpoints']: {
    parent: blobService
    name: containerName
    properties: {
      publicAccess: 'None'
    }
  }
]

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// Udział SMB na stan sensora Cowrie (dl/, log/, etc.) — montowany w Container App.
resource cowrieFileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: 'cowrie'
  properties: {
    shareQuota: 5 // GiB — mało, bo płacimy za przydział (transakcyjny tier)
    enabledProtocols: 'SMB'
  }
}

// ---------------------------------------------------------------------------
// Event Hubs (Basic) — wlot telemetrii z sensorów DMZ
// Basic: 1 dzień retencji, bez Capture — wystarcza, bo konsument
// (tcp-listener / funkcja) zdejmuje zdarzenia na bieżąco. Najtańszy tier.
// ---------------------------------------------------------------------------
resource eventHubNamespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: eventHubNamespaceName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    // Bezkluczowo: producenci (sensory) i konsumenci łączą się przez Managed Identity.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource honeypotEventsHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: eventHubNamespace
  name: 'honeypot-events'
  properties: {
    partitionCount: 2
    messageRetentionInDays: 1 // maksimum dla tieru Basic
  }
}

// ---------------------------------------------------------------------------
// Service Bus (Basic) — kolejka zadań klasyfikacji AI
// ---------------------------------------------------------------------------
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusNamespaceName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    // Bezkluczowo: dostęp przez Managed Identity + role Service Bus Data Sender/Receiver.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

// Kolejka zadań dla klasyfikatora LLM (gpt-4o-mini, moduł ai.bicep).
resource aiClassifyQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBusNamespace
  name: 'ai-classify'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D' // zlecenia klasyfikacji starsze niż doba są bezwartościowe
  }
}

// ---------------------------------------------------------------------------
// PLACEHOLDERY: Private Endpoints (warstwa data tylko przez Private Link)
// TODO (Tydzień 2, Track B): Private Endpoints dla cosmosAccount (groupId 'Sql'),
// storageAccount (groupId 'blob' i 'file') oraz serviceBusNamespace (groupId
// 'namespace') w podsieci snet-data + rejestracja w strefach Private DNS
// z modułu network.bicep. Potem: publicNetworkAccess = 'Disabled'.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output cosmosAccountId string = cosmosAccount.id
output cosmosAccountName string = cosmosAccount.name
output cosmosEndpoint string = cosmosAccount.properties.documentEndpoint
output cosmosDatabaseName string = cosmosDatabase.name

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output fileEndpoint string = storageAccount.properties.primaryEndpoints.file

output eventHubNamespaceId string = eventHubNamespace.id
output eventHubNamespaceName string = eventHubNamespace.name
output eventHubName string = honeypotEventsHub.name

output serviceBusNamespaceId string = serviceBusNamespace.id
output serviceBusNamespaceName string = serviceBusNamespace.name
output aiClassifyQueueName string = aiClassifyQueue.name
