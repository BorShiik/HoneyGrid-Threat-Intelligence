// ============================================================================
// HoneyGrid — moduł SIEM (Tydzień 0: szkielet, Tydzień 4: pełna ścieżka ingestii)
//
// Zasoby:
//   - Log Analytics Workspace (PerGB2018) z DZIENNYM LIMITEM ingestii
//     (dailyQuotaGb, domyślnie 5 GB) — bezpiecznik kosztowy: Microsoft Sentinel
//     jest darmowy do 5 GB/dzień przez pierwsze 31 dni (trial), a limit chroni
//     budżet studencki przed zalewem telemetrii z honeypotów
//   - Onboarding Microsoft Sentinel (onboardingStates/default)
//   - Data Collection Endpoint (DCE) — realny od Tygodnia 0
//   - (Tydzień 4) Tabela niestandardowa Cowrie_CL + Data Collection Rule
//     (kind 'Direct', Logs Ingestion API) + rola Monitoring Metrics Publisher
//     dla workera Ingestion, zawężona do KONKRETNEGO DCR
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Dzienny limit ingestii Log Analytics w GB (bezpiecznik kosztowy).')
param dailyQuotaGb int = 5

@description('Retencja danych w workspace (dni). 90 dni wystarcza na analizę kampanii.')
param retentionInDays int = 90

@description('''principalId tożsamości workera Ingestion (z modułu security.bicep) —
dostaje rolę Monitoring Metrics Publisher na KONKRETNYM DCR (Tydzień 4).
Pusty => przypisanie się nie wdraża, moduł kompiluje się w izolacji.''')
param workerPrincipalId string = ''

var workspaceName = '${namePrefix}-${environment}-law'
var dceName = '${namePrefix}-${environment}-dce'
var dcrName = '${namePrefix}-${environment}-dcr-cowrie'

// JEDYNE ŹRÓDŁO PRAWDY schematu Cowrie_CL: te same kolumny trafiają do
// definicji tabeli i do streamDeclarations DCR (nazwy i typy 1:1 — rozjazd
// kolumn = ciche dropowanie pól przy ingestii). Lustrzane DTO żyje w workerze
// .NET (HoneyGrid.Ingestion) — każda zmiana tutaj wymaga zmiany tam.
// UWAGA: kolumny Category/KillChainPhase to ZAŚLEPKA pod klasyfikator AI
// Track B (jeszcze nie istnieje) — schemat gotowy, wartości na razie null.
var cowrieColumns = [
  { name: 'TimeGenerated', type: 'datetime' }
  { name: 'AttackerIp', type: 'string' }
  { name: 'SensorId', type: 'string' }
  { name: 'SensorType', type: 'string' }
  { name: 'EventType', type: 'string' }
  { name: 'SessionId', type: 'string' }
  { name: 'Username', type: 'string' }
  { name: 'Password', type: 'string' }
  { name: 'Command', type: 'string' }
  { name: 'HttpMethod', type: 'string' }
  { name: 'HttpPath', type: 'string' }
  { name: 'UserAgent', type: 'string' }
  { name: 'CountryCode', type: 'string' }
  { name: 'City', type: 'string' }
  { name: 'Asn', type: 'string' }
  { name: 'AsnOrg', type: 'string' }
  { name: 'ThreatScore', type: 'int' }
  { name: 'KnownMalicious', type: 'boolean' }
  { name: 'Category', type: 'string' }
  { name: 'KillChainPhase', type: 'string' }
]

// Nazwa strumienia wejściowego DCR — kontrakt z workerem .NET (LogsIngestionClient).
var cowrieStreamName = 'Custom-CowrieStream'

