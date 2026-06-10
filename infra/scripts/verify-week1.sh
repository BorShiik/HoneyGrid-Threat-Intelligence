#!/usr/bin/env bash
#
# verify-week1.sh — weryfikacja infrastruktury HoneyGrid po wdrożeniu Tygodnia 1.
#
# Użycie:
#   ./infra/scripts/verify-week1.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week1.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wykonuje wyłącznie odczyty (az ... list/show).
# Każda kontrola wypisuje ✅ (OK) lub ❌ (problem) i skrypt idzie dalej,
# żeby pokazać pełny obraz. Kod wyjścia != 0, jeśli cokolwiek padło.

set -euo pipefail

RG="${1:-hg-dev-rg}"
VNET="hg-dev-vnet"
NSG_DMZ="hg-dev-nsg-dmz"
ID_SENSOR="hg-dev-id-sensor"
ID_PLAYBOOK="hg-dev-id-playbook"

FAILURES=0

# Pomocnicze funkcje wypisujące wynik kontroli
ok()   { echo "  ✅ $1"; }
fail() { echo "  ❌ $1"; FAILURES=$((FAILURES + 1)); }
section() { echo; echo "=== $1 ==="; }

# --- Wymagania wstępne: az CLI i zalogowanie ---------------------------------
if ! command -v az >/dev/null 2>&1; then
  echo "❌ Brak Azure CLI (az). Zainstaluj: brew install azure-cli" >&2
  exit 1
fi
if ! az account show >/dev/null 2>&1; then
  echo "❌ Nie jesteś zalogowany/-a. Uruchom: az login" >&2
  exit 1
fi

echo "HoneyGrid — weryfikacja Tygodnia 1"
echo "Grupa zasobów: $RG"
echo "Subskrypcja:   $(az account show --query name -o tsv)"

# --- 0. Czy grupa zasobów w ogóle istnieje ------------------------------------
section "Grupa zasobów"
if [[ "$(az group exists -n "$RG")" == "true" ]]; then
  ok "Grupa zasobów '$RG' istnieje"
else
  fail "Grupa zasobów '$RG' NIE istnieje — najpierw wykonaj wdrożenie (az deployment sub create ...)"
  echo
  echo "Wynik: bez grupy zasobów dalsze kontrole nie mają sensu. Koniec."
  exit 1
fi

# --- 1. Liczba zasobów w grupie -----------------------------------------------
section "Liczba zasobów w grupie"
RES_COUNT="$(az resource list -g "$RG" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
if [[ "$RES_COUNT" -ge 20 ]]; then
  ok "W grupie jest $RES_COUNT zasobów (oczekiwano: kilkadziesiąt)"
else
  fail "W grupie jest tylko $RES_COUNT zasobów — wdrożenie wygląda na niekompletne"
fi

# --- 2. VNet + 3 podsieci ------------------------------------------------------
section "Sieć wirtualna i podsieci"
if az network vnet show -g "$RG" -n "$VNET" >/dev/null 2>&1; then
  ok "VNet '$VNET' istnieje"
  for SNET in snet-dmz snet-logic snet-data; do
    PREFIX="$(az network vnet subnet show -g "$RG" --vnet-name "$VNET" -n "$SNET" \
              --query addressPrefix -o tsv 2>/dev/null || true)"
    if [[ -n "$PREFIX" ]]; then
      ok "Podsieć '$SNET' istnieje ($PREFIX)"
    else
      fail "Brak podsieci '$SNET' w VNet '$VNET'"
    fi
  done
else
  fail "Brak VNet '$VNET'"
fi

# --- 3. NSG strefy DMZ: tabela reguł + kontrola anti-pivot ---------------------
section "NSG strefy DMZ ($NSG_DMZ)"
if az network nsg show -g "$RG" -n "$NSG_DMZ" >/dev/null 2>&1; then
  ok "NSG '$NSG_DMZ' istnieje — reguły poniżej:"
  echo
  az network nsg rule list -g "$RG" --nsg-name "$NSG_DMZ" -o table || true
  echo
  # Kluczowa reguła anti-pivot: Deny-Outbound-All (priorytet 4000, Outbound, Deny)
  RULE_INFO="$(az network nsg rule show -g "$RG" --nsg-name "$NSG_DMZ" \
               -n Deny-Outbound-All \
               --query '[direction, access, priority]' -o tsv 2>/dev/null || true)"
  if [[ -n "$RULE_INFO" ]]; then
    # -o tsv dla tablicy zwraca wartości rozdzielone tabulatorami w jednej linii
    IFS=$'\t' read -r DIRECTION ACCESS PRIORITY <<< "$RULE_INFO"
    if [[ "$DIRECTION" == "Outbound" && "$ACCESS" == "Deny" && "$PRIORITY" == "4000" ]]; then
      ok "Reguła anti-pivot 'Deny-Outbound-All' OK (Outbound / Deny / priorytet 4000)"
    else
      fail "Reguła 'Deny-Outbound-All' istnieje, ale ma złe parametry: direction=$DIRECTION access=$ACCESS priority=$PRIORITY (oczekiwano Outbound/Deny/4000)"
    fi
  else
    fail "BRAK reguły 'Deny-Outbound-All' w NSG '$NSG_DMZ' — honeypot NIE jest odizolowany (anti-pivot)!"
  fi
