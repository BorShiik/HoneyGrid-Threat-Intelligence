#!/usr/bin/env bash
#
# verify-week4.sh — weryfikacja ścieżki Sentinela (Tydzień 4, Track A).
#
# Sprawdza pełną drogę zdarzenia do SIEM-a:
#   DCR 'Direct' (hg-dev-dcr-cowrie) + immutableId,
#   DCE (hg-dev-dce) + endpoint Logs Ingestion API,
#   tabela niestandardowa Cowrie_CL w Log Analytics,
#   rola Monitoring Metrics Publisher workera ZAWĘŻONA do konkretnego DCR,
#   worker Ingestion działający we WSPÓLNYM środowisku (konsolidacja:
#   limit MaxNumberOfRegionalEnvironmentsInSubExceeded — jedno środowisko),
#   i na koniec realne dane: zapytanie KQL do tabeli Cowrie_CL.
#
# Użycie:
#   ./infra/scripts/verify-week4.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week4.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... show/list/query).
# Każda kontrola wypisuje ✅ (OK) lub ❌ (problem) i idzie dalej, żeby pokazać
# pełny obraz. "Resource not found" => czytelne ❌, a nie wywrotka skryptu.

set -euo pipefail

RG="${1:-hg-dev-rg}"

# Nazwy zasobów (spójne z konwencją namePrefix=hg, environment=dev w bicep).
PREFIX="hg-dev"
DCR_NAME="${PREFIX}-dcr-cowrie"
DCE_NAME="${PREFIX}-dce"
LAW_NAME="${PREFIX}-law"
TABLE_NAME="Cowrie_CL"
STREAM_NAME="Custom-CowrieStream"
CA_INGESTION="${PREFIX}-ca-ingestion"
CAE="${PREFIX}-cae"
ID_WORKER="${PREFIX}-id-worker"

# Dobrze znany GUID roli (musi zgadzać się z modules/sentinel.bicep).
ROLE_METRICS_PUBLISHER="3913510d-42f4-4e42-8a64-420c390055eb" # Monitoring Metrics Publisher

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

echo "HoneyGrid — weryfikacja Tygodnia 4 (Sentinel: Cowrie_CL + DCR + Logs Ingestion API)"
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

# --- 1. DCR — Data Collection Rule (kind Direct) ------------------------------
# UWAGA: generyczne `az resource show` zamiast `az monitor data-collection ...`
# (to drugie wymaga rozszerzenia monitor-control-service, którego pip czasem
# nie potrafi zainstalować — zależność zewnętrzna, nie nasz kod).
section "Data Collection Rule ($DCR_NAME)"
DCR_JSON="$(az resource show -g "$RG" -n "$DCR_NAME" \
  --resource-type Microsoft.Insights/dataCollectionRules -o json 2>/dev/null || true)"
if [[ -z "$DCR_JSON" ]]; then
  fail "DCR '$DCR_NAME' nie istnieje (resource not found) — sprawdź modules/sentinel.bicep"
else
  ok "DCR '$DCR_NAME' istnieje"
  DCR_IMMUTABLE="$(echo "$DCR_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin)["properties"].get("immutableId",""))' 2>/dev/null || true)"
  if [[ -n "$DCR_IMMUTABLE" ]]; then
    ok "immutableId = $DCR_IMMUTABLE (worker potrzebuje go w Ingestion__DcrImmutableId)"
  else
    fail "DCR bez immutableId — bez niego Logs Ingestion API nie zadziała"
  fi
  if echo "$DCR_JSON" | grep -q "$STREAM_NAME"; then
    ok "Strumień wejściowy '$STREAM_NAME' zadeklarowany"
  else
    fail "Brak strumienia '$STREAM_NAME' w DCR — kontrakt z workerem .NET złamany"
  fi
fi

# --- 2. DCE — endpoint Logs Ingestion API -------------------------------------
# UWAGA: nie używamy `az monitor data-collection ...` — wymaga rozszerzenia
# monitor-control-service, którego instalacja potrafi paść (pip). Generyczne
# `az resource show` działa bez rozszerzeń.
section "Data Collection Endpoint ($DCE_NAME)"
DCE_ENDPOINT="$(az resource show -g "$RG" -n "$DCE_NAME" \
  --resource-type Microsoft.Insights/dataCollectionEndpoints \
  --query 'properties.logsIngestion.endpoint' -o tsv 2>/dev/null || true)"
