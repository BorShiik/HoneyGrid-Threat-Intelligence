#!/usr/bin/env bash
#
# verify-week2.sh — weryfikacja sensorów HoneyGrid (Tydzień 2, Track A).
#
# Sprawdza, czy 3 Container Apps sensorów (cowrie, web-honeypot, tcp-listener)
# istnieją i działają, mają poprawny ingress, podpiętą tożsamość zarządzaną
# sensorów oraz czy do Event Hubs faktycznie wpływają zdarzenia.
#
# Użycie:
#   ./infra/scripts/verify-week2.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week2.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... list/show/metrics).
# Każda kontrola wypisuje ✅ (OK) lub ❌ (problem) i idzie dalej, żeby pokazać
# pełny obraz. "Resource not found" => czytelne ❌, a nie wywrotka skryptu.

set -euo pipefail

RG="${1:-hg-dev-rg}"

# Nazwy zasobów (spójne z konwencją namePrefix=hg, environment=dev w bicep).
PREFIX="hg-dev"
CA_COWRIE="${PREFIX}-ca-cowrie"
CA_WEB="${PREFIX}-ca-web"
CA_TCP="${PREFIX}-ca-tcp"
ID_SENSOR="${PREFIX}-id-sensor"

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
# Rozszerzenie containerapp bywa potrzebne do niektórych pól — ostrzegamy, nie blokujemy.
if ! az extension show -n containerapp >/dev/null 2>&1; then
  echo "ℹ️  Brak rozszerzenia 'containerapp' — w razie błędów: az extension add -n containerapp" >&2
fi

echo "HoneyGrid — weryfikacja Tygodnia 2 (sensory)"
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

# Pobierz principalId tożsamości sensorów (do kontroli przypisania do aplikacji).
SENSOR_PRINCIPAL="$(az identity show -g "$RG" -n "$ID_SENSOR" \
  --query principalId -o tsv 2>/dev/null || true)"
if [[ -n "$SENSOR_PRINCIPAL" ]]; then
  ok "Tożsamość sensorów '$ID_SENSOR' istnieje (principalId: $SENSOR_PRINCIPAL)"
else
  fail "Brak tożsamości sensorów '$ID_SENSOR' — sensory nie będą mogły pullować obrazów ani wysyłać telemetrii"
fi

# --- Funkcja kontroli pojedynczej aplikacji sensora --------------------------
# Argumenty: $1 = nazwa Container App, $2 = etykieta opisowa
check_app() {
  local APP="$1"
  local LABEL="$2"
  section "Sensor: $LABEL ($APP)"

  # Czy aplikacja w ogóle istnieje (defensywnie: brak => czytelne ❌).
  local APP_JSON
  APP_JSON="$(az containerapp show -g "$RG" -n "$APP" -o json 2>/dev/null || true)"
  if [[ -z "$APP_JSON" ]]; then
    fail "Container App '$APP' nie istnieje (resource not found)"
    return
  fi
  ok "Container App '$APP' istnieje"

  # Stan provisioningu i działania.
  local PROV RUN
  PROV="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.provisioningState' -o tsv 2>/dev/null || true)"
  RUN="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.runningStatus' -o tsv 2>/dev/null || true)"
  if [[ "$PROV" == "Succeeded" ]]; then
    ok "provisioningState = Succeeded"
  else
    fail "provisioningState = ${PROV:-<brak>} (oczekiwano Succeeded)"
  fi
  if [[ "$RUN" == "Running" ]]; then
    ok "runningStatus = Running"
  else
    fail "runningStatus = ${RUN:-<brak>} (oczekiwano Running)"
  fi

  # minReplicas musi być >= 1 (honeypot zawsze-on; scale-to-zero zabronione).
  local MIN_REPL
  MIN_REPL="$(az containerapp show -g "$RG" -n "$APP" \
              --query 'properties.template.scale.minReplicas' -o tsv 2>/dev/null || true)"
  if [[ -n "$MIN_REPL" && "$MIN_REPL" -ge 1 ]]; then
    ok "minReplicas = $MIN_REPL (zawsze-on — poprawnie)"
  else
    fail "minReplicas = ${MIN_REPL:-<brak>} (honeypot MUSI mieć >= 1 — inaczej przegapi ataki)"
  fi

  # Ingress: FQDN + porty.
  local FQDN TARGET EXPOSED TRANSPORT
  FQDN="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.configuration.ingress.fqdn' -o tsv 2>/dev/null || true)"
  TRANSPORT="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.configuration.ingress.transport' -o tsv 2>/dev/null || true)"
  TARGET="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.configuration.ingress.targetPort' -o tsv 2>/dev/null || true)"
  EXPOSED="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.configuration.ingress.exposedPort' -o tsv 2>/dev/null || true)"
  if [[ -n "$FQDN" ]]; then
    ok "Ingress FQDN: $FQDN (transport=$TRANSPORT, targetPort=$TARGET, exposedPort=${EXPOSED:-auto})"
  else
    fail "Brak ingress FQDN — sensor nie jest osiągalny z zewnątrz"
  fi
  # Dodatkowe mapowania portów (np. Telnet 23, RDP 3389) — tylko informacyjnie.
  local ADDL
  ADDL="$(az containerapp show -g "$RG" -n "$APP" \
          --query 'properties.configuration.ingress.additionalPortMappings' -o json 2>/dev/null || echo '[]')"
  if [[ "$ADDL" != "[]" && "$ADDL" != "null" && -n "$ADDL" ]]; then
    echo "  ℹ️  Dodatkowe mapowania portów: $(echo "$ADDL" | tr -d '\n ')"
  fi

  # Tożsamość zarządzana sensorów przypisana do aplikacji.
  local APP_IDS
  APP_IDS="$(az containerapp show -g "$RG" -n "$APP" \
             --query 'identity.userAssignedIdentities' -o tsv 2>/dev/null || true)"
  if echo "$APP_IDS" | grep -qi "$ID_SENSOR"; then
    ok "Przypisana tożsamość sensorów '$ID_SENSOR' (User-Assigned)"
  else
    fail "Aplikacja '$APP' NIE ma przypisanej tożsamości '$ID_SENSOR' — pull z ACR i telemetria zawiodą"
  fi
}