else
  fail "Brak NSG '$NSG_DMZ'"
fi

# --- 4. Private Endpoints -------------------------------------------------------
section "Private Endpoints (snet-data: Cosmos, Blob, Key Vault)"
PE_COUNT="$(az network private-endpoint list -g "$RG" --query 'length(@)' -o tsv 2>/dev/null || echo 0)"
if [[ "$PE_COUNT" -ge 3 ]]; then
  ok "Znaleziono $PE_COUNT Private Endpoint(y/ów) (oczekiwano: 3)"
else
  fail "Znaleziono tylko $PE_COUNT Private Endpoint(y/ów) — oczekiwano 3 (Cosmos, Blob, Key Vault)"
fi
# Stan każdego endpointu: provisioningState musi być Succeeded, połączenie Approved
PE_LIST="$(az network private-endpoint list -g "$RG" \
  --query "[].[name, provisioningState, privateLinkServiceConnections[0].privateLinkServiceConnectionState.status]" \
  -o tsv 2>/dev/null || true)"
if [[ -n "$PE_LIST" ]]; then
  while IFS=$'\t' read -r NAME PROV STATUS; do
    if [[ "$PROV" == "Succeeded" && "$STATUS" == "Approved" ]]; then
      echo "  ✅ PE '$NAME': provisioningState=Succeeded, connection=Approved"
    else
      echo "  ❌ PE '$NAME': provisioningState=$PROV, connection=$STATUS (oczekiwano Succeeded/Approved)"
    fi
  done <<< "$PE_LIST"
fi
# Kontrola zbiorcza (podbija licznik FAILURES, jeśli któryś PE jest w złym stanie):
PE_BAD="$(az network private-endpoint list -g "$RG" \
  --query "length([?provisioningState!='Succeeded' || privateLinkServiceConnections[0].privateLinkServiceConnectionState.status!='Approved'])" \
  -o tsv 2>/dev/null || echo 0)"
if [[ "$PE_BAD" != "0" ]]; then
  fail "$PE_BAD Private Endpoint(y/ów) w złym stanie (szczegóły powyżej)"
fi

# --- 5. Tożsamości zarządzane ----------------------------------------------------
section "Tożsamości zarządzane (User-Assigned Managed Identity)"
SENSOR_PRINCIPAL=""
PLAYBOOK_PRINCIPAL=""
for MI in "$ID_SENSOR" "$ID_PLAYBOOK"; do
  PRINCIPAL="$(az identity show -g "$RG" -n "$MI" --query principalId -o tsv 2>/dev/null || true)"
  if [[ -n "$PRINCIPAL" ]]; then
    ok "Tożsamość '$MI' istnieje (principalId: $PRINCIPAL)"
    [[ "$MI" == "$ID_SENSOR" ]]   && SENSOR_PRINCIPAL="$PRINCIPAL"
    [[ "$MI" == "$ID_PLAYBOOK" ]] && PLAYBOOK_PRINCIPAL="$PRINCIPAL"
  else
    fail "Brak tożsamości '$MI'"
  fi
done

# --- 6. Przypisania ról dla tożsamości -------------------------------------------
section "Przypisania ról (RBAC) dla tożsamości zarządzanych"
# Oczekiwane role:
#   sensor   -> Azure Event Hubs Data Sender, Storage Blob Data Contributor,
#               AcrPull, Monitoring Metrics Publisher
#   playbook -> Microsoft Sentinel Responder, Network Contributor (na hg-dev-nsg-dmz)
for PAIR in "sensor:$SENSOR_PRINCIPAL" "playbook:$PLAYBOOK_PRINCIPAL"; do
  LABEL="${PAIR%%:*}"
  PRINCIPAL="${PAIR#*:}"
  if [[ -z "$PRINCIPAL" ]]; then
    fail "Pomijam role tożsamości '$LABEL' — tożsamość nie istnieje (patrz wyżej)"
    continue
  fi
  ROLES="$(az role assignment list --all --assignee "$PRINCIPAL" \
           --query "[].roleDefinitionName" -o tsv 2>/dev/null | sort -u || true)"
  COUNT="$(echo "$ROLES" | grep -c . || true)"
  if [[ "$COUNT" -ge 1 ]]; then
    ok "Tożsamość '$LABEL' ma $COUNT przypisań(-nie/-nia) ról:"
    echo "$ROLES" | sed 's/^/       - /'
  else
    fail "Tożsamość '$LABEL' nie ma ŻADNYCH przypisań ról"
  fi
done
echo
echo "  Pełna tabela ról w grupie (do wzrokowej kontroli zakresów):"
az role assignment list --resource-group "$RG" -o table 2>/dev/null || echo "  (nie udało się pobrać listy ról)"

# --- Podsumowanie -----------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Infrastruktura Tygodnia 1 wygląda dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

# Wskazówka: test idempotencji IaC (nie uruchamiamy automatycznie, bo trwa kilka minut)
echo
echo "Wskazówka — test idempotencji (powinien pokazać brak zmian / NoChange):"
echo "  az deployment sub what-if --location westeurope --name hg-dev-w1 \\"
echo "    --template-file infra/bicep/main.bicep --parameters infra/bicep/main.dev.bicepparam"

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