if [[ -n "$DCE_ENDPOINT" ]]; then
  ok "DCE '$DCE_NAME' istnieje; endpoint ingestii: $DCE_ENDPOINT"
  echo "  ℹ️  Worker potrzebuje go w Ingestion__DceLogsIngestionEndpoint."
else
  fail "DCE '$DCE_NAME' nie istnieje lub bez endpointu logsIngestion"
fi

# --- 3. Tabela niestandardowa Cowrie_CL ---------------------------------------
section "Tabela Log Analytics ($TABLE_NAME)"
TABLE_PLAN="$(az monitor log-analytics workspace table show -g "$RG" \
  --workspace-name "$LAW_NAME" -n "$TABLE_NAME" \
  --query 'plan' -o tsv 2>/dev/null || true)"
if [[ -n "$TABLE_PLAN" ]]; then
  ok "Tabela '$TABLE_NAME' istnieje (plan = $TABLE_PLAN)"
  COL_COUNT="$(az monitor log-analytics workspace table show -g "$RG" \
    --workspace-name "$LAW_NAME" -n "$TABLE_NAME" \
    --query 'length(schema.columns)' -o tsv 2>/dev/null || true)"
  # 20 kolumn schematu, w tym TimeGenerated (zaślepki AI: Category/KillChainPhase).
  if [[ -n "$COL_COUNT" ]]; then
    ok "Liczba kolumn schematu: $COL_COUNT"
  else
    echo "  ℹ️  Nie udało się policzyć kolumn (różnice wersji az) — sprawdź ręcznie."
  fi
else
  fail "Tabela '$TABLE_NAME' nie istnieje w workspace '$LAW_NAME' — sprawdź modules/sentinel.bicep"
fi

# --- 4. RBAC: Monitoring Metrics Publisher workera NA KONKRETNYM DCR ----------
section "RBAC — Monitoring Metrics Publisher workera (zakres: DCR, nie RG)"
WORKER_PRINCIPAL="$(az identity show -g "$RG" -n "$ID_WORKER" \
  --query principalId -o tsv 2>/dev/null || true)"
if [[ -z "$WORKER_PRINCIPAL" ]]; then
  fail "Brak tożsamości workera '$ID_WORKER' — pomijam kontrolę roli"
elif [[ -z "$DCR_JSON" ]]; then
  fail "Brak DCR — pomijam kontrolę roli (zakres nie istnieje)"
else
  DCR_ID="$(echo "$DCR_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null || true)"
  MMP_SCOPES="$(az role assignment list --assignee "$WORKER_PRINCIPAL" --all \
    --query "[?contains(roleDefinitionId, '$ROLE_METRICS_PUBLISHER')].scope" -o tsv 2>/dev/null || true)"
  if echo "$MMP_SCOPES" | grep -qi "dataCollectionRules/${DCR_NAME}"; then
    ok "Worker ma Monitoring Metrics Publisher na DCR '$DCR_NAME' (zakres zawężony — TODO z T0 domknięte)"
  else
    fail "Brak roli Monitoring Metrics Publisher na DCR '$DCR_NAME' — Logs Ingestion API zwróci 403"
    echo "  ℹ️  Oczekiwany zakres: $DCR_ID"
  fi
  # Uczciwa kontrola: rola NIE powinna już wisieć szeroko na całej RG.
  if echo "$MMP_SCOPES" | grep -qiE "resourceGroups/${RG}\$"; then
    fail "Rola Monitoring Metrics Publisher nadal na CAŁEJ RG — stare przypisanie do sprzątnięcia"
  else
    ok "Brak szerokiego przypisania na RG (least privilege zachowane)"
  fi
fi

# --- 5. Worker Ingestion we WSPÓLNYM (jedynym) środowisku ---------------------
section "Worker Ingestion ($CA_INGESTION) w środowisku '$CAE'"
ENV_COUNT="$(az containerapp env list -g "$RG" --query 'length(@)' -o tsv 2>/dev/null || true)"
if [[ "$ENV_COUNT" == "1" ]]; then
  ok "Dokładnie JEDNO środowisko Container Apps w RG (konsolidacja Tygodnia 4)"
elif [[ -n "$ENV_COUNT" ]]; then
  fail "Liczba środowisk Container Apps = $ENV_COUNT (oczekiwano 1 — limit subskrypcji!)"
else
  fail "Nie udało się policzyć środowisk Container Apps"
