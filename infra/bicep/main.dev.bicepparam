// Parametry środowiska DEV — praca bieżąca, minimalne koszty.
using 'main.bicep'

param environment = 'dev'
// UWAGA: polityka subskrypcji (Azure for Students) ogranicza regiony.
// Ustaw region DOZWOLONY przez Twoją subskrypcję (sprawdź sondą `az group create`).
// swedencentral to pełny region blisko Polski; jeśli niedozwolony — zmień tę linię.
param location = 'swedencentral'
param namePrefix = 'hg'

// Track B (OpenAI, Maps, Static Web App) wyłączony — te usługi nie są dostępne
// w regionach narzucanych studenckim subskrypcjom. Track A wdraża się w całości.
// Track B włączy = true w swoim wspieranym regionie (np. dla OpenAI: swedencentral).
param deployTrackB = false

// Tydzień 6 (SOAR): objectId SP "Azure Security Insights" w tym tenancie —
// pobrany przez `az ad sp list --filter "appId eq '98785600-1bb7-4fb9-b9fa-19afe2c8a360'"`.
// Umożliwia automation rule uruchamianie playbooka (auto-blokada IP). Pusty => playbook
// tylko ręcznie z incydentu.
param sentinelAutomationPrincipalId = '867828f7-8077-484d-b183-fabde0418341'