// ---------------------------------------------------------------------------
// Log Analytics Workspace
// ---------------------------------------------------------------------------
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    workspaceCapping: {
      // Twardy dzienny limit — po przekroczeniu ingestia jest wstrzymywana
      // do północy UTC. Świadomy kompromis: lepiej zgubić logi niż budżet.
      dailyQuotaGb: dailyQuotaGb
    }
    features: {
      // Bezkluczowo: wymuszamy AAD/Entra przy zapytaniach do workspace.
      disableLocalAuth: false // TODO (Tydzień 4, Track B): true, gdy agenci przejdą na DCR + MI
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// Microsoft Sentinel — onboarding workspace
// ---------------------------------------------------------------------------
resource sentinelOnboarding 'Microsoft.SecurityInsights/onboardingStates@2024-03-01' = {
  scope: logAnalyticsWorkspace
  name: 'default'
  properties: {
    customerManagedKey: false
  }
}

// ---------------------------------------------------------------------------
// Data Collection Endpoint — realny od Tygodnia 0
// (sensory potrzebują stałego adresu ingestii; sam DCR dojdzie w Tygodniu 4)
// ---------------------------------------------------------------------------
resource dataCollectionEndpoint 'Microsoft.Insights/dataCollectionEndpoints@2023-03-11' = {
  name: dceName
  location: location
  tags: tags
  properties: {
    networkAcls: {
      // TODO (Tydzień 2, Track B): 'Disabled' po podpięciu Private Link Scope (AMPLS).
      publicNetworkAccess: 'Enabled'
    }
  }
}

// ---------------------------------------------------------------------------
// Tabela niestandardowa Cowrie_CL (Tydzień 4) — zrealizowany stub z Tygodnia 0.
// Plan 'Analytics' (pełny KQL + reguły analityczne Sentinela — plan 'Basic'
// nie wspiera alertów). Kolumny z cowrieColumns (jedyne źródło prawdy wyżej);
// Category/KillChainPhase = zaślepka pod klasyfikator AI Track B.
// ---------------------------------------------------------------------------
resource cowrieTable 'Microsoft.OperationalInsights/workspaces/tables@2023-09-01' = {
  parent: logAnalyticsWorkspace
  name: 'Cowrie_CL'
  properties: {
    plan: 'Analytics'
    schema: {
      name: 'Cowrie_CL'
      columns: cowrieColumns
    }
    retentionInDays: retentionInDays
  }
}

// ---------------------------------------------------------------------------
// Data Collection Rule (kind 'Direct') — Tydzień 4, Logs Ingestion API.
// Worker .NET wysyła wsady JSON na DCE -> strumień Custom-CowrieStream ->
// transformacja ingestion-time -> tabela Cowrie_CL.
//
// Transformacja KQL odrzuca śmieci (puste / 'unknown' AttackerIp) PRZED
// zapisem do workspace — każdy odrzucony rekord to oszczędność twardego
// limitu 5 GB/dzień (workspaceCapping wyżej). UWAGA: błąd składni KQL w
// transformKql wywala CAŁY deployment — zmieniać ostrożnie, jedna linia.
// ---------------------------------------------------------------------------
resource cowrieDcr 'Microsoft.Insights/dataCollectionRules@2023-03-11' = {
  name: dcrName
  location: location
  tags: tags
  kind: 'Direct' // ingestia bezpośrednia przez Logs Ingestion API (bez agenta)
  properties: {
    dataCollectionEndpointId: dataCollectionEndpoint.id
    streamDeclarations: {
      // Te same kolumny co tabela (cowrieColumns) — nazwy i typy 1:1.
      'Custom-CowrieStream': {
        columns: cowrieColumns
      }
    }
    destinations: {
      logAnalytics: [
        {
          workspaceResourceId: logAnalyticsWorkspace.id
          name: 'law-destination'
        }
      ]
    }
    dataFlows: [
      {
        streams: [cowrieStreamName]
        destinations: ['law-destination']
        // Filtr ingestion-time (strojenie po REALNYM ruchu, Tydzień 4):
        //  - bez sensownego IP rekord jest bezwartościowy dla korelacji;
        //  - 127.0.0.1/::1 to sondy zdrowia platformy Container Apps do nasłuchu
        //    TCP — stanowiły 83% wolumenu i zjadały limit 5 GB/dzień.
        // Odrzucamy PRZED naliczeniem do limitu — to jest cała wartość DCR.
        transformKql: 'source | where isnotempty(AttackerIp) and AttackerIp !in (\'unknown\', \'127.0.0.1\', \'::1\', \'::ffff:127.0.0.1\')'
        outputStream: 'Custom-Cowrie_CL'
      }
    ]
  }
  // Jawna zależność: tabela MUSI istnieć zanim DCR zadeklaruje outputStream
  // Custom-Cowrie_CL — inaczej walidacja DCR odrzuci nieznaną tabelę docelową.
  dependsOn: [cowrieTable]
}

// ---------------------------------------------------------------------------
// RBAC: worker Ingestion -> Monitoring Metrics Publisher na KONKRETNYM DCR.
// Domknięcie TODO z Tygodnia 0 (rbac.bicep): zakres zawężony z całej RG do
// TEGO JEDNEGO DCR — worker może pchać logi wyłącznie do swojego strumienia
// Custom-CowrieStream, niczego więcej w RG nie dotknie. Przypisanie żyje tutaj
// (nie w rbac.bicep), bo zakres (DCR) powstaje w tym module.
// ---------------------------------------------------------------------------
var roleMonitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb' // Monitoring Metrics Publisher

resource workerDcrMetricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerPrincipalId)) {
  name: guid(cowrieDcr.id, workerPrincipalId, roleMonitoringMetricsPublisher)
  scope: cowrieDcr
  properties: {
    principalId: workerPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleMonitoringMetricsPublisher)
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: worker Ingestion pcha logi do Cowrie_CL przez Logs Ingestion API (tylko ten DCR).'
  }
}

