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

// --- Tydzień 3+4, Track A: worker Ingestion/Enrichment ----------------------
// UCZCIWY KOMPROMIS (Tydzień 4, decyzja zatwierdzona): subskrypcja studencka
// (Azure for Students) NIE pozwala na dwa środowiska Container Apps w regionie
// (błąd wdrożenia: MaxNumberOfRegionalEnvironmentsInSubExceeded). Worker
// Ingestion przenosi się więc do JEDNEGO, wspólnego środowiska sensorów
// (snet-dmz). Izolacja DMZ/logic przechodzi z poziomu SIECI na poziom
// TOŻSAMOŚCI: osobne UAMI (id-sensor vs id-worker) i rozłączne role RBAC —
// kompromitacja sensora nie daje uprawnień workera (sensor MI nie ma ról
// danych Cosmos/odczytu EH). Kompromis udokumentowany, nie przemilczany;
// powrót do dwóch środowisk możliwy na pełnej subskrypcji (snet-logic i NSG
// logic zostają w network.bicep gotowe na ten scenariusz).

@description('Resource ID User-Assigned Managed Identity workera Ingestion (z modułu security.bicep).')
param workerIdentityId string = ''

@description('Client ID tożsamości workera — DefaultAzureCredential wybiera właściwą UAMI.')
param workerIdentityClientId string = ''

@description('Principal ID tożsamości workera — przypisanie roli AcrPull (pull obrazu z ACR).')
param workerIdentityPrincipalId string = ''

@description('Nazwa konta Storage (z modułu data.bicep) — do złożenia URI blob service workera.')
param storageAccountName string = ''

@description('Nazwa namespace Service Bus (z modułu data.bicep) — do złożenia FQDN.')
param serviceBusNamespaceName string = ''

@description('Endpoint dokumentowy Cosmos DB (z modułu data.bicep).')
param cosmosEndpoint string = ''

// --- Tydzień 7, Track A: host API (killer-ficzy: Session Replay + STIX/IoC) --
// Osobna tożsamość id-api TYLKO DO ODCZYTU (Cosmos Data Reader + Blob Data
// Reader) — kompromitacja API nie daje zapisu do danych. Puste => moduł
// kompiluje się w izolacji (brak bloku identity/rejestru, brak Container App).

@description('Resource ID User-Assigned Managed Identity hosta API (z modułu security.bicep).')
param apiIdentityId string = ''

@description('Client ID tożsamości API — DefaultAzureCredential wybiera właściwą UAMI.')
param apiIdentityClientId string = ''

@description('Principal ID tożsamości API — przypisanie roli AcrPull (pull obrazu z ACR).')
param apiIdentityPrincipalId string = ''

// --- Tydzień 4, Track A: wysyłka Cowrie_CL przez Logs Ingestion API ----------
// Trzy parametry z modułu sentinel.bicep (DCE/DCR). Domyślnie puste — moduł
// kompiluje się w izolacji, a sink .NET przy pustych wartościach robi no-op.

@description('Endpoint ingestii logów DCE (z modułu sentinel.bicep) — Logs Ingestion API.')
param dceLogsIngestionEndpoint string = ''

@description('ImmutableId reguły DCR cowrie (z modułu sentinel.bicep).')
param dcrImmutableId string = ''

@description('Nazwa strumienia wejściowego DCR (Custom-CowrieStream).')
param dcrStreamName string = ''

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

// Analogiczne bloki dla workera Ingestion (Tydzień 3) — osobna tożsamość,
// żeby kompromitacja sensora w DMZ nie dawała uprawnień warstwy logic.
var hasWorkerIdentity = !empty(workerIdentityId)

var workerIdentityBlock = hasWorkerIdentity
  ? {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${workerIdentityId}': {}
      }
    }
  : {
      type: 'None'
    }

var workerRegistriesBlock = hasWorkerIdentity
  ? [
      {
        server: containerRegistry.properties.loginServer
        identity: workerIdentityId
      }
    ]
  : []

// FQDN namespace Service Bus — pusty parametr => pusty FQDN (kompilacja w izolacji).
var serviceBusFqdn = empty(serviceBusNamespaceName) ? '' : '${serviceBusNamespaceName}.servicebus.windows.net'
// URI blob service workera (checkpointy EventProcessorClient + surowe zdarzenia).
// az.environment().suffixes.storage = core.windows.net w chmurze publicznej —
// bez hardkodowania sufiksu (linter no-hardcoded-env-urls); kwalifikator `az.`
// jest konieczny, bo parametr `environment` przesłania funkcję environment().
var blobServiceUri = empty(storageAccountName) ? '' : 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}'

