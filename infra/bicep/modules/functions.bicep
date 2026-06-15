// ============================================================================
// HoneyGrid — Function App (Track B): host funkcji konwejera
//   ClassifyEvents · FanOutToSignalR · BuildAggregates · CorrelateActors ·
//   DailyBriefing · negotiate (SignalR Serverless).
//
// .NET isolated (v4) na Linux Consumption. Bezkluczowo: tożsamość systemowa +
// role na Cosmos (dane), Storage (AzureWebJobs, keyless), Azure OpenAI i SignalR.
// Kod wgrywa się osobno: `func azure functionapp publish <name>` (zip deploy).
//
// UWAGA (.NET 10): runtime '10.0' jest świeży — jeśli region/stos Functions go
// nie wspiera, zmień linuxFxVersion na 'DOTNET-ISOLATED|9.0' i target net9.0.
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

var suffix = uniqueString(resourceGroup().id)
var functionAppName = '${namePrefix}-${environment}-func-${suffix}'
var planName = '${namePrefix}-${environment}-funcplan'
var storageSuffix = az.environment().suffixes.storage

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

// ── Plan Consumption (Linux) ───────────────────────────────────────────────
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {
    reserved: true // Linux
  }
}

// ── Function App (.NET isolated v4) ────────────────────────────────────────
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        allowedOrigins: ['*']
      }
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        // AzureWebJobsStorage bezkluczowo (identity-based) — wymaga ról Storage niżej.
        { name: 'AzureWebJobsStorage__blobServiceUri', value: 'https://${storageAccountName}.blob.${storageSuffix}' }
        { name: 'AzureWebJobsStorage__queueServiceUri', value: 'https://${storageAccountName}.queue.${storageSuffix}' }
        { name: 'AzureWebJobsStorage__tableServiceUri', value: 'https://${storageAccountName}.table.${storageSuffix}' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        // Kontrakt z kodem funkcji (HoneyGrid.Functions):
        { name: 'CosmosDatabase', value: cosmosDatabaseName }
        { name: 'CosmosConnection__accountEndpoint', value: cosmosEndpoint }
        { name: 'OpenAIEndpoint', value: openAiEndpoint }
        { name: 'OpenAIDeployment', value: openAiDeploymentName }
        { name: 'AzureSignalRConnectionString__serviceUri', value: signalREndpoint }
      ]
    }
  }
}

// ── Role bezkluczowe dla tożsamości funkcji ────────────────────────────────
var funcPrincipalId = functionApp.identity.principalId

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
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7e4f1700-ea5a-4f59-8f37-079cfe29dca6')
    principalId: funcPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Wyjścia ────────────────────────────────────────────────────────────────
output functionAppName string = functionApp.name
output functionAppHostname string = functionApp.properties.defaultHostName
output functionAppPrincipalId string = funcPrincipalId
