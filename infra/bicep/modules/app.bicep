// ============================================================================
// HoneyGrid — moduł warstwy aplikacyjnej (Tydzień 0: szkielet)
//
// Zasoby:
//   - Container Apps Environment (plan Consumption — SCALE-TO-ZERO: sensory
//     i API płacą tylko za faktyczne wykonanie; kluczowe dla budżetu)
//   - Azure Container Registry (Basic — najtańszy tier, wystarczy na obrazy projektu)
//   - Static Web App (Free) — dashboard threat-intel
//   - Application Insights podpięty do wspólnego Log Analytics (moduł sentinel.bicep)
//
// Same Container Apps (cowrie, web-honeypot, tcp-listener, api) to stuby
// na kolejne tygodnie — patrz TODO na dole pliku.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Nazwa wspólnego workspace Log Analytics (z modułu sentinel.bicep).')
param logAnalyticsWorkspaceName string

var suffix = uniqueString(resourceGroup().id)

var containerAppsEnvName = '${namePrefix}-${environment}-cae'
// ACR: tylko litery i cyfry, nazwa globalna.
var containerRegistryName = toLower('${namePrefix}${environment}acr${suffix}')
var staticWebAppName = '${namePrefix}-${environment}-swa'
var appInsightsName = '${namePrefix}-${environment}-appi'

// Odwołanie do istniejącego workspace (tworzy go moduł sentinel.bicep).
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

// ---------------------------------------------------------------------------
// Container Apps Environment — plan Consumption (scale-to-zero)
// ---------------------------------------------------------------------------
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        // Jedyny klucz w architekturze "bezkluczowej" — wymagany przez platformę
        // do wysyłki logów konsoli; nie wycieka poza płaszczyznę kontrolną Azure.
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
    // TODO (Tydzień 2, Track A): vnetConfiguration z infrastructureSubnetId =
    // podsieć snet-logic (sensory w DMZ logicznie, API w logic) + internal: true.
  }
}

// ---------------------------------------------------------------------------
// Azure Container Registry — Basic
// ---------------------------------------------------------------------------
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // Bezkluczowo: admin wyłączony — pull obrazów przez Managed Identity + rola AcrPull.
    adminUserEnabled: false
  }
}

// ---------------------------------------------------------------------------
// Application Insights (workspace-based, wspólny Log Analytics)
// ---------------------------------------------------------------------------
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---------------------------------------------------------------------------
// Static Web App — dashboard (Free tier, 100 GB transferu/mies. gratis)
// ---------------------------------------------------------------------------
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // TODO (Tydzień 5, Track B): podpiąć repo GitHub (repositoryUrl, branch)
    // albo deployment przez GitHub Actions z tokenem wdrożeniowym.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// PLACEHOLDERY: Container Apps (definicje per sensor/usługa)
// Wspólne założenia: minReplicas = 0 (scale-to-zero), obrazy z ACR przez
// Managed Identity (AcrPull), sekrety wyłącznie z Key Vault (moduł ai.bicep).
// ---------------------------------------------------------------------------

// TODO (Tydzień 1, Track A): sensor Cowrie (SSH/Telnet honeypot)
// resource cowrieApp 'Microsoft.App/containerApps@2024-03-01' = {
//   name: '${namePrefix}-${environment}-ca-cowrie'
//   location: location
//   tags: tags
//   identity: { type: 'SystemAssigned' }
//   properties: {
//     managedEnvironmentId: containerAppsEnvironment.id
//     configuration: {
//       ingress: { external: true, targetPort: 2222, transport: 'tcp', exposedPort: 22 }
//     }
//     template: {
//       containers: [ /* obraz cowrie z ACR + mount udziału Azure Files 'cowrie' */ ]
//       scale: { minReplicas: 0, maxReplicas: 1 } // scale-to-zero
//     }
//   }
// }

// TODO (Tydzień 1, Track A): web-honeypot (fałszywy panel logowania HTTP/HTTPS)
// resource webHoneypotApp 'Microsoft.App/containerApps@2024-03-01' = { ... porty 80/443 ... }

// TODO (Tydzień 2, Track A): tcp-listener (niskointerakcyjny nasłuch 23/3389)
// resource tcpListenerApp 'Microsoft.App/containerApps@2024-03-01' = { ... transport: 'tcp' ... }

// TODO (Tydzień 3, Track B): api (REST nad Cosmos: events/actors/iocs dla dashboardu)
// resource apiApp 'Microsoft.App/containerApps@2024-03-01' = { ... ingress internal,
//   dostęp do Cosmos przez Managed Identity (bez kluczy) ... }

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output containerAppsEnvironmentId string = containerAppsEnvironment.id
output containerAppsEnvironmentName string = containerAppsEnvironment.name
output containerAppsDefaultDomain string = containerAppsEnvironment.properties.defaultDomain

output containerRegistryId string = containerRegistry.id
output containerRegistryName string = containerRegistry.name
output containerRegistryLoginServer string = containerRegistry.properties.loginServer

output staticWebAppId string = staticWebApp.id
output staticWebAppName string = staticWebApp.name
output staticWebAppDefaultHostname string = staticWebApp.properties.defaultHostname

output appInsightsId string = appInsights.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
