#!/usr/bin/env bash
#
# verify-week3.sh — weryfikacja workera Ingestion/Enrichment (Tydzień 3, Track A).
#
# Sprawdza, czy worker Ingestion (Container App) istnieje i działa, czy
# istnieje środowisko Container Apps, tożsamość id-worker i jej role
# (ARM: EH Receiver / Blob Contributor / SB Sender + płaszczyzna danych
# Cosmos), kontener blob 'checkpoints' oraz przepływ zdarzeń (metryki
# Event Hubs, kolejka Service Bus).
#
# AKTUALIZACJA (Tydzień 4): drugie środowisko `cae-logic` zostało USUNIĘTE
# (limit subskrypcji: MaxNumberOfRegionalEnvironmentsInSubExceeded) — worker
# działa we WSPÓLNYM środowisku sensorów (hg-dev-cae). Skrypt sprawdza
# teraz to jedno środowisko.
#
# Użycie:
#   ./infra/scripts/verify-week3.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week3.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... list/show/metrics).
# Każda kontrola wypisuje ✅ (OK) lub ❌ (problem) i idzie dalej, żeby pokazać
# pełny obraz. "Resource not found" => czytelne ❌, a nie wywrotka skryptu.

set -euo pipefail

RG="${1:-hg-dev-rg}"

# Nazwy zasobów (spójne z konwencją namePrefix=hg, environment=dev w bicep).
PREFIX="hg-dev"
CA_INGESTION="${PREFIX}-ca-ingestion"
# Tydzień 4: jedno wspólne środowisko (cae-logic usunięte — limit subskrypcji).
CAE="${PREFIX}-cae"
ID_WORKER="${PREFIX}-id-worker"
SB_QUEUE="ai-classify"

# Dobrze znane GUID-y ról (muszą zgadzać się z modules/rbac.bicep).
ROLE_EH_RECEIVER="a638d3c7-ab3a-418d-83e6-5f17a39d4fde"   # Azure Event Hubs Data Receiver
ROLE_BLOB_CONTRIB="ba92f5b4-2d11-453d-a403-e96b0029c9fe"  # Storage Blob Data Contributor
ROLE_SB_SENDER="69a216fc-b8fb-44d8-bc22-1f3c2cd27a39"     # Azure Service Bus Data Sender
ROLE_ACR_PULL="7f951dda-4ed3-4680-a7ca-43fe172d538d"      # AcrPull
COSMOS_DATA_CONTRIB="00000000-0000-0000-0000-000000000002" # Cosmos DB Built-in Data Contributor (płaszczyzna danych)

FAILURES=0

# Pomocnicze funkcje wypisujące wynik kontroli.
ok()      { echo "  ✅ $1"; }
fail()    { echo "  ❌ $1"; FAILURES=$((FAILURES + 1)); }
section() { echo; echo "=== $1 ==="; }

# --- Wymagania wstępne: az CLI + zalogowanie ---------------------------------
if ! command -v az >/dev/null 2>&1; then
  echo "❌ Brak Azure CLI (az). Zainstaluj: brew install azure-cli" >&2
  exit 1
fi
if ! az account show >/dev/null 2>&1; then
  echo "❌ Nie jesteś zalogowany/-a. Uruchom: az login" >&2
  exit 1
fi
if ! az extension show -n containerapp >/dev/null 2>&1; then
  echo "ℹ️  Brak rozszerzenia 'containerapp' — w razie błędów: az extension add -n containerapp" >&2
fi

echo "HoneyGrid — weryfikacja Tygodnia 3 (worker Ingestion/Enrichment)"
echo "Grupa zasobów: $RG"
echo "Subskrypcja:   $(az account show --query name -o tsv)"

# --- 0. Czy grupa zasobów istnieje -------------------------------------------
section "Grupa zasobów"
if [[ "$(az group exists -n "$RG")" == "true" ]]; then
  ok "Grupa zasobów '$RG' istnieje"
