// ============================================================================
// HoneyGrid — moduł warstwy aplikacyjnej (Tydzień 2, Track A: realne sensory)
//
// Zasoby:
//   - Container Apps Environment (Consumption) ZINTEGROWANY z VNet (snet-dmz) —
//     sensory żyją logicznie w DMZ; ruch wychodzący ograniczany przez NSG dmz
//   - Azure Container Registry (Basic — najtańszy tier, wystarczy na obrazy projektu)
//   - Static Web App (Free) — dashboard threat-intel
//   - Application Insights podpięty do wspólnego Log Analytics (moduł sentinel.bicep)
//   - 3 Container Apps sensorów (Tydzień 2):
//       * cowrie       — 2 kontenery (Cowrie + sidecar CowrieShipper), TCP 22/23
//       * web-honeypot — fałszywy panel logowania HTTP, port 8080
//       * tcp-listener — generyczny nasłuch TCP 23/3389
//
// Wszystkie sensory: User-Assigned Managed Identity (pull z ACR + wysyłka do
// Event Hubs bezkluczowo), minReplicas = 1 (honeypot MUSI zawsze nasłuchiwać —
// scale-to-zero NIE dotyczy sensorów), CPU 0.25 / RAM 0.5Gi (minimalny koszt).
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Nazwa wspólnego workspace Log Analytics (z modułu sentinel.bicep).')
param logAnalyticsWorkspaceName string

@description('Resource ID podsieci snet-dmz — integracja VNet dla Container Apps Environment.')
param dmzSubnetId string = ''

@description('Resource ID User-Assigned Managed Identity sensorów (z modułu security.bicep).')
param sensorIdentityId string = ''

@description('Client ID tożsamości sensorów — używany przez DefaultAzureCredential w kontenerach.')
param sensorIdentityClientId string = ''

@description('Principal ID tożsamości sensorów — przypisanie roli AcrPull (pull obrazów z ACR).')
param sensorIdentityPrincipalId string = ''

@description('''Czy wdrażać Static Web App (dashboard, Track B). SWA jest dostępny TYLKO
w wybranych regionach (West Europe / East US 2 / Central US / West US 2 / East Asia),
więc gdy polityka subskrypcji wymusza inny region (np. Szwecja/Norwegia/Polska),
trzymamy go wyłączonym aż Track B znajdzie wspierany region. Domyślnie false.''')
param deployStaticWebApp bool = false

@description('Nazwa namespace Event Hubs (z modułu data.bicep) — do złożenia FQDN.')
param eventHubNamespaceName string = ''

@description('Nazwa Event Huba telemetrii (musi zgadzać się z data.bicep).')
param eventHubName string = 'honeypot-events'

var suffix = uniqueString(resourceGroup().id)

// FQDN namespace Event Hubs (AMQP/HTTPS) — przekazywany do sensorów jako zmienna
// środowiskowa. Pusty namespace => pusty FQDN (moduł kompiluje się też bez wiring).
var eventHubFqdn = empty(eventHubNamespaceName) ? '' : '${eventHubNamespaceName}.servicebus.windows.net'

// Czy mamy komplet danych do podpięcia tożsamości sensorów. Gdy moduł jest
// kompilowany w izolacji (bez parametrów), nie deklarujemy bloku identity,
// żeby nie wstrzykiwać pustego klucza.
var hasSensorIdentity = !empty(sensorIdentityId)

// Wspólny blok tożsamości (User-Assigned) dla wszystkich sensorów.
var sensorIdentityBlock = hasSensorIdentity
  ? {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${sensorIdentityId}': {}
      }
    }
  : {
      type: 'None'
    }

// Wspólny blok rejestru ACR (pull przez tożsamość sensorów — rola AcrPull
// nadana w Tygodniu 1, moduł rbac.bicep). Bez tożsamości => brak rejestru.
var registriesBlock = hasSensorIdentity
  ? [
      {
        server: containerRegistry.properties.loginServer
        identity: sensorIdentityId
      }
    ]
  : []

