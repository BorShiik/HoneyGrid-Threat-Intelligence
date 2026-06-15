// ============================================================================
// HoneyGrid — moduł płaszczyzny bezpieczeństwa (Tydzień 1, Track A)
//
// Zasoby:
//   - User-Assigned Managed Identity dla sensorów honeypot (Cowrie / web /
//     tcp-listener) — w Tygodniu 2 zostanie podpięta jako tożsamość
//     Container Apps: wysyłka telemetrii do Event Hubs, zapis blobów,
//     pull obrazów z ACR (rola AcrPull)
//   - User-Assigned Managed Identity dla przyszłego playbooka SOAR
//     (Logic App, Tydzień 6) — blokowanie IP atakującego w NSG dmz,
//     aktualizacja incydentów Sentinela
//   - Key Vault w trybie RBAC (bez access policies) — filar architektury
//     bezkluczowej: aplikacje czytają sekrety przez Managed Identity
//     (przeniesiony z ai.bicep — Key Vault należy do płaszczyzny
//     bezpieczeństwa Track A zgodnie z podziałem pracy)
//
// Dlaczego User-Assigned (a nie System-Assigned): tożsamość żyje niezależnie
// od cyklu życia Container App — przypisania RBAC nie znikają przy
// przebudowie aplikacji, a jedna tożsamość obsługuje wiele sensorów.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

// Sufiks unikalności — IDENTYCZNY jak w ai.bicep przed przeniesieniem
// Key Vaulta (uniqueString z id grupy zasobów), żeby ponowne wdrożenie
// nie utworzyło duplikatu pod inną nazwą.
var suffix = uniqueString(resourceGroup().id)

var sensorIdentityName = '${namePrefix}-${environment}-id-sensor'
var playbookIdentityName = '${namePrefix}-${environment}-id-playbook'
var workerIdentityName = '${namePrefix}-${environment}-id-worker'
var apiIdentityName = '${namePrefix}-${environment}-id-api'
var functionsIdentityName = '${namePrefix}-${environment}-id-functions'
// Key Vault: nazwa globalna, max 24 znaki.
var keyVaultName = '${namePrefix}-${environment}-kv-${take(suffix, 8)}'

// ---------------------------------------------------------------------------
// Tożsamość zarządzana sensorów honeypot
// ---------------------------------------------------------------------------
resource sensorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: sensorIdentityName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Tożsamość zarządzana playbooka SOAR (Logic App — Tydzień 6)
// TODO (Tydzień 6, Track A): podpiąć jako identity playbooka
// "block-attacker-ip" (Logic App Standard/Consumption).
// ---------------------------------------------------------------------------
resource playbookIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: playbookIdentityName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Tożsamość zarządzana workera Ingestion/Enrichment (Tydzień 3, Track A)
// Worker żyje w strefie LOGIC i bezkluczowo: CZYTA z Event Hubs, ZAPISUJE
// checkpointy i surowe zdarzenia do Blob, ZAPISUJE dokumenty do Cosmos
// (rola płaszczyzny danych — data.bicep) i WYSYŁA zlecenia do Service Bus.
// Role ARM nadaje rbac.bicep; AcrPull nadaje app.bicep (przed Container App).
// ---------------------------------------------------------------------------
resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: workerIdentityName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Tożsamość zarządzana hosta API (Tydzień 7, Track A — killer-ficzy)
// Host API serwuje Session Replay (/api/sessions/{id}/replay) oraz eksport
// STIX/IoC (/api/iocs/stix). Tożsamość jest TYLKO DO ODCZYTU (least privilege):
// CZYTA dokumenty z Cosmos (events/iocs/sessions — rola płaszczyzny danych
// Cosmos Built-in Data READER w data.bicep) i CZYTA nagrania TTY z Blob
// (Storage Blob Data READER w rbac.bicep). AcrPull nadaje app.bicep (kolejność).
// Świadoma asymetria względem workera: worker pisze (Contributor), API czyta.
// ---------------------------------------------------------------------------
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: apiIdentityName
  location: location
  tags: tags
}

// Tożsamość Function App (Track B). User-assigned (NIE system-assigned), żeby
// principalId był znany na starcie wdrożenia i dało się go użyć w nazwach
// przypisań ról (guid()) — system-assigned principalId nie jest dostępny na
// starcie (błąd BCP120).
resource functionsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: functionsIdentityName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Key Vault — tryb RBAC, zero access policies (architektura bezkluczowa)
// ---------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    // Filar bezkluczowości: autoryzacja WYŁĄCZNIE przez Azure RBAC
    // (role "Key Vault Secrets User/Officer"), żadnych access policies.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7 // minimum — dev często kasuje/odtwarza
    enablePurgeProtection: environment == 'prod' ? true : null
    // Private Endpoint dla Key Vaulta tworzy moduł privatelink.bicep (Tydzień 1).
    // TODO (Tydzień 2, Track A+B): publicNetworkAccess: 'Disabled' po
    // zweryfikowaniu rozwiązywania nazw przez Private DNS — krok skoordynowany
    // z Track B (nie wyłączać jednostronnie).
    publicNetworkAccess: 'Enabled'
  }
}

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output sensorIdentityId string = sensorIdentity.id
output sensorIdentityPrincipalId string = sensorIdentity.properties.principalId
output sensorIdentityClientId string = sensorIdentity.properties.clientId

output playbookIdentityId string = playbookIdentity.id
output playbookIdentityPrincipalId string = playbookIdentity.properties.principalId

output workerIdentityId string = workerIdentity.id
output workerIdentityPrincipalId string = workerIdentity.properties.principalId
output workerIdentityClientId string = workerIdentity.properties.clientId

output apiIdentityId string = apiIdentity.id
output apiIdentityPrincipalId string = apiIdentity.properties.principalId
output apiIdentityClientId string = apiIdentity.properties.clientId

output functionsIdentityId string = functionsIdentity.id
output functionsIdentityPrincipalId string = functionsIdentity.properties.principalId
output functionsIdentityClientId string = functionsIdentity.properties.clientId

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
