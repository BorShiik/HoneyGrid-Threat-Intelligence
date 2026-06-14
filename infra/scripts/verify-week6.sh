#!/usr/bin/env bash
#
# verify-week6.sh — weryfikacja SOAR / auto-mitygacji (Tydzień 6, Track A).
#
# Sprawdza pełny łańcuch automatycznej reakcji na incydent:
#   Playbook (Logic App Consumption) 'hg-{env}-pb-block-ip' (Microsoft.Web/workflows)
#     z tożsamością UserAssigned (hg-{env}-id-playbook), stan Enabled,
#   API connection 'hg-{env}-con-sentinel' (Microsoft.Web/connections, auth MSI),
#   Automation Rule Sentinela (Microsoft.SecurityInsights/automationRules) odpalająca playbook,
#   role MI playbooka ZAWĘŻONE (Sentinel Responder na RG, Network Contributor TYLKO na
#     NSG dmz, Storage Blob Data Contributor na koncie storage),
#   kontener 'edl' (feed EDL: blocked-ips.txt),
#   oraz EFEKT MITYGACJI (money shot po incydencie): reguły Deny 'Block-<ip>' w NSG dmz
#     + zawartość edl/blocked-ips.txt,
#   na koniec gotowy blok symulacji ataku + ścieżka ręcznego Run playbook.
#
# Użycie:
#   ./infra/scripts/verify-week6.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week6.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... show/list + az rest GET).
# Wszystkie wywołania są BEZ ROZSZERZEŃ az (maszyna dev nie instaluje rozszerzeń —
# pip pada). NIE używamy `az monitor ...` / `az logic ...` — tylko az resource
# show/list, az rest, az role assignment list, az network nsg rule list,
# az storage ... oraz az identity / az account (polecenia rdzeniowe).
#
# WDROŻENIE wykonuje użytkownik (Azure RG bywa kasowane między sesjami) —
# patrz docs/tydzien-6-soar.md (krok Dzień 0 + az deployment sub create).

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
PLAYBOOK_NAME="${PREFIX}-pb-block-ip"
CONNECTION_NAME="${PREFIX}-con-sentinel"
ID_PLAYBOOK="${PREFIX}-id-playbook"
LAW_NAME="${PREFIX}-law"
NSG_DMZ="${PREFIX}-nsg-dmz"
CA_WEB="${PREFIX}-ca-web"
EDL_CONTAINER="edl"
EDL_BLOB="blocked-ips.txt"

# Dobrze znane GUID-y ról (muszą zgadzać się z modules/playbook.bicep).
ROLE_SENTINEL_RESPONDER="3e150937-b8fe-4cfb-8069-0eaf05ecd056"  # Microsoft Sentinel Responder
ROLE_NETWORK_CONTRIBUTOR="4d97b98b-1d4f-4787-a291-c67834d212e7" # Network Contributor
ROLE_BLOB_CONTRIBUTOR="ba92f5b4-2d11-453d-a403-e96b0029c9fe"    # Storage Blob Data Contributor

# Wersje API płaszczyzny zarządzania.
API_WORKFLOWS="2019-05-01"
API_SECURITYINSIGHTS="2023-11-01"

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

SUB_ID="$(az account show --query id -o tsv 2>/dev/null || true)"

echo "HoneyGrid — weryfikacja Tygodnia 6 (SOAR: playbook + automation rule + auto-mitygacja NSG/EDL)"
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
  info "Wdrożenie wykonuje użytkownik — patrz docs/tydzien-6-soar.md (Dzień 0:"
  echo "      pobranie objectId SP Sentinela + az deployment sub create ...)."
  echo "      Po wdrożeniu uruchom ten skrypt ponownie."
  exit 1
fi