else
  fail "Grupa zasobów '$RG' NIE istnieje — najpierw wdróż infrastrukturę"
  echo
  echo "Wynik: bez grupy zasobów dalsze kontrole nie mają sensu. Koniec."
  exit 1
fi

# --- 1. Tożsamość workera ------------------------------------------------------
section "Tożsamość workera ($ID_WORKER)"
WORKER_PRINCIPAL="$(az identity show -g "$RG" -n "$ID_WORKER" \
  --query principalId -o tsv 2>/dev/null || true)"
if [[ -n "$WORKER_PRINCIPAL" ]]; then
  ok "Tożsamość workera '$ID_WORKER' istnieje (principalId: $WORKER_PRINCIPAL)"
else
  fail "Brak tożsamości workera '$ID_WORKER' — worker nie pociągnie obrazu ani nie dostanie się do danych"
fi

# --- 2. Role ARM workera (EH Receiver / Blob Contributor / SB Sender / AcrPull)
section "Role ARM workera (az role assignment list)"
if [[ -n "$WORKER_PRINCIPAL" ]]; then
  # Jedno wywołanie: pobieramy wszystkie GUID-y definicji ról przypisanych workerowi.
  WORKER_ROLES="$(az role assignment list --assignee "$WORKER_PRINCIPAL" --all \
    --query '[].roleDefinitionId' -o tsv 2>/dev/null || true)"

  check_role() {
    local GUID="$1" LABEL="$2"
    if echo "$WORKER_ROLES" | grep -qi "$GUID"; then
      ok "$LABEL ($GUID)"
    else
      fail "Brak roli: $LABEL ($GUID) — sprawdź modules/rbac.bicep / app.bicep"
    fi
  }
  check_role "$ROLE_EH_RECEIVER"  "Azure Event Hubs Data Receiver (odczyt telemetrii)"
  check_role "$ROLE_BLOB_CONTRIB" "Storage Blob Data Contributor (checkpointy + raw)"
  check_role "$ROLE_SB_SENDER"    "Azure Service Bus Data Sender (kolejka ai-classify)"
  check_role "$ROLE_ACR_PULL"     "AcrPull (pull obrazu honeygrid-ingestion)"
else
  fail "Pomijam kontrolę ról — brak principalId workera"
fi

# --- 3. Rola płaszczyzny danych Cosmos (sqlRoleAssignments, NIE ARM!) ---------
section "Cosmos DB — rola płaszczyzny danych (sqlRoleAssignments)"
# Konto Cosmos ma losowy sufiks (uniqueString) — szukamy po prefiksie.
COSMOS_ACC="$(az cosmosdb list -g "$RG" \
  --query "[?starts_with(name, '${PREFIX}-cosmos')].name | [0]" -o tsv 2>/dev/null || true)"
if [[ -z "$COSMOS_ACC" ]]; then
  fail "Nie znaleziono konta Cosmos (prefiks '${PREFIX}-cosmos') w grupie '$RG'"
elif [[ -z "$WORKER_PRINCIPAL" ]]; then
  fail "Pomijam kontrolę Cosmos — brak principalId workera"
else
  ok "Konto Cosmos: $COSMOS_ACC"
  # UWAGA: to NIE jest rola ARM — disableLocalAuth=true wymusza wyłącznie AAD,
  # więc bez tego przypisania SDK workera dostaje 403 mimo ról ARM.
  COSMOS_ASSIGN="$(az cosmosdb sql role assignment list -g "$RG" -a "$COSMOS_ACC" \
    --query "[?principalId=='$WORKER_PRINCIPAL'].roleDefinitionId" -o tsv 2>/dev/null || true)"
  if echo "$COSMOS_ASSIGN" | grep -qi "$COSMOS_DATA_CONTRIB"; then
    ok "Worker ma rolę Cosmos DB Built-in Data Contributor ($COSMOS_DATA_CONTRIB)"
  else
    fail "Brak roli płaszczyzny danych Cosmos dla workera — SDK dostanie 403 (sprawdź data.bicep)"
  fi