// ---------------------------------------------------------------------------
// Reguły analityczne Sentinela (Tydzień 5) — domknięcie TODO z Tygodnia 0.
//
// Filozofia: reguły RATE-BASED (progi na agregatach KQL) zamiast per-event —
// honeypot generuje setki zdarzeń login.failed na godzinę i alert na każde
// z nich to czysty alert fatigue. Zamiast tego: 4 wzorce ataku z progami.
//
// Zapytania KQL żyją w osobnych plikach (infra/sentinel/queries/*.kql —
// jedyne źródło prawdy, ładowane przez loadTextContent w czasie kompilacji),
// żeby dało się je czytać/stroić bez grzebania w Bicepie. UWAGA: błąd
// składni KQL wywala CAŁY deployment (lekcja z transformKql wyżej).
//
// Mapowanie MITRE ATT&CK (tactics/techniques) — wymóg planu §12; encje
// (entityMappings: IP / Account / Host) to WEJŚCIE dla SOAR (Tydzień 6):
// playbooki Logic Apps dostaną z incydentu gotową encję IP do blokady/wzbogacenia.
//
// Grupowanie incydentów (incidentConfiguration.groupingConfiguration):
// 260 zdarzeń Hydry = JEDEN incydent zgrupowany po encji, a nie 260 alertów —
// to wprost metryka MTTD / alert fatigue z planu §12.
//
// TODO (Tydzień 6): automation rules wpinające playbooki (Logic Apps).
// ---------------------------------------------------------------------------