# --- 1. Playbook (Logic App Consumption) -------------------------------------
# UWAGA: zamiast `az logic workflow ...` (rozszerzenie 'logic', pip pada)
# używamy generycznego `az resource show` + `az rest` GET na właściwości.
section "Playbook — Logic App ($PLAYBOOK_NAME)"
PB_JSON="$(az resource show -g "$RG" -n "$PLAYBOOK_NAME" \
  --resource-type Microsoft.Web/workflows -o json 2>/dev/null || true)"
if [[ -z "$PB_JSON" ]]; then
  fail "Playbook '$PLAYBOOK_NAME' nie istnieje (resource not found) — sprawdź modules/playbook.bicep"
else
  ok "Playbook '$PLAYBOOK_NAME' istnieje"

  # Tożsamość: ma być UserAssigned (hg-{env}-id-playbook), nie SystemAssigned.
  IDENTITY_TYPE="$(echo "$PB_JSON" | python3 -c 'import sys,json; print((json.load(sys.stdin).get("identity") or {}).get("type",""))' 2>/dev/null || true)"
  if echo "$IDENTITY_TYPE" | grep -qi "UserAssigned"; then
    ok "Tożsamość playbooka: UserAssigned (oczekiwano '$ID_PLAYBOOK')"
    if echo "$PB_JSON" | grep -qi "userAssignedIdentities/${ID_PLAYBOOK}"; then
      ok "Przypięta tożsamość to '$ID_PLAYBOOK'"
    else
      info "Nie potwierdzono nazwy UAMI w identity (sprawdź ręcznie, że to '$ID_PLAYBOOK')"
    fi
  else
    fail "Tożsamość playbooka nie jest UserAssigned (type='${IDENTITY_TYPE:-<brak>}') — bezkluczowość/RBAC nie zadziała"
  fi

  # Stan workflow: properties.state == 'Enabled' (az rest GET na zasób workflow).
  PB_ID="$(echo "$PB_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null || true)"
  PB_STATE="$(az rest --method get \
    --url "https://management.azure.com${PB_ID}?api-version=${API_WORKFLOWS}" \
    --query 'properties.state' -o tsv 2>/dev/null || true)"
  if [[ "$PB_STATE" == "Enabled" ]]; then
    ok "Stan playbooka: Enabled"
  else
    fail "Stan playbooka: ${PB_STATE:-<brak>} (oczekiwano Enabled — wyłączony playbook nie zareaguje)"
  fi
fi

# --- 2. API connection do Sentinela (auth MSI) -------------------------------
section "API connection ($CONNECTION_NAME)"
CON_JSON="$(az resource show -g "$RG" -n "$CONNECTION_NAME" \
  --resource-type Microsoft.Web/connections -o json 2>/dev/null || true)"
if [[ -z "$CON_JSON" ]]; then
  fail "Connection '$CONNECTION_NAME' nie istnieje (resource not found) — sprawdź modules/playbook.bicep"
else
  ok "Connection '$CONNECTION_NAME' istnieje"
  CON_STATUS="$(echo "$CON_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("properties",{}).get("overallStatus",""))' 2>/dev/null || true)"
  if [[ "$CON_STATUS" == "Connected" ]]; then
    ok "overallStatus = Connected (konektor zautoryzowany — MSI działa)"
  elif [[ -n "$CON_STATUS" ]]; then
    info "overallStatus = $CON_STATUS — pierwsze uruchomienie playbooka weryfikuje autoryzację MSI konektora"
  else
    info "Nie odczytano overallStatus (różnice wersji) — sprawdź w portalu (API connections)"
  fi
fi

# --- 3. Automation Rule Sentinela --------------------------------------------
# az rest GET na provider SecurityInsights w zakresie workspace'a (bez rozszerzeń).
section "Automation Rule (Microsoft.SecurityInsights/automationRules)"
if [[ -z "$SUB_ID" ]]; then
  fail "Brak subskrypcji (az account show) — pomijam kontrolę automation rules"
