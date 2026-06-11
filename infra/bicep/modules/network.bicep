// ============================================================================
// HoneyGrid — moduł sieciowy (hub-and-spoke, Tydzień 0: szkielet)
//
// Topologia: jeden VNet z trzema podsieciami-strefami:
//   dmz   — sensory honeypot (Cowrie, web-honeypot, tcp-listener); wystawione na Internet
//   logic — warstwa przetwarzania (Container Apps, API); BEZ publicznych IP
//   data  — Private Endpoints do Cosmos/Storage/Service Bus (Private Link)
//
// Kluczowa decyzja architektoniczna (anty-pivot): skompromitowany sensor
// w DMZ NIE MOŻE pivotować do warstwy analitycznej — outbound z DMZ jest
// domyślnie ZABLOKOWANY, z wyjątkiem kanału telemetrii (Event Hubs / Azure Monitor).
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Przestrzeń adresowa VNet.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('Podsieć DMZ — sensory honeypot.')
param dmzSubnetPrefix string = '10.20.0.0/24'

@description('Podsieć logic — warstwa przetwarzania.')
param logicSubnetPrefix string = '10.20.1.0/24'

@description('Podsieć data — Private Endpoints.')
param dataSubnetPrefix string = '10.20.2.0/24'

// ---------------------------------------------------------------------------
// Zmienne nazewnicze
// ---------------------------------------------------------------------------
var vnetName = '${namePrefix}-${environment}-vnet'
var dmzNsgName = '${namePrefix}-${environment}-nsg-dmz'
var logicNsgName = '${namePrefix}-${environment}-nsg-logic'
var dataNsgName = '${namePrefix}-${environment}-nsg-data'

// Porty przynęty otwarte na świat — celowo: SSH, Telnet, HTTP(S), RDP
// oraz 2222 (alternatywny SSH dla Cowrie).
var honeypotPorts = [
  '22'
  '23'
  '80'
  '443'
  '3389'
  '2222'
]

