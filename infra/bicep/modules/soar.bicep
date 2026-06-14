// ============================================================================
// HoneyGrid — moduł SOAR (Tydzień 6, Track A)
//
// Cel: playbook (Logic App Consumption), który uruchamia się, gdy Microsoft
// Sentinel TWORZY incydent, wyciąga encję IP atakującego i AUTOMATYCZNIE
// mityguje zagrożenie:
//   (1) dopisuje regułę Deny do NSG strefy DMZ,
//   (2) dopisuje IP do feedu EDL (blob blocked-ips.txt),
//   (3) wysyła powiadomienie POST na webhook operatora (kanał powiadomień —
//       NIE Teams),
//   (4) komentuje i zamyka incydent w Sentinelu.
//
// Bezkluczowo: Logic App używa ISTNIEJĄCEJ tożsamości user-assigned
// hg-{env}-id-playbook (z Tygodnia 1). Ma już: Sentinel Responder na RG,
// Network Contributor zawężony do NSG DMZ; rolę Storage Blob Data Contributor
// (zapis EDL) dodaje równolegle inny moduł. Połączenie do Sentinela też idzie
// przez tę tożsamość (parameterValueType 'Alternative'), bez sekretów.
//
// Wszystkie zasoby są ZAGUARDOWANE — moduł kompiluje się i wdraża z pustymi
// parametrami (standalone), więc nadaje się do kompilacji w izolacji.
//
// ----------------------------------------------------------------------------
// WIRING w main.bicep (do wpięcia przez integratora — dokładny snippet):
//
//   // nowy parametr główny (webhook operatora, np. https://webhook.site/<uuid>)
//   @description('Webhook HTTP do powiadomień SOAR; pusty => krok powiadomienia pominięty.')
//   param notifyWebhookUrl string = ''
//
//   module soar 'modules/soar.bicep' = {
//     name: 'soar'
//     params: {
//       environment:         environment
//       namePrefix:          namePrefix
//       location:            location
//       tags:                tags
//       playbookIdentityId:  security.outputs.playbookIdentityId
//       dmzNsgName:          network.outputs.dmzNsgName
//       storageAccountName:  data.outputs.storageAccountName
//       workspaceName:       sentinel.outputs.logAnalyticsWorkspaceName
//       notifyWebhookUrl:    notifyWebhookUrl
//     }
//   }
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('''resourceId tożsamości UAMI hg-{env}-id-playbook
(security.outputs.playbookIdentityId). Pusty => Logic App bez bloku identity
i automation rule się nie wdraża (moduł kompiluje się w izolacji).''')
param playbookIdentityId string = ''

@description('Nazwa NSG strefy DMZ, np. hg-dev-nsg-dmz (network.outputs.dmzNsgName). Cel reguł Deny.')
param dmzNsgName string = ''

@description('Nazwa konta storage dla feedu EDL (data.outputs.storageAccountName).')
param storageAccountName string = ''

@description('''Nazwa workspace Log Analytics, np. hg-{env}-law
(sentinel.outputs.logAnalyticsWorkspaceName). Potrzebna do połączenia API
Sentinela oraz do zakresu automation rule.''')
param workspaceName string = ''

@description('''Webhook HTTP do powiadomień (np. webhook.site). Pusty =>
krok powiadomienia w playbooku jest pomijany (warunek w workflow).''')
param notifyWebhookUrl string = ''

@description('''objectId service principala "Azure Security Insights" w tym tenancie
(az ad sp list --filter "appId eq '98785600-1bb7-4fb9-b9fa-19afe2c8a360'" --query "[0].id" -o tsv).
Sentinel musi mieć uprawnienia na playbooku, by automation rule mogła go uruchomić.
PUSTY => automation rule NIE jest wdrażana (playbook działa, uruchamiasz go ręcznie
z incydentu) — wdrożenie nie blokuje się na braku tego objectId.''')
param sentinelAutomationPrincipalId string = ''

// ---------------------------------------------------------------------------
// Nazwy i wartości pochodne
// ---------------------------------------------------------------------------
var sentinelConnectionName = '${namePrefix}-${environment}-con-sentinel'
var playbookName = '${namePrefix}-${environment}-pb-block-ip'

// Parametr `environment` przesłania funkcję environment() — używamy kwalifikatora az.
var managedApiSentinelId = '${subscription().id}/providers/Microsoft.Web/locations/${location}/managedApis/azuresentinel'

// resourceId NSG DMZ budujemy lokalnie (bez cross-module zależności).
// Pusty dmzNsgName => pusty resourceId (kompilacja w izolacji).
var nsgResourceId = empty(dmzNsgName) ? '' : resourceId('Microsoft.Network/networkSecurityGroups', dmzNsgName)

// Czy mamy tożsamość playbooka — analogicznie do guardów w app.bicep.
var hasPlaybookIdentity = !empty(playbookIdentityId)

// Blok identity (User-Assigned) Logic Appa; bez tożsamości => 'None',
// żeby nie wstrzykiwać pustego klucza userAssignedIdentities (jak w app.bicep).
var playbookIdentityBlock = hasPlaybookIdentity
  ? {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${playbookIdentityId}': {}
      }
    }
  : {
      type: 'None'
    }