else
  AR_URL="https://management.azure.com/subscriptions/${SUB_ID}/resourceGroups/${RG}/providers/Microsoft.OperationalInsights/workspaces/${LAW_NAME}/providers/Microsoft.SecurityInsights/automationRules?api-version=${API_SECURITYINSIGHTS}"
  AR_JSON="$(az rest --method get --url "$AR_URL" -o json 2>/dev/null || true)"
  if [[ -z "$AR_JSON" ]]; then
    fail "Nie udało się pobrać automation rules — sprawdź workspace '$LAW_NAME' i onboarding Sentinela"
  else
    AR_MATCH="$(echo "$AR_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
rules = d.get("value", [])
hits = 0
for r in rules:
    p = r.get("properties", {}) or {}
    blob = json.dumps(p)
    # Reguła wiążąca playbook: w actions występuje runPlaybook / logicAppResourceId.
    if "pb-block-ip" in blob or "runPlaybook" in blob or "logicAppResourceId" in blob:
        hits += 1
        dn = p.get("displayName", "?")
        order = p.get("order", "?")
        triggering = p.get("triggeringLogic", {}) or {}
        enabled = triggering.get("isEnabled")
        trig = triggering.get("triggersOn", "?")
        when = triggering.get("triggersWhen", "?")
        print("  ✅ %s | order=%s | enabled=%s | %s/%s" % (dn, order, enabled, trig, when))
print("COUNT=%d" % hits)
' 2>/dev/null || true)"
    AR_COUNT="$(echo "$AR_MATCH" | sed -n 's/^COUNT=//p')"
    AR_COUNT="${AR_COUNT:-0}"
    echo "$AR_MATCH" | grep -v '^COUNT=' || true
    if [[ "$AR_COUNT" -ge 1 ]]; then
      ok "Automation rule odpalająca playbook: $AR_COUNT (oczekiwano >= 1)"
    else
      fail "Brak automation rule wiążącej playbook '$PLAYBOOK_NAME' — incydent nie uruchomi mitygacji"
    fi
  fi
fi

# --- 4. RBAC playbooka — role ZAWĘŻONE ---------------------------------------
# 3 role: Sentinel Responder (RG), Network Contributor (TYLKO NSG dmz),
# Storage Blob Data Contributor (storage). Network Contributor na NSG, nie na RG.
section "RBAC playbooka — Sentinel Responder + Network Contributor (NSG) + Blob"
PB_PRINCIPAL="$(az identity show -g "$RG" -n "$ID_PLAYBOOK" \
  --query principalId -o tsv 2>/dev/null || true)"
if [[ -z "$PB_PRINCIPAL" ]]; then
  fail "Brak tożsamości playbooka '$ID_PLAYBOOK' — pomijam kontrolę ról"
else
  ok "principalId tożsamości '$ID_PLAYBOOK': $PB_PRINCIPAL"
  RA_JSON="$(az role assignment list --assignee "$PB_PRINCIPAL" --all -o json 2>/dev/null || true)"
  if [[ -z "$RA_JSON" ]]; then
    fail "Nie udało się pobrać przypisań ról (az role assignment list)"
  else
    # Sentinel Responder.
    if echo "$RA_JSON" | grep -qi "$ROLE_SENTINEL_RESPONDER"; then
      SR_SCOPE="$(echo "$RA_JSON" | python3 -c '
import sys,json
for a in json.load(sys.stdin):
    if "'"$ROLE_SENTINEL_RESPONDER"'" in (a.get("roleDefinitionId","") or ""):
        print(a.get("scope","")); break
' 2>/dev/null || true)"
      ok "Microsoft Sentinel Responder — obecna (scope: ${SR_SCOPE:-?})"
    else
      fail "Brak roli Microsoft Sentinel Responder — playbook nie zamknie/skomentuje incydentu"
    fi

    # Network Contributor — i kontrola least privilege (ma być NSG, nie RG).
    if echo "$RA_JSON" | grep -qi "$ROLE_NETWORK_CONTRIBUTOR"; then
      NC_SCOPE="$(echo "$RA_JSON" | python3 -c '