// Wspólne zasoby kontenera — świadomie minimalne. UWAGA KOSZTOWA: sensory mają
// minReplicas = 1 (zawsze działają), więc płacimy 24/7. 0.25 vCPU / 0.5 GiB to
// najniższa sensowna kombinacja w Container Apps (vCPU:GiB musi być 1:2).
var sensorCpu = json('0.25')
var sensorMemory = '0.5Gi'

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
// Container Apps Environment — plan Consumption, ZINTEGROWANY z VNet (snet-dmz)
//
// Integracja VNet (infrastructureSubnetId) wpina środowisko w podsieć snet-dmz,
// dzięki czemu sensory podlegają regułom NSG dmz (m.in. anty-pivot
// Deny-Outbound-All). Ruch wychodzący do Event Hubs jest dozwolony osobną
// regułą NSG (telemetria) — patrz network.bicep.
//
// internal = false: środowisko ma publiczny load balancer, bo sensory MUSZĄ
// być osiągalne z Internetu (to honeypoty). Sama podsieć i NSG kontrolują
// kierunek WYCHODZĄCY (anty-pivot), a nie blokują wejścia na porty przynęty.
//
// UWAGA (Consumption vs workload profiles): środowisko Consumption-only wymaga
// dla integracji VNet delegowanej podsieci /23 lub większej. snet-dmz to /24
// (10.20.0.0/24) — przy realnym wdrożeniu w Consumption-only może być konieczne
// poszerzenie podsieci LUB użycie środowiska z profilami obciążenia (workload
// profiles), które akceptuje /27. Patrz UWAGA / TODO przy aplikacji cowrie.
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
    // Profil Consumption (scale-to-zero, płatność za użycie, BEZ opłaty bazowej).
    // Typ "workload profiles" jest wymagany, by integracja VNet działała na podsieci
    // /24 — wariant "Consumption only" wymaga /23. Profil + delegacja snet-dmz =
    // poprawna konfiguracja sieci dla Container Apps.
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    // Integracja VNet — sensory logicznie w DMZ. Pomijana, gdy moduł jest
    // kompilowany w izolacji bez przekazanego dmzSubnetId.
    vnetConfiguration: empty(dmzSubnetId)
      ? null
      : {
          infrastructureSubnetId: dmzSubnetId
          internal: false
        }
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

