// ============================================================================
// HoneyGrid — moduł AI (Tydzień 0: szkielet; Tydzień 1: Key Vault → security.bicep)
//
// Zasoby:
//   - Azure OpenAI (S0) + deployment gpt-4o-mini — klasyfikacja sesji
//     atakujących (TTP, intencja, poziom zagrożenia) z kolejki 'ai-classify'
//   - Azure Maps (Gen2 / SKU G2 — darmowy poziom transakcji) — geokodowanie
//     IP atakujących pod mapę na dashboardzie
//   - Communication Services — PLACEHOLDER (powiadomienia e-mail, Tydzień 5)
//
// Key Vault został przeniesiony do modules/security.bicep (Tydzień 1, Track A) —
// należy do płaszczyzny bezpieczeństwa zgodnie z podziałem pracy.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

var suffix = uniqueString(resourceGroup().id)

var openAiAccountName = '${namePrefix}-${environment}-oai-${suffix}'
var mapsAccountName = '${namePrefix}-${environment}-maps'

// ---------------------------------------------------------------------------
// Azure OpenAI
// ---------------------------------------------------------------------------
resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    // Subdomena wymagana do uwierzytelniania Entra ID (bezkluczowo).
    customSubDomainName: openAiAccountName
    // Bezkluczowo: wyłączamy klucze API — dostęp tylko przez Managed Identity
    // + rola "Cognitive Services OpenAI User".
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled' // TODO (Tydzień 3, Track B): Private Endpoint
  }
}

// Deployment gpt-4o-mini — najtańszy model z sensowną jakością klasyfikacji.
// GlobalStandard rozlicza per token (brak stałej opłaty) — pasuje do budżetu.
resource gpt4oMiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAiAccount
  name: 'gpt-4o-mini'
  sku: {
    name: 'GlobalStandard'
    capacity: 8 // 8k TPM — wystarczy na klasyfikację sesji z kolejki
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
    // TODO (Tydzień 3, Track B): dostroić capacity po pomiarze realnego
    // wolumenu sesji; ewentualnie content filter pod treści ataków.
  }
}

// ---------------------------------------------------------------------------
// Azure Maps — Gen2, SKU G2 (darmowe 1000 transakcji geolokalizacji/mies. i więcej)
// ---------------------------------------------------------------------------
resource mapsAccount 'Microsoft.Maps/accounts@2023-06-01' = {
  name: mapsAccountName
  location: location
  tags: tags
  kind: 'Gen2'
  sku: {
    name: 'G2'
  }
  properties: {
    // Bezkluczowo: tylko Entra ID (rola "Azure Maps Data Reader" dla dashboardu).
    disableLocalAuth: true
  }
}

// ---------------------------------------------------------------------------
// PLACEHOLDER: Azure Communication Services — powiadomienia e-mail o incydentach
// TODO (Tydzień 5, Track B): odkomentować + domena e-mail (Email Communication
// Services) + integracja z playbookiem Logic Apps.
// ---------------------------------------------------------------------------
// resource communicationServices 'Microsoft.Communication/communicationServices@2023-04-01' = {
//   name: '${namePrefix}-${environment}-acs'
//   location: 'global'
//   tags: tags
//   properties: {
//     dataLocation: 'Europe'
//   }
// }

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output openAiAccountId string = openAiAccount.id
output openAiAccountName string = openAiAccount.name
output openAiEndpoint string = openAiAccount.properties.endpoint
output gptDeploymentName string = gpt4oMiniDeployment.name

output mapsAccountId string = mapsAccount.id
output mapsAccountName string = mapsAccount.name