import sys,json
for a in json.load(sys.stdin):
    if "'"$ROLE_NETWORK_CONTRIBUTOR"'" in (a.get("roleDefinitionId","") or ""):
        print(a.get("scope","")); break
' 2>/dev/null || true)"
      ok "Network Contributor — obecna (scope: ${NC_SCOPE:-?})"
      if echo "$NC_SCOPE" | grep -qi "networkSecurityGroups/${NSG_DMZ}\$"; then
        ok "Zakres Network Contributor ZAWĘŻONY do NSG '$NSG_DMZ' (least privilege ✅)"
      elif echo "$NC_SCOPE" | grep -qiE "resourceGroups/${RG}\$"; then
        fail "Network Contributor na CAŁEJ RG — za szeroko; oczekiwano tylko NSG '$NSG_DMZ'"
      else
        info "Zakres Network Contributor nie wskazuje wprost NSG '$NSG_DMZ' — sprawdź ręcznie"
      fi
    else
      fail "Brak roli Network Contributor — playbook nie doda reguły Deny do NSG dmz"
    fi

    # Storage Blob Data Contributor.
    if echo "$RA_JSON" | grep -qi "$ROLE_BLOB_CONTRIBUTOR"; then
      BL_SCOPE="$(echo "$RA_JSON" | python3 -c '
import sys,json
for a in json.load(sys.stdin):
    if "'"$ROLE_BLOB_CONTRIBUTOR"'" in (a.get("roleDefinitionId","") or ""):
        print(a.get("scope","")); break
' 2>/dev/null || true)"
      ok "Storage Blob Data Contributor — obecna (scope: ${BL_SCOPE:-?})"
    else
      fail "Brak roli Storage Blob Data Contributor — playbook nie zapisze EDL (blocked-ips.txt)"
    fi
  fi
fi

# --- Wspólny krok: nazwa konta storage w RG ----------------------------------
# EDL i blob wiszą na koncie storage; nazwy storage są globalnie unikalne
# (z sufiksem), więc wykrywamy je dynamicznie zamiast zgadywać.
SA_NAME="$(az storage account list -g "$RG" --query '[0].name' -o tsv 2>/dev/null || true)"

# --- 5. Kontener EDL ----------------------------------------------------------
section "Kontener EDL ('$EDL_CONTAINER') na koncie storage"
if [[ -z "$SA_NAME" ]]; then
  fail "Nie znaleziono konta storage w RG — pomijam kontrolę kontenera EDL"
else
  ok "Konto storage: $SA_NAME"
  if az storage container show --account-name "$SA_NAME" -n "$EDL_CONTAINER" \
       --auth-mode login -o none 2>/dev/null; then
    ok "Kontener '$EDL_CONTAINER' istnieje"
  else
    # Fallback: lista blobów (czasem container show pada na uprawnieniach data-plane).
    if az storage blob list --account-name "$SA_NAME" -c "$EDL_CONTAINER" \
         --auth-mode login -o none 2>/dev/null; then
      ok "Kontener '$EDL_CONTAINER' istnieje (potwierdzony przez blob list)"
    else
      fail "Kontener '$EDL_CONTAINER' nie istnieje LUB brak uprawnień data-plane (Storage Blob Data Reader)"
      info "Jeśli to kwestia uprawnień: nadaj sobie 'Storage Blob Data Reader' na koncie '$SA_NAME'."
    fi
  fi
fi

# --- 6. EFEKT MITYGACJI (MONEY SHOT — po incydencie) -------------------------
# Reguły Deny 'Block-<ip>' dodawane przez playbook do NSG dmz + zawartość EDL.
section "Efekt mitygacji — reguły Deny w NSG '$NSG_DMZ' + zawartość EDL"
BLOCK_RULES="$(az network nsg rule list -g "$RG" --nsg-name "$NSG_DMZ" \
  --query "[?starts_with(name,'Block-')].{name:name, src:sourceAddressPrefix, access:access, prio:priority}" \
  -o table 2>/dev/null || true)"
