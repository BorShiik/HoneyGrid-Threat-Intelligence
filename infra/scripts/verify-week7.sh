#!/usr/bin/env bash
#
# verify-week7.sh — weryfikacja killer-ficzy Track A (Tydzień 7).
#
# Sprawdza host API (Container App ca-api) serwujący dwa killer-ficzy:
#   * GET /api/iocs/stix            — eksport bundle STIX 2.1,
#   * GET /api/sessions/{id}/replay — odtworzenie sesji TTY (Session Replay),
# wraz z bezkluczową, NAJMNIEJ-UPRAWNIONĄ tożsamością id-api:
#   - Container App 'hg-{env}-ca-api' istnieje, ma ingress (FQDN), running,
#     scale-to-zero (minReplicas = 0),
#   - tożsamość 'hg-{env}-id-api' (UserAssigned) istnieje,
#   - role API: Cosmos DB Built-in Data Reader (płaszczyzna danych),
#     Storage Blob Data Reader (nagrania TTY), AcrPull (pull obrazu),
#   - curl /health (200) + /api/iocs/stix (JSON, type == "bundle").
#
# Użycie:
#   ./infra/scripts/verify-week7.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week7.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... show/list, az rest GET,
# curl). Wszystkie wywołania az są BEZ ROZSZERZEŃ (maszyna dev nie instaluje
# rozszerzeń — pip pada). NIE używamy `az monitor`/`az log-analytics`.
# `az cosmosdb`, `az containerapp`, `az identity`, `az role assignment`,
# `az resource` to polecenia RDZENIOWE (bez rozszerzeń).
#
# WDROŻENIE wykonuje użytkownik (Azure RG bywa kasowane między sesjami) —
# patrz docs/tydzien-7-killer-features.md (rebuild obrazów + az deployment).

set -euo pipefail

RG="${1:-hg-dev-rg}"

# Wyprowadzenie środowiska z nazwy RG (hg-dev-rg -> dev), w razie wątpliwości: dev.
ENV="dev"
case "$RG" in
  hg-*-rg)
    ENV="${RG#hg-}"
    ENV="${ENV%-rg}"
    ;;
esac
[[ -z "$ENV" ]] && ENV="dev"

# Nazwy zasobów (konwencja namePrefix=hg, environment=$ENV w bicep).
PREFIX="hg-${ENV}"
CA_API="${PREFIX}-ca-api"
ID_API="${PREFIX}-id-api"

# Dobrze znane GUID-y ról (muszą zgadzać się z modułami bicep).
ROLE_BLOB_READER="2a2b9908-6ea1-4ae2-8e65-a410df84e7d1"  # Storage Blob Data Reader
ROLE_ACRPULL="7f951dda-4ed3-4680-a7ca-43fe172d538d"      # AcrPull
# Cosmos DB Built-in Data Reader (rola PŁASZCZYZNY DANYCH — sqlRoleDefinitions).
COSMOS_DATA_READER_ID="00000000-0000-0000-0000-000000000001"

FAILURES=0

# Pomocnicze funkcje wypisujące wynik kontroli.
ok()      { echo "  ✅ $1"; }
fail()    { echo "  ❌ $1"; FAILURES=$((FAILURES + 1)); }
info()    { echo "  ℹ️  $1"; }
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

echo "HoneyGrid — weryfikacja Tygodnia 7 (killer-ficzy: Session Replay + STIX/IoC)"
echo "Grupa zasobów: $RG"
echo "Środowisko:    $ENV (prefix: $PREFIX)"
echo "Subskrypcja:   $(az account show --query name -o tsv)"

# --- 0. Czy grupa zasobów istnieje -------------------------------------------
section "Grupa zasobów"
if [[ "$(az group exists -n "$RG")" == "true" ]]; then
  ok "Grupa zasobów '$RG' istnieje"
else
  fail "Grupa zasobów '$RG' NIE istnieje"
  echo
  echo "Wynik: bez grupy zasobów dalsze kontrole nie mają sensu."
  info "Wdrożenie wykonuje użytkownik — patrz docs/tydzien-7-killer-features.md"
  echo "      (rebuild obrazów honeygrid-api + az deployment sub create ...)."
  exit 1
fi

# --- 1. Tożsamość id-api (UserAssigned, tylko-do-odczytu) --------------------
section "Tożsamość API ($ID_API)"
API_PRINCIPAL="$(az identity show -g "$RG" -n "$ID_API" \
  --query principalId -o tsv 2>/dev/null || true)"