// ---------------------------------------------------------------------------
// Połączenie API do Microsoft Sentinel — uwierzytelnianie przez Managed
// Identity (bezkluczowo: połączenie używa tożsamości playbooka, nie sekretu).
// parameterValueType 'Alternative' to mechanizm konektora Sentinela na MSI.
// ---------------------------------------------------------------------------
resource sentinelConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: sentinelConnectionName
  location: location
  tags: tags
  properties: {
    displayName: sentinelConnectionName
    // 'Alternative' => konektor Sentinela uwierzytelnia się tożsamością
    // zarządzaną Logic Appa zamiast OAuth/sekretem. Bicep nie zna tej
    // właściwości w modelu typów (BCP037), ale jest to oficjalny mechanizm
    // MSI konektora Sentinela — świadomie wyciszamy ostrzeżenie.
    #disable-next-line BCP037
    parameterValueType: 'Alternative'
    api: {
      id: managedApiSentinelId
    }
  }
}

// ---------------------------------------------------------------------------
// Logic App (Consumption) — playbook "block-ip".
// Definicja workflow żyje w osobnym pliku JSON (jedyne źródło prawdy,
// recenzowane jak pliki .kql) i jest ładowana w czasie kompilacji.
// Konkretne wartości (resourceId NSG, storage, webhook itd.) wstrzykujemy
// w blok `parameters` workflow — JSON trzyma tylko bezpieczne defaulty.
// ---------------------------------------------------------------------------
// Logic App (Consumption) = Microsoft.Logic/workflows (NIE Microsoft.Web — to był
// błąd: poprawny namespace to Microsoft.Logic). Bicep ma model typów dla tego zasobu.
resource playbook 'Microsoft.Logic/workflows@2019-05-01' = {
  name: playbookName
  location: location
  tags: tags
  identity: playbookIdentityBlock
  properties: {
    state: 'Enabled'
    definition: loadJsonContent('../../soar/playbook-block-ip.json')
    parameters: {
      // Wiązanie połączenia Sentinela — uwierzytelnianie tożsamością playbooka
      // (connectionProperties.authentication = ManagedServiceIdentity).
      '$connections': {
        value: {
          azuresentinel: {
            connectionId: sentinelConnection.id
            connectionName: sentinelConnectionName
            id: managedApiSentinelId
            connectionProperties: {
              authentication: {
                type: 'ManagedServiceIdentity'
                identity: playbookIdentityId
              }
            }
          }
        }
      }
      // Konkretne wartości środowiska wstrzyknięte z Bicepa do workflow.
      nsgResourceId: {
        value: nsgResourceId
      }
      subscriptionId: {
        value: subscription().subscriptionId
      }
      resourceGroup: {
        value: resourceGroup().name
      }
      storageAccountName: {
        value: storageAccountName
      }
      edlContainer: {
        value: 'edl'
      }
      edlBlob: {
        value: 'blocked-ips.txt'
      }
      webhookUrl: {
        value: notifyWebhookUrl
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Automation rule Sentinela — wpina playbook do incydentów (tu, a nie w
// sentinel.bicep, żeby uniknąć edycji między modułami). Odpala się przy
// UTWORZENIU incydentu o severity High/Medium i uruchamia playbook.
//
// Cały zasób zaguardowany: wdraża się tylko, gdy mamy workspace i tożsamość
// playbooka — inaczej moduł kompiluje się/wdraża z pustymi parametrami.
// ---------------------------------------------------------------------------
resource existingWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = if (!empty(workspaceName)) {
  name: empty(workspaceName) ? 'placeholder' : workspaceName
}

// Sentinel (SP "Azure Security Insights") MUSI mieć uprawnienia na playbooku,
// inaczej tworzenie automation rule pada: "Missing required permissions for
// Microsoft Sentinel on the playbook". Nadajemy rolę Microsoft Sentinel
// Automation Contributor ZAWĘŻONĄ do tego playbooka i sprawiamy, że automation
// rule zależy od tego przypisania (kolejność — lekcja z AcrPull).
var sentinelAutomationContributor = 'f4c81013-99ee-4d62-a7ee-b3f1f648599a'
var canDeployAutomation = !empty(workspaceName) && !empty(playbookIdentityId) && !empty(sentinelAutomationPrincipalId)

resource sentinelPlaybookPermission 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (canDeployAutomation) {
  scope: playbook
  name: guid(playbook.id, sentinelAutomationPrincipalId, sentinelAutomationContributor)
  properties: {
    principalId: sentinelAutomationPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sentinelAutomationContributor)
    principalType: 'ServicePrincipal'
    description: 'HoneyGrid: Sentinel uruchamia playbook block-ip (zakres: ten playbook).'
  }
}

resource automationRule 'Microsoft.SecurityInsights/automationRules@2023-11-01' = if (canDeployAutomation) {
  scope: existingWorkspace
  dependsOn: [sentinelPlaybookPermission]
  name: guid(playbookName, 'automation-rule')
  properties: {
    displayName: 'HoneyGrid — auto-mitygacja: blokuj IP atakującego'
    order: 1
    triggeringLogic: {
      isEnabled: true
      triggersOn: 'Incidents'
      triggersWhen: 'Created'
      conditions: [
        {
          conditionType: 'Property'
          conditionProperties: {
            propertyName: 'IncidentSeverity'
            // Dla IncidentSeverity dozwolone są tylko Equals/NotEquals (Contains
            // działa na polach tekstowych jak tytuł). 'Equals' z listą wartości =
            // "severity należy do {High, Medium}".
            operator: 'Equals'
            propertyValues: [
              'High'
              'Medium'
            ]
          }
        }
      ]
    }
    actions: [
      {
        order: 1
        actionType: 'RunPlaybook'
        actionConfiguration: {
          logicAppResourceId: playbook.id
          tenantId: subscription().tenantId
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output playbookName string = playbook.name
output playbookResourceId string = playbook.id
output sentinelConnectionName string = sentinelConnection.name
output automationRuleName string = (!empty(workspaceName) && !empty(playbookIdentityId)) ? automationRule.name : ''