fi

# --- 4. Środowisko Container Apps (wspólne — konsolidacja Tygodnia 4) ---------
section "Środowisko Container Apps ($CAE — wspólne dla sensorów i workera)"
CAE_STATE="$(az containerapp env show -g "$RG" -n "$CAE" \
  --query 'properties.provisioningState' -o tsv 2>/dev/null || true)"
if [[ "$CAE_STATE" == "Succeeded" ]]; then
  ok "Środowisko '$CAE' istnieje (provisioningState = Succeeded)"
elif [[ -n "$CAE_STATE" ]]; then
  fail "Środowisko '$CAE' w stanie '$CAE_STATE' (oczekiwano Succeeded)"
else
  fail "Środowisko '$CAE' nie istnieje (resource not found)"
fi

# --- 5. Container App workera --------------------------------------------------
section "Worker Ingestion ($CA_INGESTION)"
APP_JSON="$(az containerapp show -g "$RG" -n "$CA_INGESTION" -o json 2>/dev/null || true)"
if [[ -z "$APP_JSON" ]]; then
  fail "Container App '$CA_INGESTION' nie istnieje (resource not found)"
else
  ok "Container App '$CA_INGESTION' istnieje"

  PROV="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
          --query 'properties.provisioningState' -o tsv 2>/dev/null || true)"
  RUN="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
          --query 'properties.runningStatus' -o tsv 2>/dev/null || true)"
  if [[ "$PROV" == "Succeeded" ]]; then
    ok "provisioningState = Succeeded"
  else
    fail "provisioningState = ${PROV:-<brak>} (oczekiwano Succeeded)"
  fi
  if [[ "$RUN" == "Running" ]]; then
    ok "runningStatus = Running"
  else
    fail "runningStatus = ${RUN:-<brak>} (oczekiwano Running — ciągły konsument strumienia)"
  fi

  # Worker to proces tła — NIE powinien mieć ingressu.
  INGRESS="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
          --query 'properties.configuration.ingress' -o tsv 2>/dev/null || true)"
  if [[ -z "$INGRESS" || "$INGRESS" == "None" ]]; then
    ok "Brak ingressu (worker tła — poprawnie)"
  else
    fail "Worker MA ingress — proces tła nie powinien przyjmować połączeń"
  fi

  # Tożsamość workera przypięta do aplikacji.
  APP_IDS="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
             --query 'identity.userAssignedIdentities' -o tsv 2>/dev/null || true)"
  if echo "$APP_IDS" | grep -qi "$ID_WORKER"; then
    ok "Przypisana tożsamość workera '$ID_WORKER' (User-Assigned)"
  else
    fail "Aplikacja NIE ma przypiętej tożsamości '$ID_WORKER' — pull z ACR i dostęp do danych zawiodą"
  fi
fi

# --- 6. Kontener blob 'checkpoints' --------------------------------------------
section "Blob — kontener 'checkpoints' (offsety EventProcessorClient)"
ST_ACC="$(az storage account list -g "$RG" \
  --query "[?starts_with(name, 'hgdevst')].name | [0]" -o tsv 2>/dev/null || true)"
if [[ -z "$ST_ACC" ]]; then
  fail "Nie znaleziono konta Storage (prefiks 'hgdevst') w grupie '$RG'"
else
  ok "Konto Storage: $ST_ACC"
  CHK_EXISTS="$(az storage container exists --account-name "$ST_ACC" \
    --name checkpoints --auth-mode login --query exists -o tsv 2>/dev/null || true)"
  if [[ "$CHK_EXISTS" == "true" ]]; then
    ok "Kontener blob 'checkpoints' istnieje"
  else
    fail "Brak kontenera 'checkpoints' (lub brak uprawnień do odczytu — wymagana rola danych blob)"
  fi
fi