BLOCK_COUNT="$(az network nsg rule list -g "$RG" --nsg-name "$NSG_DMZ" \
  --query "length([?starts_with(name,'Block-')])" -o tsv 2>/dev/null || true)"
if [[ -z "$BLOCK_COUNT" ]]; then
  fail "Nie udało się odczytać reguł NSG '$NSG_DMZ' (resource not found?) — sprawdź wdrożenie sieci"
elif [[ "$BLOCK_COUNT" -gt 0 ]]; then
  ok "Reguły Deny 'Block-*' w NSG '$NSG_DMZ': $BLOCK_COUNT 🎉"
  echo "$BLOCK_RULES" | sed 's/^/    /'
else
  info "Brak reguł 'Block-*' (na razie). To NORMALNE przed pierwszym incydentem."
fi

# Zawartość EDL (blocked-ips.txt).
if [[ -n "$SA_NAME" ]]; then
  if az storage blob download --account-name "$SA_NAME" -c "$EDL_CONTAINER" \
       -n "$EDL_BLOB" --auth-mode login -f /tmp/edl.txt -o none 2>/dev/null; then
    EDL_LINES="$(grep -c . /tmp/edl.txt 2>/dev/null || echo 0)"
    if [[ "$EDL_LINES" -gt 0 ]]; then
      ok "EDL '$EDL_CONTAINER/$EDL_BLOB' zawiera $EDL_LINES wpis(ów) IP 🎉:"
      sed 's/^/    /' /tmp/edl.txt
    else
      info "EDL '$EDL_BLOB' istnieje, ale jest pusty (jeszcze brak zablokowanych IP)."
    fi
  else
    info "Brak blobu '$EDL_BLOB' lub brak uprawnień — pojawia się po pierwszym incydencie."
  fi
fi
info "Reguły/EDL pojawiają się ~kilka minut po incydencie. Jeśli pusto — uruchom symulację (sekcja 7)."

# --- 7. Symulacja ataku + ręczny Run playbook --------------------------------
section "Symulacja ataku (copy-paste) — wywołanie incydentu i auto-mitygacji"
WEB_FQDN="$(az containerapp show -g "$RG" -n "$CA_WEB" \
  --query 'properties.configuration.ingress.fqdn' -o tsv 2>/dev/null || true)"
if [[ -z "$WEB_FQDN" ]]; then
  info "Nie udało się odczytać FQDN web-sensora ('$CA_WEB') — podstaw własny adres ręcznie."
  WEB_FQDN="<FQDN-web>"
else
  ok "Web-sensor FQDN: $WEB_FQDN"
fi
echo
echo "  Wklej do terminala (mini-Hydra: >15 prób z jednego IP -> przekroczenie progu brute-force):"
echo
echo "    for i in \$(seq 1 25); do"
echo "      curl -s -X POST \"https://${WEB_FQDN}/wp-login.php\" -d \"log=admin&pwd=pass\$i\" -o /dev/null"
echo "    done"
echo
info "Przepływ: reguła Sentinela -> incydent (z encją IP) -> automation rule -> playbook ->"
echo "      reguła Deny w NSG dmz + dopis do EDL + powiadomienie webhook + komentarz/zamknięcie incydentu."
info "Czas: incydent ~5-10 min po ataku, mitygacja kilka-kilkanaście sek. po incydencie."
echo
info "Ścieżka RĘCZNA (test playbooka PRZED poleganiem na automation rule):"
echo "      Microsoft Sentinel -> Incidents -> wybierz incydent -> Run playbook -> '$PLAYBOOK_NAME'."

# --- Podsumowanie ---------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Warstwa SOAR Tygodnia 6 wygląda dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