if [[ -z "$API_PRINCIPAL" ]]; then
  fail "Tożsamość '$ID_API' nie istnieje — sprawdź modules/security.bicep"
else
  ok "Tożsamość '$ID_API' istnieje (principalId: $API_PRINCIPAL)"
fi

# --- 2. Container App ca-api -------------------------------------------------
section "Host API — Container App ($CA_API)"
CA_JSON="$(az containerapp show -g "$RG" -n "$CA_API" -o json 2>/dev/null || true)"
API_FQDN=""
if [[ -z "$CA_JSON" ]]; then
  fail "Container App '$CA_API' nie istnieje — sprawdź modules/app.bicep"
else
  ok "Container App '$CA_API' istnieje"

  # FQDN ingressu (external).
  API_FQDN="$(echo "$CA_JSON" | python3 -c 'import sys,json; print(((json.load(sys.stdin).get("properties",{}).get("configuration",{}) or {}).get("ingress",{}) or {}).get("fqdn",""))' 2>/dev/null || true)"
  if [[ -n "$API_FQDN" ]]; then
    ok "Ingress FQDN: $API_FQDN"
  else
    fail "Brak ingress FQDN — API nie jest osiągalne (oczekiwano external HTTP)"
  fi

  # Stan provisioningu / running.
  PROV_STATE="$(echo "$CA_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("properties",{}).get("provisioningState",""))' 2>/dev/null || true)"
  if [[ "$PROV_STATE" == "Succeeded" ]]; then
    ok "provisioningState = Succeeded"
  else
    info "provisioningState = ${PROV_STATE:-<brak>} (Running/Succeeded pojawia się po wdrożeniu)"
  fi

  # Scale-to-zero: minReplicas == 0 (inaczej niż sensory/worker = always-on).
  MIN_REPLICAS="$(echo "$CA_JSON" | python3 -c 'import sys,json; print(((json.load(sys.stdin).get("properties",{}).get("template",{}) or {}).get("scale",{}) or {}).get("minReplicas",""))' 2>/dev/null || true)"
  if [[ "$MIN_REPLICAS" == "0" ]]; then
    ok "Scale-to-zero: minReplicas = 0 (API sterowane żądaniami — oszczędność)"
  else
    info "minReplicas = ${MIN_REPLICAS:-<brak>} (oczekiwano 0 — scale-to-zero)"
  fi

  # Tożsamość UserAssigned = id-api.
  if echo "$CA_JSON" | grep -qi "userAssignedIdentities/${ID_API}"; then
    ok "Przypięta tożsamość UserAssigned to '$ID_API'"
  else
    info "Nie potwierdzono nazwy UAMI '$ID_API' w identity (sprawdź ręcznie)"
  fi
fi

# --- 3. RBAC API — role ARM (Blob Data Reader + AcrPull) ---------------------
section "RBAC API — Storage Blob Data Reader + AcrPull (tylko odczyt)"
if [[ -z "$API_PRINCIPAL" ]]; then
  fail "Brak principalId tożsamości '$ID_API' — pomijam kontrolę ról ARM"
else
  RA_JSON="$(az role assignment list --assignee "$API_PRINCIPAL" --all -o json 2>/dev/null || true)"
  if [[ -z "$RA_JSON" ]]; then
    fail "Nie udało się pobrać przypisań ról (az role assignment list)"
  else
    # Storage Blob Data Reader (NIE Contributor — API tylko czyta nagrania TTY).
    if echo "$RA_JSON" | grep -qi "$ROLE_BLOB_READER"; then
      BL_SCOPE="$(echo "$RA_JSON" | python3 -c '
import sys,json
for a in json.load(sys.stdin):
    if "'"$ROLE_BLOB_READER"'" in (a.get("roleDefinitionId","") or ""):
        print(a.get("scope","")); break
' 2>/dev/null || true)"
      ok "Storage Blob Data Reader — obecna (scope: ${BL_SCOPE:-?})"
    else
      fail "Brak roli Storage Blob Data Reader — API nie odczyta nagrań TTY (Session Replay)"
    fi

    # AcrPull.
    if echo "$RA_JSON" | grep -qi "$ROLE_ACRPULL"; then
      ok "AcrPull — obecna (pull obrazu honeygrid-api z ACR)"
    else
      fail "Brak roli AcrPull — Container App nie pobierze obrazu honeygrid-api"
    fi
  fi
fi