# --- 7. Event Hubs: wlot vs odbiór (Incoming/OutgoingMessages) -----------------
section "Event Hubs — przepływ telemetrii (Incoming/OutgoingMessages)"
EH_NS="$(az eventhubs namespace list -g "$RG" \
  --query "[?starts_with(name, '${PREFIX}-ehns')].name | [0]" -o tsv 2>/dev/null || true)"
if [[ -z "$EH_NS" ]]; then
  fail "Nie znaleziono namespace Event Hubs (prefiks '${PREFIX}-ehns') w grupie '$RG'"
else
  ok "Namespace Event Hubs: $EH_NS"
  EH_ID="$(az eventhubs namespace show -g "$RG" -n "$EH_NS" --query id -o tsv 2>/dev/null || true)"
  # OutgoingMessages > 0 oznacza, że KTOŚ (worker) faktycznie ODBIERA zdarzenia.
  for METRIC in IncomingMessages OutgoingMessages; do
    VAL="$(az monitor metrics list --resource "$EH_ID" \
        --metric "$METRIC" --aggregation Total --interval PT1H \
        --query 'value[0].timeseries[0].data[-1].total' -o tsv 2>/dev/null || true)"
    if [[ -n "$VAL" && "$VAL" != "None" ]] && awk "BEGIN{exit !($VAL > 0)}"; then
      ok "$METRIC (ost. godz.) = $VAL"
    else
      echo "  ℹ️  $METRIC (ost. godz.) = ${VAL:-brak danych} — zero ruchu lub opóźnienie metryk."
    fi
  done
  echo
  echo "  Ręczna kontrola przepływu (kopiuj-wklej):"
  echo "    az monitor metrics list --resource \"$EH_ID\" \\"
  echo "      --metric IncomingMessages,OutgoingMessages --aggregation Total --interval PT5M -o table"
fi

# --- 8. Service Bus: kolejka ai-classify ---------------------------------------
section "Service Bus — kolejka '$SB_QUEUE' (zlecenia klasyfikacji)"
SB_NS="$(az servicebus namespace list -g "$RG" \
  --query "[?starts_with(name, '${PREFIX}-sbns')].name | [0]" -o tsv 2>/dev/null || true)"
if [[ -z "$SB_NS" ]]; then
  fail "Nie znaleziono namespace Service Bus (prefiks '${PREFIX}-sbns') w grupie '$RG'"
else
  ok "Namespace Service Bus: $SB_NS"
  ACTIVE_MSG="$(az servicebus queue show -g "$RG" --namespace-name "$SB_NS" -n "$SB_QUEUE" \
    --query 'countDetails.activeMessageCount' -o tsv 2>/dev/null || true)"
  if [[ -n "$ACTIVE_MSG" ]]; then
    ok "Kolejka '$SB_QUEUE' istnieje (activeMessageCount = $ACTIVE_MSG)"
    if [[ "$ACTIVE_MSG" -gt 0 ]]; then
      echo "  ℹ️  W kolejce czekają zlecenia — worker realnie publikuje (konsument AI to Track B)."
    else
      echo "  ℹ️  Kolejka pusta — brak nowych zdarzeń do klasyfikacji lub brak ruchu na sensorach."
    fi
  else
    fail "Kolejka '$SB_QUEUE' nie istnieje lub brak dostępu (sprawdź data.bicep)"
  fi
fi

# --- 9. Podgląd logów workera (wskazówka) --------------------------------------
section "Logi workera — szybka diagnostyka"
echo "  ℹ️  Ogon logów konsoli workera (kopiuj-wklej):"
echo "       az containerapp logs show -g $RG -n $CA_INGESTION --tail 50 --follow false"
echo "     Szukaj: poprawnego startu EventProcessorClient, zapisów do Cosmos/Blob"
echo "     i wysyłek do Service Bus. Błędy 403 => sprawdź role (sekcje 2-3 wyżej)."

# --- Podsumowanie ---------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Worker Ingestion Tygodnia 3 wygląda dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