fi

APP_ENV_ID="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
  --query 'properties.managedEnvironmentId' -o tsv 2>/dev/null || true)"
if [[ -z "$APP_ENV_ID" ]]; then
  fail "Container App '$CA_INGESTION' nie istnieje (resource not found)"
else
  if echo "$APP_ENV_ID" | grep -qi "managedEnvironments/${CAE}\$"; then
    ok "Worker działa w środowisku '$CAE' (wspólnym z sensorami)"
  else
    fail "Worker w innym środowisku niż '$CAE': $APP_ENV_ID"
  fi
  RUN="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
    --query 'properties.runningStatus' -o tsv 2>/dev/null || true)"
  if [[ "$RUN" == "Running" ]]; then
    ok "runningStatus = Running"
  else
    fail "runningStatus = ${RUN:-<brak>} (oczekiwano Running — ciągły konsument strumienia)"
  fi
  # Zmienne środowiskowe kontraktu Logs Ingestion API (sekcja .NET "Ingestion").
  APP_ENVVARS="$(az containerapp show -g "$RG" -n "$CA_INGESTION" \
    --query 'properties.template.containers[0].env[].name' -o tsv 2>/dev/null || true)"
  for VAR in Ingestion__DceLogsIngestionEndpoint Ingestion__DcrImmutableId Ingestion__DcrStreamName; do
    if echo "$APP_ENVVARS" | grep -q "^${VAR}$"; then
      ok "Zmienna środowiskowa '$VAR' ustawiona"
    else
      fail "Brak zmiennej '$VAR' — sink Sentinela w workerze zrobi cichy no-op"
    fi
  done
fi

# --- 6. MONEY SHOT: realne dane w Cowrie_CL (zapytanie KQL) --------------------
section "Dane w tabeli $TABLE_NAME (zapytanie KQL)"
LAW_CUSTOMER_ID="$(az monitor log-analytics workspace show -g "$RG" -n "$LAW_NAME" \
  --query 'customerId' -o tsv 2>/dev/null || true)"
if [[ -z "$LAW_CUSTOMER_ID" ]]; then
  fail "Nie znaleziono workspace '$LAW_NAME' (customerId) — pomijam zapytanie KQL"
else
  ok "Workspace '$LAW_NAME' (customerId: $LAW_CUSTOMER_ID)"
  # UWAGA: `az monitor log-analytics query` wymaga rozszerzenia log-analytics
  # (instalacja przez pip potrafi paść) — odpytujemy REST API bezpośrednio.
  KQL_RESULT="$(az rest --method post \
    --url "https://api.loganalytics.io/v1/workspaces/${LAW_CUSTOMER_ID}/query" \
    --resource "https://api.loganalytics.io" \
    --body "{\"query\":\"${TABLE_NAME} | summarize cnt=count(), newest=max(TimeGenerated)\"}" \
    -o json 2>/dev/null || true)"
  EVENT_COUNT="$(echo "$KQL_RESULT" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["tables"][0]["rows"][0][0])' 2>/dev/null || echo "?")"
  if [[ "$EVENT_COUNT" != "0" && "$EVENT_COUNT" != "?" && -n "$EVENT_COUNT" ]]; then
    ok "Tabela $TABLE_NAME zawiera dane: count = $EVENT_COUNT 🎉"
  elif [[ "$EVENT_COUNT" == "0" ]]; then
    echo "  ℹ️  Zapytanie działa, ale tabela pusta. Dane pojawiają się 2-15 MINUT po zdarzeniu"
    echo "      (pierwsza ingestia do świeżej tabeli bywa wolniejsza) — wygeneruj ruch na"
    echo "      sensorach (np. curl http://<fqdn-web>/wp-login.php) i spróbuj ponownie."
  else
    fail "Zapytanie KQL nie powiodło się — sprawdź uprawnienia (Log Analytics Reader)"
  fi
  echo
  echo "  Ręczna kontrola (kopiuj-wklej):"
  echo "    az rest --method post --url \"https://api.loganalytics.io/v1/workspaces/${LAW_CUSTOMER_ID}/query\" \\"
  echo "      --resource \"https://api.loganalytics.io\" \\"
  echo "      --body '{\"query\":\"${TABLE_NAME} | take 20\"}'"
fi

# --- Podsumowanie ---------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Ścieżka Sentinela Tygodnia 4 wygląda dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