// Analogiczne bloki dla hosta API (Tydzień 7) — osobna tożsamość id-api
// (tylko-do-odczytu). Pusty apiIdentityId => brak bloku identity/rejestru
// (kompilacja w izolacji bez wstrzykiwania pustego klucza).
var hasApiIdentity = !empty(apiIdentityId)

var apiIdentityBlock = hasApiIdentity
  ? {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${apiIdentityId}': {}
      }
    }
  : {
      type: 'None'
    }

var apiRegistriesBlock = hasApiIdentity
  ? [
      {
        server: containerRegistry.properties.loginServer
        identity: apiIdentityId
      }
    ]
  : []

var containerAppsEnvName = '${namePrefix}-${environment}-cae'
// UWAGA (Tydzień 4): drugie środowisko `...cae-logic` USUNIĘTE — limit
// subskrypcji (MaxNumberOfRegionalEnvironmentsInSubExceeded) wymusza JEDNO
// środowisko Container Apps na region. Patrz komentarz przy parametrach workera.
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
// (Tydzień 4) Tu stało DRUGIE środowisko Container Apps `...cae-logic`
// (strefa LOGIC, Tydzień 3). USUNIĘTE: wdrożenie padło na limicie
// MaxNumberOfRegionalEnvironmentsInSubExceeded — Azure for Students nie mieści
// dwóch środowisk w regionie. Worker Ingestion działa teraz we wspólnym
// środowisku sensorów (powyżej); izolację zapewniają osobne tożsamości
// i rozłączne role RBAC, nie osobne podsieci.
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

// Worker MI -> AcrPull (zakres: TEN rejestr). Ta sama lekcja co przy sensorach:
// rola MUSI powstać w module app PRZED utworzeniem Container App workera
// (aplikacja ma `dependsOn: [workerAcrPull]`) — rbac.bicep wykonuje się za późno.
resource workerAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerIdentityPrincipalId)) {
  name: guid(containerRegistry.id, workerIdentityPrincipalId, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: workerIdentityPrincipalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: worker Ingestion ciagnie obraz z ACR (bezkluczowo).'
  }
}

// API MI -> AcrPull (zakres: TEN rejestr). Ta sama lekcja co przy sensorach
// i workerze: rola MUSI powstać w module app PRZED utworzeniem Container App
// hosta API (aplikacja ma `dependsOn: [apiAcrPull]`) — rbac.bicep wykonuje się
// za późno (zależy od app).
resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(apiIdentityPrincipalId)) {
  name: guid(containerRegistry.id, apiIdentityPrincipalId, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: apiIdentityPrincipalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: host API ciagnie obraz honeygrid-api z ACR (bezkluczowo).'
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

// ---------------------------------------------------------------------------
// Azure SignalR Service — backplane realtime (Track B, Tydzień 2)
//
// Dashboard subskrybuje /hubs/attacks; nowe zdarzenia trafiają na front bez
// odświeżania. Usługa SignalR pełni rolę backplane'u: pozwala procesom innym
// niż API (funkcja Change Feed) rozsyłać komunikaty do podłączonych klientów.
//
// SKU Free_F1 (1 jednostka, 20 połączeń, 20 tys. wiadomości/dzień) — wystarcza
// na coursework/demo. Produkcyjnie: Standard_S1.
//
// ServiceMode:
//   * 'Default'    — hub żyje w API (AddSignalR().AddAzureSignalR), backplane.
//   * 'Serverless' — brak trwałego huba w API; broadcast przez output binding
//                    [SignalROutput] w funkcji + endpoint negotiate w API.
// Rekomendacja planu: 'Serverless' (mniej kodu dla Change Feed → SignalR).
// Bezkluczowo: dostęp przez Managed Identity (rola SignalR App Server / Owner).
// ---------------------------------------------------------------------------
var signalRName = '${namePrefix}-${environment}-sigr-${suffix}'

resource signalRService 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: signalRName
  location: location
  tags: tags
  sku: {
    name: 'Free_F1'
    tier: 'Free'
    capacity: 1
  }
  kind: 'SignalR'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // 'Serverless' rekomendowany (Track B): broadcast z funkcji przez binding.
    // Zmień na 'Default', jeśli hub ma żyć w procesie API (AddAzureSignalR).
    features: [
      {
        flag: 'ServiceMode'
        value: 'Serverless'
      }
    ]
    cors: {
      allowedOrigins: [
        '*'
      ]
    }
  }
}

