// ============================================================================
// HoneyGrid — Function App (Track B): host funkcji konwejera
//   ClassifyEvents · FanOutToSignalR · BuildAggregates · CorrelateActors ·
//   DailyBriefing · negotiate (SignalR Serverless).
//
// .NET isolated (v4) na FLEX CONSUMPTION (FC1). Bezkluczowo: tożsamość
// user-assigned + role na Cosmos (dane), Storage (AzureWebJobs + paczka
// wdrożeniowa, keyless), Azure OpenAI i SignalR.
// Kod wgrywa się osobno: `az functionapp deploy --src-path func.zip --type zip`
// (OneDeploy → kontener `app-package` w koncie Storage).
//
// DLACZEGO FLEX, nie Consumption (Y1): plan Linux Consumption NIE uruchamia
// workera .NET 10 (host milczy → 503, zero telemetrii). Flex Consumption ma
// pełne wsparcie .NET 10 isolated i bezkluczowego host storage; Linux
// Consumption jest wycofywany (30.09.2028).
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('Istniejące konto Storage (AzureWebJobsStorage — keyless).')
param storageAccountName string

@description('Connection string App Insights (observability).')
param appInsightsConnectionString string

@description('Konto Cosmos (do roli płaszczyzny danych) + endpoint + baza.')
param cosmosAccountName string
param cosmosEndpoint string
param cosmosDatabaseName string = 'honeygrid'

@description('Azure OpenAI: konto (rola), endpoint i nazwa deploymentu modelu.')
param openAiAccountName string
param openAiEndpoint string
param openAiDeploymentName string

@description('Azure SignalR Service: nazwa (rola) i endpoint (https://...).')
param signalRName string
param signalREndpoint string

@description('''Tożsamość user-assigned Function App (z security.bicep). User-assigned,
bo principalId musi być znany na starcie wdrożenia do nazw przypisań ról (guid()).''')
param functionsIdentityId string
param functionsIdentityPrincipalId string
param functionsIdentityClientId string

@description('''Podsieć integracji VNet (snet-func, delegowana do Microsoft.App/environments).
Daje hostowi dostęp do Cosmos przez Private Endpoint — bez niej change-feed dostaje 403.''')
param functionsSubnetId string

var suffix = uniqueString(resourceGroup().id)
var functionAppName = '${namePrefix}-${environment}-func-${suffix}'
var planName = '${namePrefix}-${environment}-funcplan'

// ── Istniejące zasoby (do przypisań ról bezkluczowych) ─────────────────────
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}
resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: openAiAccountName
}
resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' existing = {
  name: signalRName
}

// ── Kontener na paczkę wdrożeniową (Flex OneDeploy) ────────────────────────
// Flex pobiera kod z kontenera blob (nie WEBSITE_RUN_FROM_PACKAGE). Dostęp do
// niego ma tożsamość funkcji (Storage Blob Data Owner — patrz niżej).
resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storageAccountName}/default/app-package'
}

// ── Plan Flex Consumption (FC1, Linux) ─────────────────────────────────────
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  kind: 'functionapp'
  properties: {
    reserved: true // Linux
  }
}

// ── Function App (.NET 10 isolated na Flex Consumption) ────────────────────
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${functionsIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Integracja VNet: ruch do Cosmos idzie przez Private Endpoint w snet-data
    // (vnetRouteAllEnabled kieruje cały outbound przez VNet → strefa Private DNS
    // rozwiązuje FQDN Cosmos na prywatny IP). SignalR/OpenAI/Storage publiczne
    // nadal wychodzą do Internetu (brak NSG na snet-func).
    virtualNetworkSubnetId: functionsSubnetId
    vnetRouteAllEnabled: true
    // Flex: stos i skalowanie idą w functionAppConfig (NIE linuxFxVersion ani
    // FUNCTIONS_EXTENSION_VERSION/FUNCTIONS_WORKER_RUNTIME w appSettings).
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}app-package'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: functionsIdentityId
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        instanceMemoryMB: 2048
        maximumInstanceCount: 40
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        allowedOrigins: ['*']
      }
      appSettings: [
        // Bezkluczowy host storage (AzureWebJobsStorage) na tożsamości
        // user-assigned: accountName + credential=managedidentity + clientId.
        // Wymaga ról Storage (Blob Owner + Queue + Table) — patrz niżej.
        { name: 'AZURE_CLIENT_ID', value: functionsIdentityClientId }
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: functionsIdentityClientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        // Kontrakt z kodem funkcji (HoneyGrid.Functions):
        { name: 'CosmosDatabase', value: cosmosDatabaseName }
        { name: 'CosmosConnection__accountEndpoint', value: cosmosEndpoint }
        { name: 'CosmosConnection__credential', value: 'managedidentity' }
        { name: 'CosmosConnection__clientId', value: functionsIdentityClientId }
        { name: 'OpenAIEndpoint', value: openAiEndpoint }
        { name: 'OpenAIDeployment', value: openAiDeploymentName }
        { name: 'AzureSignalRConnectionString__serviceUri', value: signalREndpoint }
        { name: 'AzureSignalRConnectionString__credential', value: 'managedidentity' }
        { name: 'AzureSignalRConnectionString__clientId', value: functionsIdentityClientId }
      ]
    }
  }
}

// ── Role bezkluczowe dla tożsamości funkcji ────────────────────────────────
// principalId tożsamości user-assigned (znany na starcie — patrz BCP120).
var funcPrincipalId = functionsIdentityPrincipalId

// Cosmos — rola PŁASZCZYZNY DANYCH (Built-in Data Contributor: 0000…0002).
resource cosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, funcPrincipalId, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: funcPrincipalId
    scope: cosmos.id
  }
}

// Storage — Blob Data Owner + Queue Data Contributor (AzureWebJobsStorage keyless).
resource storageBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, funcPrincipalId, 'blob-data-owner')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}
resource storageQueueContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, funcPrincipalId, 'queue-data-contributor')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}
// Storage Table Data Contributor — host z bezkluczowym AzureWebJobsStorage
// używa też Table (leasy/stan). Bez tej roli host nie wstaje.
resource storageTableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, funcPrincipalId, 'table-data-contributor')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Azure OpenAI — Cognitive Services OpenAI User.
resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, funcPrincipalId, 'openai-user')
  scope: openAi
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// SignalR — SignalR Service Owner (negotiate + broadcast w trybie Serverless).
resource signalROwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(signalR.id, funcPrincipalId, 'signalr-owner')
  scope: signalR
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7e4f1700-ea5a-4f59-8f37-079cfe29dce3')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Wyjścia ────────────────────────────────────────────────────────────────
output functionAppName string = functionApp.name
output functionAppHostname string = functionApp.properties.defaultHostName
output functionAppPrincipalId string = funcPrincipalId
