// ============================================================================
// HoneyGrid — moduł Private Link (Tydzień 1, Track A)
//
// Warstwa danych dostępna wyłącznie przez sieć prywatną: strefy Private DNS
// + Private Endpoints w podsieci snet-data dla trzech usług:
//   - Cosmos DB      (strefa privatelink.documents.azure.com,  groupId 'Sql')
//   - Blob Storage   (strefa privatelink.blob.<sufiks storage>, groupId 'blob')
//   - Key Vault      (strefa privatelink.vaultcore.azure.net,  groupId 'vault')
//
// ============================================================================
// ŚWIADOMY KOMPROMIS KOSZTOWY — Event Hubs i Service Bus BEZ Private Endpoint:
//
//   - Event Hubs w tierze BASIC NIE wspiera Private Endpoints — wymagany
//     jest tier Standard lub wyższy.
//   - Service Bus wspiera Private Endpoints dopiero w tierze PREMIUM
//     (~670 USD/mies.) — całkowicie poza budżetem Azure for Students.
//
//   Telemetria z sensorów DMZ do Event Hubs i tak płynie szyfrowana (TLS,
//   porty 443/5671/5672), a NSG dmz dopuszcza outbound WYŁĄCZNIE do tagu
//   usługi 'EventHub' (reguła Allow-Outbound-Telemetry-EventHub w
//   network.bicep) — powierzchnia ataku jest więc kontrolowana mimo braku PE.
//
//   TODO (opcjonalnie): podniesienie Event Hubs do Standard (~$22/mies.)
//   umożliwiłoby Private Endpoint
// ============================================================================
//
// KOSZT Private Endpoints: ~7 USD/mies. za endpoint (3 PE ≈ 22 USD/mies.) —
// kasować RG między sesjami pracy (`az group delete -n hg-dev-rg --no-wait`),
// szkielet odtwarza się idempotentnie jednym wdrożeniem.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Id VNet (z modułu network.bicep) — link stref Private DNS.')
param vnetId string

@description('Nazwa VNet — używana w nazwach linków virtualNetworkLinks.')
param vnetName string

@description('Id podsieci snet-data — tu lądują Private Endpoints.')
param dataSubnetId string

@description('Nazwa konta Cosmos DB (z modułu data.bicep).')
param cosmosAccountName string

@description('Nazwa konta Storage (z modułu data.bicep).')
param storageAccountName string

@description('Nazwa Key Vaulta (z modułu security.bicep).')
param keyVaultName string

// ---------------------------------------------------------------------------
// Odwołania do istniejących zasobów (ta sama RG) — potrzebujemy ich id
// w privateLinkServiceConnections, bez przeciągania całych obiektów przez params.
// ---------------------------------------------------------------------------
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// ---------------------------------------------------------------------------
// Strefy Private DNS (globalne) + linki do VNet
// registrationEnabled: false — rekordy A wpisują privateDnsZoneGroups
// z Private Endpointów, nie automatyczna rejestracja maszyn.
// ---------------------------------------------------------------------------
resource cosmosDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.documents.azure.com'
  location: 'global'
  tags: tags
}

resource cosmosDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: cosmosDnsZone
  name: '${vnetName}-cosmos-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

// Sufiks domeny blob z funkcji środowiskowej (przenośność między chmurami).
resource blobDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.blob.${az.environment().suffixes.storage}'
  location: 'global'
  tags: tags
}

resource blobDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: blobDnsZone
  name: '${vnetName}-blob-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

resource keyVaultDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
  tags: tags
}

resource keyVaultDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: keyVaultDnsZone
  name: '${vnetName}-vault-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint: Cosmos DB (groupId 'Sql')
// ---------------------------------------------------------------------------
resource cosmosPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-${environment}-pe-cosmos'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: dataSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${namePrefix}-${environment}-pe-cosmos-conn'
        properties: {
          privateLinkServiceId: cosmosAccount.id
          groupIds: [
            'Sql'
          ]
        }
      }
    ]
  }
}

resource cosmosPeDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: cosmosPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'cosmos'
        properties: {
          privateDnsZoneId: cosmosDnsZone.id
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint: Blob Storage (groupId 'blob')
// TODO (Tydzień 2, Track A): osobny PE z groupId 'file' (Azure Files / SMB
// dla persystencji Cowrie), jeśli udział ma być montowany przez sieć prywatną.
// ---------------------------------------------------------------------------
resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-${environment}-pe-blob'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: dataSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${namePrefix}-${environment}-pe-blob-conn'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource blobPeDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: {
          privateDnsZoneId: blobDnsZone.id
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint: Key Vault (groupId 'vault')
// ---------------------------------------------------------------------------
resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: '${namePrefix}-${environment}-pe-kv'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: dataSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${namePrefix}-${environment}-pe-kv-conn'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: [
            'vault'
          ]
        }
      }
    ]
  }
}

resource keyVaultPeDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'vault'
        properties: {
          privateDnsZoneId: keyVaultDnsZone.id
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// UWAGA: publicNetworkAccess na Cosmos/Storage/Key Vault zostaje 'Enabled'.
// TODO (Tydzień 2, Track A+B): przełączenie na 'Disabled' to krok
// SKOORDYNOWANY z Track B — najpierw weryfikacja, że Private Endpoints
// rozwiązują się poprawnie (nslookup z warstwy logic), potem flip.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output cosmosPrivateEndpointId string = cosmosPrivateEndpoint.id
output blobPrivateEndpointId string = blobPrivateEndpoint.id
output keyVaultPrivateEndpointId string = keyVaultPrivateEndpoint.id

output cosmosPrivateDnsZoneId string = cosmosDnsZone.id
output blobPrivateDnsZoneId string = blobDnsZone.id
output keyVaultPrivateDnsZoneId string = keyVaultDnsZone.id