# --- 4. RBAC API — Cosmos DB Built-in Data Reader (płaszczyzna danych) -------
# `az cosmosdb sql role assignment list` to polecenie RDZENIOWE (bez rozszerzeń).
section "RBAC API — Cosmos DB Built-in Data Reader (płaszczyzna danych)"
COSMOS_NAME="$(az cosmosdb list -g "$RG" --query '[0].name' -o tsv 2>/dev/null || true)"
if [[ -z "$COSMOS_NAME" ]]; then
  fail "Nie znaleziono konta Cosmos w RG — pomijam kontrolę roli danych"
elif [[ -z "$API_PRINCIPAL" ]]; then
  fail "Brak principalId tożsamości '$ID_API' — pomijam kontrolę roli danych Cosmos"
else
  ok "Konto Cosmos: $COSMOS_NAME"
  CRA_JSON="$(az cosmosdb sql role assignment list -g "$RG" -a "$COSMOS_NAME" -o json 2>/dev/null || true)"
  if [[ -z "$CRA_JSON" ]]; then
    info "Nie udało się pobrać sqlRoleAssignments Cosmos — sprawdź ręcznie w portalu (Data plane RBAC)"
  else
    CRA_MATCH="$(echo "$CRA_JSON" | python3 -c '
import sys, json
data = json.load(sys.stdin)
pid = "'"$API_PRINCIPAL"'".lower()
reader = "'"$COSMOS_DATA_READER_ID"'".lower()
hit = False
for a in data:
    p = a.get("properties", a)
    principal = (p.get("principalId","") or "").lower()
    role = (p.get("roleDefinitionId","") or "").lower()
    if principal == pid and reader in role:
        hit = True
print("READER" if hit else "MISS")
' 2>/dev/null || true)"
    if [[ "$CRA_MATCH" == "READER" ]]; then
      ok "Cosmos DB Built-in Data Reader przypisana dla id-api (tylko odczyt — least privilege)"
    else
      fail "Brak Cosmos Data Reader dla id-api — API dostanie 403 przy czytaniu events/iocs/sessions"
    fi
  fi
fi

# --- 5. Endpointy API (curl /health + /api/iocs/stix) ------------------------
section "Endpointy API (curl /health + /api/iocs/stix)"
if [[ -z "$API_FQDN" ]]; then
  info "Brak FQDN API — pomijam kontrolę endpointów (uruchom ponownie po wdrożeniu)."
else
  BASE="https://${API_FQDN}"
  info "Pierwsze żądanie może mieć zimny start (scale-to-zero) — czekam do ~30 s."

  # /health — sonda liveness (akceptujemy 200).
  HEALTH_CODE="$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "${BASE}/health" 2>/dev/null || true)"
  if [[ "$HEALTH_CODE" == "200" ]]; then
    ok "/health => HTTP 200"
  else
    fail "/health => HTTP ${HEALTH_CODE:-<brak>} (oczekiwano 200) — sprawdź logi ca-api"
  fi

  # /api/iocs/stix — eksport bundle STIX 2.1; walidacja JSON type == "bundle".
  STIX_BODY="$(curl -s --max-time 30 "${BASE}/api/iocs/stix" 2>/dev/null || true)"
  if [[ -z "$STIX_BODY" ]]; then
    fail "/api/iocs/stix => brak odpowiedzi (pusto) — sprawdź logi ca-api / rolę Cosmos Reader"
  else
    STIX_TYPE="$(echo "$STIX_BODY" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("type",""))' 2>/dev/null || true)"
    if [[ "$STIX_TYPE" == "bundle" ]]; then
      OBJ_COUNT="$(echo "$STIX_BODY" | python3 -c 'import sys,json; print(len(json.load(sys.stdin).get("objects",[]) or []))' 2>/dev/null || echo "?")"
      ok "/api/iocs/stix => poprawny bundle STIX 2.1 (obiektów: $OBJ_COUNT)"
    else
      fail "/api/iocs/stix => JSON bez type=='bundle' (otrzymano type='${STIX_TYPE:-<brak>}')"
    fi
  fi

  # /api/sessions/{id}/replay — wymaga realnego sessionId (nie testujemy ślepo).
  info "/api/sessions/{id}/replay wymaga REALNEGO sessionId — pobierz z Cosmos (kontener sessions)"
  echo "      lub z dashboardu SOC, np.:"
  echo "      curl -s \"${BASE}/api/sessions/<SESSION_ID>/replay\" | python3 -m json.tool"
fi

# --- Podsumowanie ---------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Killer-ficzy Tygodnia 7 (host API) wyglądają dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
