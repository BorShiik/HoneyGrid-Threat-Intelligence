// ============================================================================
// HoneyGrid — moduł SIEM (Tydzień 0: szkielet)
//
// Zasoby:
//   - Log Analytics Workspace (PerGB2018) z DZIENNYM LIMITEM ingestii
//     (dailyQuotaGb, domyślnie 5 GB) — bezpiecznik kosztowy: Microsoft Sentinel
//     jest darmowy do 5 GB/dzień przez pierwsze 31 dni (trial), a limit chroni
//     budżet studencki przed zalewem telemetrii z honeypotów
//   - Onboarding Microsoft Sentinel (onboardingStates/default)
//   - Data Collection Endpoint (DCE) — realny już teraz, bo adres ingestii
//     jest potrzebny sensorom od pierwszego tygodnia
//   - Data Collection Rule (DCR) — STUB zakomentowany: schemat strumienia
//     Custom-CowrieStream i tabeli Cowrie_CL to praca Tygodnia 4
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

var workspaceName = '${namePrefix}-${environment}-law'
var dceName = '${namePrefix}-${environment}-dce'
// var dcrName = '${namePrefix}-${environment}-dcr-cowrie' // TODO (Tydzień 4): odkomentować razem z DCR

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
// STUB: Data Collection Rule (kind 'Direct') — Tydzień 4
// Schemat kolumn Cowrie_CL i transformacja KQL powstaną po analizie
// rzeczywistych logów Cowrie (eventid, src_ip, username, password, input...).
// Tożsamość wysyłająca dostanie rolę "Monitoring Metrics Publisher" na DCR
// (patrz moduł rbac.bicep).
// ---------------------------------------------------------------------------
// TODO (Tydzień 4, Track B): odkomentować i uzupełnić schemat.
// resource cowrieTable 'Microsoft.OperationalInsights/workspaces/tables@2023-09-01' = {
//   parent: logAnalyticsWorkspace
//   name: 'Cowrie_CL'
//   properties: {
//     schema: {
//       name: 'Cowrie_CL'
//       columns: [
//         { name: 'TimeGenerated', type: 'datetime' }
//         { name: 'EventId', type: 'string' }       // np. cowrie.login.failed
//         { name: 'SrcIp', type: 'string' }
//         { name: 'Username', type: 'string' }
//         { name: 'Password', type: 'string' }
//         { name: 'SessionId', type: 'string' }
//         { name: 'Input', type: 'string' }          // komenda wpisana przez atakującego
//         { name: 'RawData', type: 'dynamic' }
//       ]
//     }
//     retentionInDays: retentionInDays
//   }
// }
//
// resource cowrieDcr 'Microsoft.Insights/dataCollectionRules@2023-03-11' = {
//   name: dcrName
//   location: location
//   tags: tags
//   kind: 'Direct' // ingestia bezpośrednia przez Logs Ingestion API
//   properties: {
//     dataCollectionEndpointId: dataCollectionEndpoint.id
//     streamDeclarations: {
//       'Custom-CowrieStream': {
//         columns: [
//           { name: 'TimeGenerated', type: 'datetime' }
//           { name: 'EventId', type: 'string' }
//           { name: 'SrcIp', type: 'string' }
//           { name: 'Username', type: 'string' }
//           { name: 'Password', type: 'string' }
//           { name: 'SessionId', type: 'string' }
//           { name: 'Input', type: 'string' }
//           { name: 'RawData', type: 'dynamic' }
//         ]
//       }
//     }
//     destinations: {
//       logAnalytics: [
//         {
//           workspaceResourceId: logAnalyticsWorkspace.id
//           name: 'honeygridWorkspace'
//         }
//       ]
//     }
//     dataFlows: [
//       {
//         streams: [ 'Custom-CowrieStream' ]
//         destinations: [ 'honeygridWorkspace' ]
//         transformKql: 'source' // TODO: transformacja (parsowanie, wzbogacanie GeoIP)
//         outputStream: 'Custom-Cowrie_CL'
//       }
//     ]
//   }
//   dependsOn: [ cowrieTable ]
// }

// TODO (Tydzień 5, Track B): reguły analityczne Sentinela
// (Microsoft.SecurityInsights/alertRules) — brute-force, nowe IOC, anomalie GeoIP
// oraz automation rules wpinające playbooki (Logic Apps).

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output logAnalyticsCustomerId string = logAnalyticsWorkspace.properties.customerId
output dataCollectionEndpointId string = dataCollectionEndpoint.id
output dataCollectionEndpointLogsIngestionUrl string = dataCollectionEndpoint.properties.logsIngestion.endpoint
output sentinelOnboardingId string = sentinelOnboarding.id