// (a) Brute force z jednego IP — T1110, próg >15 nieudanych prób / 5 min.
resource ruleBruteForceSingleIp 'Microsoft.SecurityInsights/alertRules@2023-11-01' = {
  scope: logAnalyticsWorkspace
  name: guid(logAnalyticsWorkspace.id, 'rule-brute-force')
  kind: 'Scheduled'
  properties: {
    displayName: 'HoneyGrid — atak brute force z jednego IP'
    description: 'Ponad 15 nieudanych logowań z jednego adresu IP w oknie 5 minut (zautomatyzowany brute force, np. Hydra). Próg wstępny — do strojenia po realnym ruchu. MITRE: T1110.001.'
    severity: 'Medium'
    enabled: true
    query: loadTextContent('../../sentinel/queries/brute-force-single-ip.kql')
    queryFrequency: 'PT5M'
    queryPeriod: 'PT1H' // okno >= bin(5m); 1h daje zapas na opóźnienia ingestii
    triggerOperator: 'GreaterThan'
    triggerThreshold: 0
    suppressionEnabled: false
    suppressionDuration: 'PT1H'
    tactics: ['CredentialAccess']
    techniques: ['T1110']
    entityMappings: [
      {
        entityType: 'IP'
        fieldMappings: [
          { identifier: 'Address', columnName: 'AttackerIp' }
        ]
      }
    ]
    incidentConfiguration: {
      createIncident: true
      groupingConfiguration: {
        enabled: true
        reopenClosedIncident: false
        lookbackDuration: 'PT5H'
        matchingMethod: 'Selected'
        groupByEntities: ['IP'] // jeden atakujący IP = jeden incydent
      }
    }
    eventGroupingSettings: {
      aggregationKind: 'SingleAlert'
    }
  }
  dependsOn: [sentinelOnboarding, cowrieTable]
}

// (b) Rozproszony brute force na jedno konto — T1110, >5 IP / 1 h na konto.
resource ruleDistributedBruteForce 'Microsoft.SecurityInsights/alertRules@2023-11-01' = {
  scope: logAnalyticsWorkspace
  name: guid(logAnalyticsWorkspace.id, 'rule-distributed-brute-force')
  kind: 'Scheduled'
  properties: {
    displayName: 'HoneyGrid — rozproszony brute force na jedno konto'
    description: 'Ponad 5 unikalnych adresów IP atakuje to samo konto w oknie 1 godziny (botnet / proxy rotation). Próg wstępny — do strojenia po realnym ruchu. MITRE: T1110.001.'
    severity: 'Medium'
    enabled: true
    query: loadTextContent('../../sentinel/queries/distributed-brute-force.kql')
    queryFrequency: 'PT1H'
    queryPeriod: 'PT1H'
    triggerOperator: 'GreaterThan'
    triggerThreshold: 0
    suppressionEnabled: false
    suppressionDuration: 'PT1H'
    tactics: ['CredentialAccess']
    techniques: ['T1110']
    entityMappings: [
      {
        entityType: 'Account'
        fieldMappings: [
          { identifier: 'Name', columnName: 'Username' }
        ]
      }
    ]
    incidentConfiguration: {
      createIncident: true
      groupingConfiguration: {
        enabled: true
        reopenClosedIncident: false
        lookbackDuration: 'PT5H'
        matchingMethod: 'Selected'
        groupByEntities: ['Account'] // jedno atakowane konto = jeden incydent
      }
    }
    eventGroupingSettings: {
      aggregationKind: 'SingleAlert'
    }
  }
  dependsOn: [sentinelOnboarding, cowrieTable]
}

// (c) Password spraying — T1110, >8 kont / 1 h, <3 próby na konto.
resource rulePasswordSpraying 'Microsoft.SecurityInsights/alertRules@2023-11-01' = {
  scope: logAnalyticsWorkspace
  name: guid(logAnalyticsWorkspace.id, 'rule-password-spraying')
  kind: 'Scheduled'
  properties: {
    displayName: 'HoneyGrid — password spraying z jednego IP'
    description: 'Jeden adres IP próbuje ponad 8 różnych kont w oknie 1 godziny, średnio poniżej 3 prób na konto (spraying popularnych haseł, omijanie progów per-konto). Progi wstępne — do strojenia po realnym ruchu. MITRE: T1110.003.'
    severity: 'Medium'
    enabled: true
    query: loadTextContent('../../sentinel/queries/password-spraying.kql')
    queryFrequency: 'PT1H'
    queryPeriod: 'PT1H'
    triggerOperator: 'GreaterThan'
    triggerThreshold: 0
    suppressionEnabled: false
    suppressionDuration: 'PT1H'
    tactics: ['CredentialAccess']
    techniques: ['T1110']
    entityMappings: [
      {
        entityType: 'IP'
        fieldMappings: [
          { identifier: 'Address', columnName: 'AttackerIp' }
        ]
      }
    ]
    incidentConfiguration: {
      createIncident: true
      groupingConfiguration: {
        enabled: true
        reopenClosedIncident: false
        lookbackDuration: 'PT5H'
        matchingMethod: 'Selected'
        groupByEntities: ['IP']
      }
    }
    eventGroupingSettings: {
      aggregationKind: 'SingleAlert'
    }
  }
  dependsOn: [sentinelOnboarding, cowrieTable]
}