// Rola płaszczyzny danych SignalR dla tożsamości API (negotiate + broadcast).
// 'SignalR App Server' = 420fcaa2-552c-430f-98ca-3264be4806c7.
// Gdy powstanie host funkcji (klasyfikacja/Change Feed), nadać tę samą rolę
// jego tożsamości — funkcja rozsyła zdarzenia do /hubs/attacks.
resource apiSignalRAppServer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(apiIdentityPrincipalId)) {
  name: guid(signalRService.id, apiIdentityPrincipalId, 'signalr-app-server')
  scope: signalRService
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '420fcaa2-552c-430f-98ca-3264be4806c7'
    )
    principalId: apiIdentityPrincipalId
    principalType: 'ServicePrincipal'
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

// ---------------------------------------------------------------------------
// WORKER: ingestion — konsument Event Hubs + wzbogacanie (Tydzień 3+4, Track A)
//
// Worker tła: CZYTA telemetrię z Event Hubs (EventProcessorClient, checkpointy
// w blob 'checkpoints'), ZAPISUJE surowe zdarzenia do blob 'raw', dokumenty
// do Cosmos (rola płaszczyzny danych — data.bicep), WYSYŁA zlecenia
// klasyfikacji do kolejki Service Bus 'ai-classify' oraz (Tydzień 4) pcha
// wzbogacone zdarzenia do tabeli Cowrie_CL przez Logs Ingestion API
// (DCE + DCR — moduł sentinel.bicep). Całość bezkluczowo przez id-worker.
//
// UCZCIWY KOMPROMIS (Tydzień 4): worker żyje we WSPÓLNYM środowisku sensorów
// (snet-dmz), bo limit subskrypcji studenckiej nie mieści drugiego środowiska
// (MaxNumberOfRegionalEnvironmentsInSubExceeded). Granicę DMZ/logic trzyma
// teraz tożsamość (osobna UAMI + rozłączne role), nie podsieć.
//
// BEZ ingressu — to czysty proces tła, nie przyjmuje żadnych połączeń
// (blok ingress celowo POMINIĘTY, nie pusty).
//
// Zmienne środowiskowe: worker .NET binduje sekcję konfiguracji "Ingestion" —
// podwójne podkreślenie to separator sekcji (Sekcja__Wlasciwosc). Nazwy MUSZĄ
// zgadzać się 1:1 z kontraktem konfiguracyjnym (realny błąd z Tygodnia 2:
// literówka w prefiksie = pusta konfiguracja bez żadnego błędu wdrożenia).
// Pozostałe ustawienia mają sensowne domyślne wartości w kodzie workera
// (ConsumerGroup=$Default, kontenery checkpoints/raw, baza honeygrid/events,
// kolejka ai-classify).
// ---------------------------------------------------------------------------
resource ingestionApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-${environment}-ca-ingestion'
  location: location
  tags: tags
  identity: workerIdentityBlock
  // Rola AcrPull workera musi istnieć PRZED utworzeniem aplikacji (pull obrazu).
  dependsOn: [workerAcrPull]
  properties: {
    // JEDNO środowisko (konsolidacja Tygodnia 4) — to samo co sensory.
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: workerRegistriesBlock
      // Brak bloku ingress — worker tła nie nasłuchuje na żadnym porcie.
    }
    template: {
      containers: [
        {
          name: 'ingestion'
          image: '${containerRegistry.properties.loginServer}/honeygrid-ingestion:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          env: [
            { name: 'Ingestion__EventHubFullyQualifiedNamespace', value: eventHubFqdn }
            { name: 'Ingestion__EventHubName', value: eventHubName }
            { name: 'Ingestion__BlobServiceUri', value: blobServiceUri }
            { name: 'Ingestion__CosmosEndpoint', value: cosmosEndpoint }
            { name: 'Ingestion__ServiceBusFullyQualifiedNamespace', value: serviceBusFqdn }
            // Tydzień 4: Logs Ingestion API (DCE/DCR -> tabela Cowrie_CL).
            // Nazwy MUSZĄ zgadzać się 1:1 z sekcją "Ingestion" w konfiguracji
            // workera .NET — literówka = cicha, pusta konfiguracja (lekcja T2).
            // Puste wartości => sink Sentinela robi no-op (kompilacja w izolacji).
            { name: 'Ingestion__DceLogsIngestionEndpoint', value: dceLogsIngestionEndpoint }
            { name: 'Ingestion__DcrImmutableId', value: dcrImmutableId }
            { name: 'Ingestion__DcrStreamName', value: dcrStreamName }
            { name: 'AZURE_CLIENT_ID', value: workerIdentityClientId }
          ]
        }
      ]
      scale: {
        // Ciągły konsument strumienia: dokładnie JEDNA replika — zero replik
        // gubiłoby zdarzenia (Basic = 1 dzień retencji), a wiele replik
        // z $Default consumer group biłoby się o te same partycje.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ---------------------------------------------------------------------------
// HOST API: ca-api — killer-ficzy Track A (Tydzień 7)
//
// Dwa endpointy demonstracyjne (wstępnie host Track A, docelowo zlewa się
// z API Track B):
//   * GET /api/iocs/stix          — eksport bundle STIX 2.1 (HoneyGrid.Stix),
//   * GET /api/sessions/{id}/replay — odtworzenie sesji TTY (HoneyGrid.Replay).
// Host CZYTA Cosmos (events/iocs/sessions) i nagrania TTY z Blob bezkluczowo
// przez id-api (Cosmos Data Reader + Blob Data Reader — least privilege).
//
// SKALOWANIE — INACZEJ NIŻ SENSORY/WORKER: API jest sterowane ŻĄDANIAMI,
// więc minReplicas = 0 (scale-to-zero). Gdy nikt nie pyta o STIX/replay,
// nie płacimy za nic — w przeciwieństwie do sensorów (zawsze-on, nasłuch
// 24/7) i workera (ciągły konsument strumienia). Pierwsze żądanie po
// uśpieniu ma zimny start — akceptowalne dla API analitycznego/demo.
//
// Ingress HTTP external (dashboard SOC + curl na demo), targetPort 8080,
// transport 'auto' (platforma terminuje HTTPS, kontener słucha HTTP na 8080).
// ---------------------------------------------------------------------------
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-${environment}-ca-api'
  location: location
  tags: tags
  identity: apiIdentityBlock
  // Rola AcrPull API musi istnieć PRZED utworzeniem aplikacji (pull obrazu).
  dependsOn: [apiAcrPull]
  properties: {
    // JEDNO środowisko (konsolidacja Tygodnia 4) — to samo co sensory i worker.
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: apiRegistriesBlock
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
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/honeygrid-api:latest'
          resources: {
            cpu: sensorCpu
            memory: sensorMemory
          }
          env: [
            // API binduje sekcję konfiguracji "HoneyGrid" (prefiks sekcji,
            // podwójne podkreślenie = separator). Endpoint Cosmos + baza,
            // URI blob service (nagrania TTY) — wszystko czytane bezkluczowo.
            { name: 'HoneyGrid__CosmosEndpoint', value: cosmosEndpoint }
            { name: 'HoneyGrid__CosmosDatabase', value: 'honeygrid' }
            { name: 'HoneyGrid__BlobServiceUri', value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}' }
            // Endpoint Azure SignalR Service (backplane realtime, Track B).
            { name: 'HoneyGrid__SignalREndpoint', value: 'https://${signalRService.properties.hostName}' }
            // DefaultAzureCredential wybiera właściwą UAMI (id-api) po Client ID.
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            // ASP.NET — nasłuch na 8080 (zgodny z targetPort ingressu).
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
          ]
        }
      ]
      scale: {
        // Scale-to-zero: API sterowane żądaniami (inaczej niż sensory/worker).
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

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

// Azure SignalR Service (Track B, realtime) — nazwa + endpoint dla API/funkcji.
output signalRName string = signalRService.name
output signalREndpoint string = 'https://${signalRService.properties.hostName}'

// Sensory (Tydzień 2, Track A) — nazwy i FQDN do weryfikacji / demo.
output cowrieAppName string = cowrieApp.name
output cowrieAppFqdn string = cowrieApp.properties.configuration.ingress.fqdn

output webHoneypotAppName string = webHoneypotApp.name
output webHoneypotAppFqdn string = webHoneypotApp.properties.configuration.ingress.fqdn

output tcpListenerAppName string = tcpListenerApp.name
output tcpListenerAppFqdn string = tcpListenerApp.properties.configuration.ingress.fqdn

// Worker Ingestion (Tydzień 3+4, Track A) — bez FQDN (brak ingressu).
// Wyjście logicEnvironmentId USUNIĘTE (Tydzień 4) — drugie środowisko nie
// istnieje; worker działa w containerAppsEnvironmentId (wyżej).
output ingestionAppName string = ingestionApp.name

// Host API (Tydzień 7, Track A) — nazwa + FQDN ingressu (curl /health, /api/iocs/stix).
output apiAppName string = apiApp.name
output apiAppFqdn string = apiApp.properties.configuration.ingress.fqdn