// Sensor MI -> AcrPull (zakres: TEN rejestr). Przypisanie MUSI powstać tutaj
// (w module app), bo Container Apps ciągną obrazy tą tożsamością przy tworzeniu
// rewizji. Aplikacje poniżej mają `dependsOn: [sensorAcrPull]`, więc rola istnieje
// zanim platforma spróbuje pobrać obraz. (W rbac.bicep tej roli już NIE ma —
// tamten moduł zależy od app, więc wykonałby się ZA PÓŹNO.)
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
resource sensorAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(sensorIdentityPrincipalId)) {
  name: guid(containerRegistry.id, sensorIdentityPrincipalId, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: sensorIdentityPrincipalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: Container Apps sensorow ciagna obrazy z ACR (bezkluczowo).'
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
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = if (deployStaticWebApp) {
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

// ===========================================================================
// SENSORY — Container Apps (Tydzień 2, Track A)
//
// Wspólne założenia:
//   - obrazy z ACR przez Managed Identity (rola AcrPull nadana w Tygodniu 1),
//   - minReplicas = 1: honeypot MUSI zawsze nasłuchiwać (scale-to-zero NIE
//     dotyczy sensorów — gdyby zeszły do zera, ominęłyby je skany/ataki),
//   - 0.25 vCPU / 0.5 GiB (minimalny koszt; UWAGA: zawsze-on = płatność 24/7),
//   - zmienne środowiskowe karmią bezkluczowy EventHubShipper sensorów:
//       EventHubNamespaceFqdn, EventHubName, SensorId, SensorType,
//       AZURE_CLIENT_ID (wskazuje DefaultAzureCredential właściwą UAMI),
//       LocalLogOnly=false (w chmurze realnie wysyłamy do Event Hubs).
//
// UWAGA dot. tagów obrazów: tag :latest to PLACEHOLDER. Obrazy buduje człowiek
// w kroku wdrożenia: `az acr build --registry <acr> --image honeygrid-*:latest <kontekst>`.
// TODO (Tydzień 2): rozważyć tagowanie wersją/commitem zamiast :latest.
// ===========================================================================

// ---------------------------------------------------------------------------
// SENSOR 1: cowrie — wzorzec SIDECAR (2 kontenery w jednym Container App)
//
// DECYZJA ARCHITEKTONICZNA (sidecar vs. dwie osobne aplikacje):
// Wybieramy DWA KONTENERY w JEDNYM Container App:
//   * 'cowrie'          — honeypot SSH/Telnet (obraz honeygrid-cowrie),
//   * 'cowrie-shipper'  — worker .NET tail-ujący cowrie.json -> Event Hubs.
// Współdzielą wolumen 'cowrie-logs' (emptyDir): Cowrie pisze cowrie.json,
// shipper go czyta. Zaleta sidecara: log nie musi przechodzić przez sieć ani
// trwały storage — to lokalny strumień w obrębie poda. Współdzielenie emptyDir
// działa TYLKO w obrębie jednej repliki, dlatego maxReplicas = 1 (i tak chcemy
// pojedynczy sensor — proste korelowanie sesji).
// TRADEOFF odrzuconego wariantu (dwie osobne aplikacje + Azure Files na log):
// więcej ruchu/latencji SMB, ryzyko czytania niedopisanych linii, większy
// koszt transakcji Files. Sidecar jest czystszy dla strumienia tranzytowego.
//
// UWAGA / TODO (ekspozycja TCP 22 i 23 — REALNE OGRANICZENIE PLATFORMY):
// Ingress Container Apps pozwala na JEDEN port główny (transport tcp) + ewentualne
// additionalPortMappings. Jednak:
//   1) W środowisku Consumption-only `exposedPort` (mapowanie zewn. portu, np.
//      22) działa TYLKO dla ingresu 'tcp' i tylko dla JEDNEGO portu na aplikację;
//      wiele zewnętrznych portów TCP (22 ORAZ 23) na jednej aplikacji wymaga
//      additionalPortMappings, które są w pełni wspierane na środowiskach z
//      PROFILAMI OBCIĄŻENIA (workload profiles), a nie zawsze w Consumption-only.
//   2) Zewnętrzny ingress TCP otrzymuje LOSOWY port na publicznym FQDN, chyba
//      że ustawimy `exposedPort` — a wtedy i tak nie jest gwarantowane wystawienie
//      uprzywilejowanego 22/23 bez statycznego IP środowiska.
// WYBRANE PODEJŚCIE (best-effort, demo): wystawiamy SSH jako główny ingress TCP
// z exposedPort=22 (2222 wewnątrz). Telnet (23->2223) dodajemy jako
// additionalPortMappings — DZIAŁA pewnie na środowisku z workload profiles;
// na Consumption-only może wymagać migracji środowiska. Telnet/SSH na port 22/23
// dla demo obrony może też wymagać mapowania na STATYCZNY IP środowiska lub
// reguły NAT — patrz verify-week2.sh (przypomnienie o otwarciu portu na demo).
// To jest świadomy kompromis: bicep ma poprawny KSZTAŁT, a ograniczenie jest
// udokumentowane, nie przemilczane.
// ---------------------------------------------------------------------------
resource cowrieApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-${environment}-ca-cowrie'
  location: location
  tags: tags
  identity: sensorIdentityBlock
  // Rola AcrPull musi istnieć PRZED utworzeniem aplikacji (pull obrazu z ACR).
  dependsOn: [sensorAcrPull]
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registriesBlock
      ingress: {
        external: true
        transport: 'tcp'
        // Port główny: SSH. Wewnątrz Cowrie nasłuchuje na 2222, publicznie 22.
        targetPort: 2222
        exposedPort: 22
        // Telnet jako dodatkowe mapowanie: publiczny 23 -> wewnętrzny 2223.
        // (Patrz UWAGA / TODO wyżej co do wsparcia na Consumption-only.)
        additionalPortMappings: [
          {
            external: true
            targetPort: 2223
            exposedPort: 23
          }
        ]
      }
    }
    template: {
      volumes: [
        {
          // Wolumen współdzielony między Cowrie a sidecarem (strumień cowrie.json).
          name: 'cowrie-logs'
          storageType: 'EmptyDir'
        }
      ]
      containers: [
        {
          name: 'cowrie'
          image: '${containerRegistry.properties.loginServer}/honeygrid-cowrie:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          volumeMounts: [
            {
              volumeName: 'cowrie-logs'
              mountPath: '/cowrie/cowrie-git/var/log/cowrie'
            }
          ]
        }
        {
          name: 'cowrie-shipper'
          image: '${containerRegistry.properties.loginServer}/honeygrid-cowrie-shipper:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          env: [
            { name: 'HoneyGrid__EventHubFullyQualifiedNamespace', value: eventHubFqdn }
            { name: 'HoneyGrid__EventHubName', value: eventHubName }
            { name: 'HoneyGrid__SensorId', value: 'cowrie-eu-01' }
            { name: 'HoneyGrid__SensorType', value: 'cowrie' }
            { name: 'HoneyGrid__LocalLogOnly', value: 'false' }
            { name: 'AZURE_CLIENT_ID', value: sensorIdentityClientId }
            // Ścieżka do tail-owanego pliku — ten sam mount co u Cowrie.
            { name: 'CowrieShipper__LogPath', value: '/var/log/cowrie/cowrie.json' }
          ]
          volumeMounts: [
            {
              volumeName: 'cowrie-logs'
              mountPath: '/var/log/cowrie'
            }
          ]
        }
      ]
      scale: {
        // Honeypot zawsze-on: dokładnie jedna replika (emptyDir = brak współdzielenia
        // między replikami, więc i tak trzymamy max = 1).
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ---------------------------------------------------------------------------
// SENSOR 2: web-honeypot — fałszywy panel logowania (HTTP)
// Jeden kontener (obraz HoneyGrid.Sensors.Web). Ingress HTTP external, port 8080.
// Łapie skany typu /wp-login.php, /admin itp. i wysyła http.request do Event Hubs.
// ---------------------------------------------------------------------------
resource webHoneypotApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-${environment}-ca-web'
  location: location
  tags: tags
  identity: sensorIdentityBlock
  // Rola AcrPull musi istnieć PRZED utworzeniem aplikacji (pull obrazu z ACR).
  dependsOn: [sensorAcrPull]
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registriesBlock
      ingress: {
        external: true
        transport: 'auto'
        targetPort: 8080
        // Publiczne 80/443 obsługuje warstwa ingress (HTTPS terminowany przez
        // platformę). Kontener słucha czystego HTTP na 8080.
      }
    }
    template: {
      containers: [
        {
          name: 'web-honeypot'
          image: '${containerRegistry.properties.loginServer}/honeygrid-web:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          env: [
            { name: 'HoneyGrid__EventHubFullyQualifiedNamespace', value: eventHubFqdn }
            { name: 'HoneyGrid__EventHubName', value: eventHubName }
            { name: 'HoneyGrid__SensorId', value: 'web-eu-01' }
            { name: 'HoneyGrid__SensorType', value: 'web' }
            { name: 'HoneyGrid__LocalLogOnly', value: 'false' }
            { name: 'AZURE_CLIENT_ID', value: sensorIdentityClientId }
            // ASP.NET — nasłuch na 8080 (zgodny z targetPort ingressu).
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
          ]
        }
      ]
      scale: {
        // Zawsze-on: panel musi odpowiadać skanerom o każdej porze.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ---------------------------------------------------------------------------
// SENSOR 3: tcp-listener — generyczny nasłuch TCP (Telnet 23 + RDP 3389)
// Jeden kontener (obraz HoneyGrid.Sensors.Tcp). Ingress TCP external.
//
// UWAGA / TODO (jak przy cowrie): wiele zewnętrznych portów TCP (23 i 3389) na
// jednej aplikacji wymaga additionalPortMappings — pewnie wspierane na
// środowisku z workload profiles. Port główny: 23 (Telnet), 3389 jako dodatkowy.
// Na Consumption-only może wymagać migracji środowiska lub statycznego IP.
// ---------------------------------------------------------------------------
resource tcpListenerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-${environment}-ca-tcp'
  location: location
  tags: tags
  identity: sensorIdentityBlock
  // Rola AcrPull musi istnieć PRZED utworzeniem aplikacji (pull obrazu z ACR).
  dependsOn: [sensorAcrPull]
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registriesBlock
      ingress: {
        external: true
        transport: 'tcp'
        // Port główny: RDP (3389). Telnet (23) obsługuje już sensor Cowrie —
        // każdy zewnętrzny port TCP musi być UNIKALNY w obrębie środowiska
        // Container Apps, więc tcp-listener bierze tylko 3389 (uniknięcie kolizji
        // portu 23 z aplikacją cowrie).
        targetPort: 3389
        exposedPort: 3389
      }
    }
    template: {
      containers: [
        {
          name: 'tcp-listener'
          image: '${containerRegistry.properties.loginServer}/honeygrid-tcp:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          env: [
            { name: 'HoneyGrid__EventHubFullyQualifiedNamespace', value: eventHubFqdn }
            { name: 'HoneyGrid__EventHubName', value: eventHubName }
            { name: 'HoneyGrid__SensorId', value: 'tcp-eu-01' }
            { name: 'HoneyGrid__SensorType', value: 'tcp' }
            { name: 'HoneyGrid__LocalLogOnly', value: 'false' }
            { name: 'AZURE_CLIENT_ID', value: sensorIdentityClientId }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

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

output staticWebAppId string = deployStaticWebApp ? staticWebApp!.id : ''
output staticWebAppName string = deployStaticWebApp ? staticWebApp!.name : ''
output staticWebAppDefaultHostname string = deployStaticWebApp ? staticWebApp!.properties.defaultHostname : ''

output appInsightsId string = appInsights.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString

// Sensory (Tydzień 2, Track A) — nazwy i FQDN do weryfikacji / demo.
output cowrieAppName string = cowrieApp.name
output cowrieAppFqdn string = cowrieApp.properties.configuration.ingress.fqdn

output webHoneypotAppName string = webHoneypotApp.name
output webHoneypotAppFqdn string = webHoneypotApp.properties.configuration.ingress.fqdn

output tcpListenerAppName string = tcpListenerApp.name
output tcpListenerAppFqdn string = tcpListenerApp.properties.configuration.ingress.fqdn