// ---------------------------------------------------------------------------
// NSG: DMZ — przyjmij wszystko na portach przynęty, NIE wypuszczaj nic poza telemetrią
// ---------------------------------------------------------------------------
resource dmzNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: dmzNsgName
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-Inbound-Honeypot-Ports'
        properties: {
          description: 'Porty przynety - celowo otwarte na Internet (to jest honeypot).'
          direction: 'Inbound'
          access: 'Allow'
          priority: 100
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: dmzSubnetPrefix
          destinationPortRanges: honeypotPorts
        }
      }
      {
        // PLACEHOLDER kanału telemetrii: tag usługi EventHub.
        // TODO (Tydzień 2, Track A): zawęzić do konkretnego namespace przez
        // Private Endpoint / service tag regionalny (EventHub.WestEurope).
        name: 'Allow-Outbound-Telemetry-EventHub'
        properties: {
          description: 'Jedyny dozwolony ruch wychodzacy: telemetria do Event Hubs (AMQP/HTTPS).'
          direction: 'Outbound'
          access: 'Allow'
          priority: 100
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: 'EventHub'
          destinationPortRanges: [
            '443'
            '5671'
            '5672'
          ]
        }
      }
      {
        // Telemetria platformowa (agent Azure Monitor -> DCE).
        name: 'Allow-Outbound-AzureMonitor'
        properties: {
          description: 'Logi agenta do Azure Monitor / DCE.'
          direction: 'Outbound'
          access: 'Allow'
          priority: 110
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: 'AzureMonitor'
          destinationPortRange: '443'
        }
      }
      // Platforma Container Apps wymaga ruchu wychodzacego do swoich zaleznosci,
      // by uruchomic sensory: pobranie obrazu z ACR, obrazy systemowe (MCR przez
      // Front Door), uwierzytelnienie (AAD), storage srodowiska. KAZDY tag uslugi
      // MUSI byc osobna regula (NSG nie pozwala laczyc tagow w jednym prefixie).
      // Anty-pivot zostaje: brak wyjscia do dowolnego Internetu ani do podsieci VNet.
      {
        name: 'Allow-Outbound-ACR'
        properties: {
          description: 'Pobranie obrazow sensorow z Azure Container Registry.'
          direction: 'Outbound'
          access: 'Allow'
          priority: 120
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationPortRange: '443'
          destinationAddressPrefix: 'AzureContainerRegistry'
        }
      }
      {
        name: 'Allow-Outbound-MCR'
        properties: {
          description: 'Obrazy systemowe Container Apps z Microsoft Container Registry.'
          direction: 'Outbound'
          access: 'Allow'
          priority: 130
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationPortRange: '443'
          destinationAddressPrefix: 'MicrosoftContainerRegistry'
        }
      }
      {
        name: 'Allow-Outbound-FrontDoor'
        properties: {
          description: 'Zaleznosc MCR (dystrybucja przez Azure Front Door).'
          direction: 'Outbound'
          access: 'Allow'
          priority: 140
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationPortRange: '443'
          destinationAddressPrefix: 'AzureFrontDoor.FirstParty'
        }
      }
      {
        name: 'Allow-Outbound-AAD'
        properties: {
          description: 'Uwierzytelnienie Managed Identity (token AAD do ACR/Event Hubs).'
          direction: 'Outbound'
          access: 'Allow'
          priority: 150
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationPortRange: '443'
          destinationAddressPrefix: 'AzureActiveDirectory'
        }
      }
      {
        name: 'Allow-Outbound-Storage'
        properties: {
          description: 'Storage wymagany przez srodowisko Container Apps.'
          direction: 'Outbound'
          access: 'Allow'
          priority: 160
          protocol: 'Tcp'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationPortRange: '443'
          destinationAddressPrefix: 'Storage'
        }
      }
      {
        // ANTY-PIVOT: skompromitowany sensor nie wyjdzie z DMZ — blokujemy
        // caly pozostaly ruch wychodzacy (rowniez do VNet, czyli do warstw logic/data).
        name: 'Deny-Outbound-All'
        properties: {
          description: 'Anty-pivot: domyslna blokada calego ruchu wychodzacego z DMZ.'
          direction: 'Outbound'
          access: 'Deny'
          priority: 4000
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// NSG: logic — brak ruchu z Internetu, brak ruchu z DMZ
// ---------------------------------------------------------------------------
resource logicNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: logicNsgName
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        // Obrona w glab: nawet gdyby ktos zdjal regule Deny-Outbound-All w DMZ,
        // warstwa logic odrzuca ruch przychodzacy z podsieci sensorow.
        name: 'Deny-Inbound-From-Dmz'
        properties: {
          description: 'Anty-pivot (druga linia): zadnego ruchu z DMZ do warstwy logic.'
          direction: 'Inbound'
          access: 'Deny'
          priority: 100
          protocol: '*'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: logicSubnetPrefix
          destinationPortRange: '*'
        }
      }
      {
        name: 'Deny-Inbound-Internet'
        properties: {
          description: 'Warstwa logic nie przyjmuje ruchu z Internetu (brak publicznych IP).'
          direction: 'Inbound'
          access: 'Deny'
          priority: 4000
          protocol: '*'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// NSG: data — tylko ruch z warstwy logic (Private Endpoints)
// ---------------------------------------------------------------------------
resource dataNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: dataNsgName
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-Inbound-From-Logic'
        properties: {
          description: 'Dostep do Private Endpoints wylacznie z warstwy logic.'
          direction: 'Inbound'
          access: 'Allow'
          priority: 100
          protocol: 'Tcp'
          sourceAddressPrefix: logicSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: dataSubnetPrefix
          destinationPortRange: '*'
        }
      }
      {
        name: 'Deny-Inbound-From-Dmz'
        properties: {
          description: 'Anty-pivot: DMZ nigdy nie gada bezposrednio z warstwa danych.'
          direction: 'Inbound'
          access: 'Deny'
          priority: 200
          protocol: '*'
          sourceAddressPrefix: dmzSubnetPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: dataSubnetPrefix
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// VNet z trzema podsieciami
// ---------------------------------------------------------------------------
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: 'snet-dmz'
        properties: {
          addressPrefix: dmzSubnetPrefix
          networkSecurityGroup: {
            id: dmzNsg.id
          }
          // Delegacja wymagana przez Container Apps Environment (workload profiles)
          // do integracji z VNet — sensory żyją logicznie w DMZ.
          delegations: [
            {
              name: 'delegation-containerapps'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-logic'
        properties: {
          addressPrefix: logicSubnetPrefix
          networkSecurityGroup: {
            id: logicNsg.id
          }
          // Brak publicznych IP w warstwie logic — wymuszamy brak domyslnego
          // dostepu wychodzacego; ruch wyjdzie wylacznie przez Private Link / NAT.
          defaultOutboundAccess: false
        }
      }
      {
        name: 'snet-data'
        properties: {
          addressPrefix: dataSubnetPrefix
          networkSecurityGroup: {
            id: dataNsg.id
          }
          // Wymagane do hostowania Private Endpoints (Private Link).
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Private DNS / Private Link żyje w modules/privatelink.bicep (Tydzień 1,
// Track A): strefy Private DNS, linki VNet i Private Endpoints dla
// Cosmos DB / Blob Storage / Key Vault w podsieci snet-data.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output vnetId string = vnet.id
output vnetName string = vnet.name
output dmzSubnetId string = vnet.properties.subnets[0].id
output logicSubnetId string = vnet.properties.subnets[1].id
output dataSubnetId string = vnet.properties.subnets[2].id
output dmzNsgId string = dmzNsg.id
output dmzNsgName string = dmzNsg.name
output logicNsgId string = logicNsg.id
output dataNsgId string = dataNsg.id