// (d) Udane logowanie po fali nieudanych — T1078, HIGH: bot wszedł do
// honeypota i zaraz zacznie post-exploitation (najcenniejszy sygnał projektu).
resource ruleSuccessAfterFailures 'Microsoft.SecurityInsights/alertRules@2023-11-01' = {
  scope: logAnalyticsWorkspace
  name: guid(logAnalyticsWorkspace.id, 'rule-success-after-failures')
  kind: 'Scheduled'
  properties: {
    displayName: 'HoneyGrid — udane logowanie po serii nieudanych prób'
    description: 'Udane logowanie z adresu IP, który wcześniej miał co najmniej 5 nieudanych prób w oknie 1 godziny — atakujący "wszedł" do honeypota, spodziewany post-exploitation. Próg wstępny — do strojenia po realnym ruchu. MITRE: T1078.'
    severity: 'High'
    enabled: true
    query: loadTextContent('../../sentinel/queries/success-after-failures.kql')
    queryFrequency: 'PT1H'
    queryPeriod: 'PT1H'
    triggerOperator: 'GreaterThan'
    triggerThreshold: 0
    suppressionEnabled: false
    suppressionDuration: 'PT1H'
    tactics: ['InitialAccess', 'Persistence']
    techniques: ['T1078']
    entityMappings: [
      {
        entityType: 'IP'
        fieldMappings: [
          { identifier: 'Address', columnName: 'AttackerIp' }
        ]
      }
      {
        entityType: 'Account'
        fieldMappings: [
          { identifier: 'Name', columnName: 'Username' }
        ]
      }
      {
        entityType: 'Host'
        fieldMappings: [
          { identifier: 'HostName', columnName: 'SensorId' }
        ]
      }
    ]
    incidentConfiguration: {
      createIncident: true
      groupingConfiguration: {
        enabled: true
        reopenClosedIncident: false
        lookbackDuration: 'PT5H'
        matchingMethod: 'Selected'
        groupByEntities: ['IP']
      }
    }
    eventGroupingSettings: {
      aggregationKind: 'SingleAlert'
    }
  }
  dependsOn: [sentinelOnboarding, cowrieTable]
}

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output logAnalyticsCustomerId string = logAnalyticsWorkspace.properties.customerId
output dataCollectionEndpointId string = dataCollectionEndpoint.id
output dataCollectionEndpointLogsIngestionUrl string = dataCollectionEndpoint.properties.logsIngestion.endpoint
output sentinelOnboardingId string = sentinelOnboarding.id

// Tydzień 4 — kontrakt konfiguracyjny workera Ingestion (Logs Ingestion API):
// te wartości trafiają przez main.bicep do zmiennych środowiskowych Ingestion__*.
output dcrImmutableId string = cowrieDcr.properties.immutableId
output dceLogsIngestionEndpoint string = dataCollectionEndpoint.properties.logsIngestion.endpoint
output dcrStreamName string = cowrieStreamName
output cowrieTableName string = cowrieTable.name

// Tydzień 5 — nazwy wyświetlane reguł analitycznych (do skryptu weryfikacyjnego).
output alertRuleNames array = [
  ruleBruteForceSingleIp.properties.displayName
  ruleDistributedBruteForce.properties.displayName
  rulePasswordSpraying.properties.displayName
  ruleSuccessAfterFailures.properties.displayName
]
