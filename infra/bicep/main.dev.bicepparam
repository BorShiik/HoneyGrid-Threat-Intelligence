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