# --- 1-3. Trzy sensory --------------------------------------------------------
check_app "$CA_COWRIE" "Cowrie (SSH/Telnet honeypot, 2 kontenery: cowrie + shipper)"

# Cowrie to wzorzec sidecar: musi mieć 2 kontenery (cowrie + cowrie-shipper).
section "Cowrie — kontrola sidecara (2 kontenery)"
CONT_COUNT="$(az containerapp show -g "$RG" -n "$CA_COWRIE" \
  --query 'length(properties.template.containers)' -o tsv 2>/dev/null || echo 0)"
if [[ "$CONT_COUNT" -ge 2 ]]; then
  ok "Cowrie ma $CONT_COUNT kontenery (oczekiwano 2: cowrie + cowrie-shipper)"
  az containerapp show -g "$RG" -n "$CA_COWRIE" \
    --query 'properties.template.containers[].name' -o tsv 2>/dev/null \
    | sed 's/^/       - kontener: /' || true
else
  fail "Cowrie ma $CONT_COUNT kontener(ów) — sidecar CowrieShipper prawdopodobnie nie wdrożony"
fi

check_app "$CA_WEB" "Web honeypot (fałszywy panel logowania HTTP, port 8080)"
check_app "$CA_TCP" "TCP listener (generyczny nasłuch 23/3389)"

# --- 4. Event Hubs: czy wpływają zdarzenia -----------------------------------
section "Event Hubs — wlot telemetrii (IncomingMessages)"
# Namespace ma losowy sufiks (uniqueString) — szukamy go po prefiksie.
EH_NS="$(az eventhubs namespace list -g "$RG" \
  --query "[?starts_with(name, '${PREFIX}-ehns')].name | [0]" -o tsv 2>/dev/null || true)"
if [[ -z "$EH_NS" ]]; then
  fail "Nie znaleziono namespace Event Hubs (prefiks '${PREFIX}-ehns') w grupie '$RG'"
else
  ok "Namespace Event Hubs: $EH_NS"
  EH_ID="$(az eventhubs namespace show -g "$RG" -n "$EH_NS" --query id -o tsv 2>/dev/null || true)"
  # Suma przychodzących wiadomości za ostatnią godzinę. Brak danych => 0 (sensory
  # mogą jeszcze nic nie odebrać — to wskazówka, nie twardy błąd).
  INCOMING="$(az monitor metrics list --resource "$EH_ID" \
      --metric IncomingMessages --aggregation Total --interval PT1H \
      --query 'value[0].timeseries[0].data[-1].total' -o tsv 2>/dev/null || true)"
  if [[ -n "$INCOMING" && "$INCOMING" != "None" ]]; then
    # Bash nie liczy floatów — porównanie przez awk.
    if awk "BEGIN{exit !($INCOMING > 0)}"; then
      ok "IncomingMessages (ost. godz.) = $INCOMING — telemetria DOCIERA do Event Hubs"
    else
      echo "  ℹ️  IncomingMessages (ost. godz.) = 0 — jeszcze brak zdarzeń."
      echo "     To normalne tuż po wdrożeniu lub bez ruchu. Wygeneruj ruch na sensory"
      echo "     (np. ssh / curl na ich FQDN) i sprawdź ponownie za kilka minut."
    fi
  else
    echo "  ℹ️  Brak danych metryki IncomingMessages (możliwe opóźnienie metryk lub zero ruchu)."
  fi
  echo
  echo "  Ręczna kontrola wlotu zdarzeń (kopiuj-wklej):"
  echo "    az monitor metrics list --resource \"$EH_ID\" \\"
  echo "      --metric IncomingMessages --aggregation Total --interval PT5M -o table"
fi

# --- 5. Przypomnienie: jak otworzyć port na żywo demo obrony ------------------
section "Demo obrony — otwarcie portu na żywo"
echo "  ℹ️  Sensory SSH/Telnet/RDP wystawiają porty przez ingress TCP Container Apps."
echo "     OGRANICZENIE: środowisko Consumption-only bywa ograniczone w wystawianiu"
echo "     wielu zewnętrznych portów TCP / niskich portów (22/23/3389). Jeśli port"
echo "     nie odpowiada z Internetu na demo:"
echo
echo "     1) Sprawdź FQDN i port aplikacji:"
echo "          az containerapp show -g $RG -n $CA_COWRIE \\"
echo "            --query 'properties.configuration.ingress' -o json"
echo "     2) Połącz się testowo (z lokalnej maszyny), używając wystawionego portu:"
echo "          ssh -p 22 root@<cowrie-fqdn>        # hasło: 123456 (dozwolone w userdb.txt)"
echo "     3) Jeśli potrzeba pewnego wystawienia 22/23/3389 — zmigruj środowisko"
echo "        Container Apps na profile obciążenia (workload profiles) lub zmapuj"
echo "        statyczny IP środowiska (env -> static IP) regułą NAT. Patrz UWAGA/TODO"
echo "        w infra/bicep/modules/app.bicep."

# --- Podsumowanie -------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Sensory Tygodnia 2 wyglądają dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
