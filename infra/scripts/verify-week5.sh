#!/usr/bin/env bash
#
# verify-week5.sh — weryfikacja detekcji i wizualizacji (Tydzień 5, Track A).
#
# Sprawdza warstwę detection engineering zbudowaną na tabeli Cowrie_CL:
#   reguły analityczne Sentinela (4 reguły Scheduled 'HoneyGrid — ...'),
#   mapowania encji (entityMappings) per reguła — wejście SOAR (Tydzień 6),
#   Workbook 'HoneyGrid — Pulpit operacyjny SOC' (kategoria sentinel),
#   incydenty wygenerowane przez reguły (money shot — widoczne po ataku),
#   oraz gotowy blok symulacji ataku (mini-Hydra) do wywołania incydentu.
#
# Użycie:
#   ./infra/scripts/verify-week5.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/verify-week5.sh moja-rg    # inna grupa zasobów
#
# Skrypt NICZEGO nie zmienia — wyłącznie odczyty (az ... show/list + az rest GET).
# Każda kontrola wypisuje ✅ (OK) lub ❌ (problem) i idzie dalej. Wszystkie
# wywołania są BEZ ROZSZERZEŃ az (az rest / az resource) — maszyna dev nie
# instaluje rozszerzeń (pip pada). NIE używamy `az sentinel ...`.
#
# WDROŻENIE wykonuje użytkownik (Azure RG bywa kasowane między sesjami) —
# patrz docs/tydzien-5-detekcja.md.

set -euo pipefail

RG="${1:-hg-dev-rg}"

# Nazwy zasobów (konwencja namePrefix=hg, environment=dev w bicep).
PREFIX="hg-dev"
LAW_NAME="${PREFIX}-law"
CA_WEB="${PREFIX}-ca-web"

# Oczekiwana liczba reguł analitycznych HoneyGrid (Tydzień 5).
EXPECTED_RULES=4

# Wersje API płaszczyzny zarządzania.
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

echo "HoneyGrid — weryfikacja Tygodnia 5 (detekcja: reguły analityczne + Workbook + incydenty)"
echo "Grupa zasobów: $RG"
echo "Subskrypcja:   $(az account show --query name -o tsv)"

# --- 0. Czy grupa zasobów istnieje -------------------------------------------
section "Grupa zasobów"
if [[ "$(az group exists -n "$RG")" == "true" ]]; then
  ok "Grupa zasobów '$RG' istnieje"
else
  fail "Grupa zasobów '$RG' NIE istnieje"
  echo
  echo "Wynik: bez grupy zasobów dalsze kontrole nie mają sensu."
  info "Wdrożenie wykonuje użytkownik — patrz docs/tydzien-5-detekcja.md (jedno polecenie"
  echo "      az deployment sub create ...). Po wdrożeniu uruchom ten skrypt ponownie."
  exit 1
fi

# --- Wspólny krok: resource ID workspace'a Log Analytics ----------------------
# Reguły analityczne i incydenty wiszą POD workspace'em (provider
# Microsoft.SecurityInsights na zakresie workspace'a) — potrzebny jego pełny ID.
section "Workspace Log Analytics ($LAW_NAME)"
LAW_ID="$(az resource show -g "$RG" -n "$LAW_NAME" \
  --resource-type Microsoft.OperationalInsights/workspaces \
  --query id -o tsv 2>/dev/null || true)"
if [[ -z "$LAW_ID" ]]; then
  fail "Workspace '$LAW_NAME' nie istnieje (resource not found) — sprawdź modules/sentinel.bicep"
  echo
  echo "Wynik: bez workspace'a nie ma jak odpytać reguł ani incydentów. Koniec."
  exit 1
else
  ok "Workspace '$LAW_NAME' istnieje"
  info "ID: $LAW_ID"
fi

# --- 1. Reguły analityczne Sentinela -----------------------------------------
# az rest GET na provider SecurityInsights w zakresie workspace'a (bez rozszerzeń).
section "Reguły analityczne (Microsoft.SecurityInsights/alertRules)"
RULES_JSON="$(az rest --method get \
  --url "https://management.azure.com${LAW_ID}/providers/Microsoft.SecurityInsights/alertRules?api-version=${API_SECURITYINSIGHTS}" \
  -o json 2>/dev/null || true)"

if [[ -z "$RULES_JSON" ]]; then
  fail "Nie udało się pobrać reguł analitycznych (az rest GET alertRules) — sprawdź uprawnienia/onboarding Sentinela"
else
  # Parsujemy w jednym przebiegu Pythonem: liczba reguł HoneyGrid (Scheduled),
  # lista displayName|severity|enabled, oraz reguły z wyłączonym 'enabled'.
  HG_COUNT="$(echo "$RULES_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
rules = d.get("value", [])
hg = [r for r in rules
      if r.get("kind") == "Scheduled"
      and (r.get("properties", {}).get("displayName", "")).startswith("HoneyGrid")]
print(len(hg))
' 2>/dev/null || echo "0")"

  if [[ "$HG_COUNT" -ge "$EXPECTED_RULES" ]]; then
    ok "Reguły 'HoneyGrid' (Scheduled): $HG_COUNT (oczekiwano >= $EXPECTED_RULES)"
  else
    fail "Reguł 'HoneyGrid' (Scheduled): $HG_COUNT — oczekiwano >= $EXPECTED_RULES (sprawdź modules/sentinel.bicep)"
  fi

  echo "  Lista reguł (displayName | severity | enabled):"
  echo "$RULES_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
for r in d.get("value", []):
    if r.get("kind") != "Scheduled":
        continue
    p = r.get("properties", {})
    name = p.get("displayName", "")
    if not name.startswith("HoneyGrid"):
        continue
    print("    - %s | %s | enabled=%s" % (name, p.get("severity", "?"), p.get("enabled")))
' 2>/dev/null || true

  # Żadna reguła HoneyGrid nie może być wyłączona (enabled=false => brak alertów).
  DISABLED="$(echo "$RULES_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
bad = [r["properties"]["displayName"] for r in d.get("value", [])
       if r.get("kind") == "Scheduled"
       and (r.get("properties", {}).get("displayName", "")).startswith("HoneyGrid")
       and r.get("properties", {}).get("enabled") is False]
print("\n".join(bad))
' 2>/dev/null || true)"
  if [[ -n "$DISABLED" ]]; then
    fail "Wyłączone reguły HoneyGrid (enabled=false) — nie wygenerują alertów:"
    echo "$DISABLED" | while IFS= read -r line; do [[ -n "$line" ]] && echo "      - $line"; done
  else
    ok "Wszystkie reguły HoneyGrid są włączone (enabled=true)"
  fi
fi

# --- 2. Mapowania encji per reguła (entityMappings) ---------------------------
# entityMappings = wejście korelacji i SOAR (Tydzień 6): bez encji incydent
# nie ma czego blokować (IP) ani kogo wiązać (Account/Host).
section "Mapowania encji (entityMappings) — wejście SOAR Tygodnia 6"
if [[ -z "${RULES_JSON:-}" ]]; then
  fail "Brak danych reguł — pomijam kontrolę encji"
else
  echo "$RULES_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
hg = [r for r in d.get("value", [])
      if r.get("kind") == "Scheduled"
      and (r.get("properties", {}).get("displayName", "")).startswith("HoneyGrid")]
missing = 0
for r in hg:
    p = r.get("properties", {})
    name = p.get("displayName", "")
    ems = p.get("entityMappings") or []
    if ems:
        types = ",".join(e.get("entityType", "?") for e in ems)
        print("  ✅ %s -> encje: %s" % (name, types))
    else:
        print("  ❌ %s -> BRAK entityMappings (korelacja/SOAR nie zadziała)" % name)
        missing += 1
sys.exit(1 if missing else 0)
' 2>/dev/null && ok "Każda reguła HoneyGrid ma niepuste entityMappings" \
                || fail "Co najmniej jedna reguła HoneyGrid bez entityMappings — patrz lista wyżej"
fi

# --- 3. Workbook — pulpit operacyjny SOC --------------------------------------
section "Workbook (Microsoft.Insights/workbooks)"
WB_JSON="$(az resource list -g "$RG" \
  --resource-type Microsoft.Insights/workbooks -o json 2>/dev/null || true)"
WB_MATCH="$(echo "$WB_JSON" | python3 -c '
import sys, json
try:
    items = json.load(sys.stdin)
except Exception:
    items = []
hits = []
for w in items:
    p = w.get("properties", {}) or {}
    dn = p.get("displayName", "")
    cat = p.get("category", "")
    if "HoneyGrid" in dn or cat == "sentinel":
        hits.append("%s | category=%s" % (dn, cat))
print("\n".join(hits))
' 2>/dev/null || true)"
if [[ -n "$WB_MATCH" ]]; then
  ok "Workbook HoneyGrid znaleziony:"
  echo "$WB_MATCH" | while IFS= read -r line; do [[ -n "$line" ]] && echo "      - $line"; done
  info "Portal: Microsoft Sentinel -> Workbooks -> My workbooks -> 'HoneyGrid — Pulpit operacyjny SOC'"
else
  fail "Brak Workbooka 'HoneyGrid' (kategoria sentinel) w RG — sprawdź modules/workbook.bicep i wpięcie w main.bicep"
fi

# --- 4. Incydenty (MONEY SHOT — widoczne po ataku) ----------------------------
section "Incydenty Sentinela (Microsoft.SecurityInsights/incidents)"
INC_JSON="$(az rest --method get \
  --url "https://management.azure.com${LAW_ID}/providers/Microsoft.SecurityInsights/incidents?api-version=${API_SECURITYINSIGHTS}" \
  -o json 2>/dev/null || true)"
if [[ -z "$INC_JSON" ]]; then
  fail "Nie udało się pobrać incydentów (az rest GET incidents) — sprawdź uprawnienia/onboarding Sentinela"
else
  INC_COUNT="$(echo "$INC_JSON" | python3 -c 'import sys,json; print(len(json.load(sys.stdin).get("value", [])))' 2>/dev/null || echo "0")"
  if [[ "$INC_COUNT" -gt 0 ]]; then
    ok "Liczba incydentów: $INC_COUNT 🎉"
    echo "  Najświeższe tytuły:"
    echo "$INC_JSON" | python3 -c '
import sys, json
d = json.load(sys.stdin)
vals = d.get("value", [])
def created(i):
    return i.get("properties", {}).get("createdTimeUtc", "")
for inc in sorted(vals, key=created, reverse=True)[:5]:
    p = inc.get("properties", {})
    print("    - [%s/%s] %s" % (p.get("severity", "?"), p.get("status", "?"), p.get("title", "")))
' 2>/dev/null || true
  else
    info "Brak incydentów (na razie). To NORMALNE bez ruchu na sensorach."
    info "Incydenty pojawiają się do ~10 min po przekroczeniu progu reguły"
    echo "      (okno reguły co 5 min + opóźnienie ingestii 2-15 min). Wygeneruj atak (sekcja niżej)."
  fi
fi

# --- 5. Symulacja ataku (mini-Hydra) — wywołaj incydent brute-force -----------
# Reguła 'brute-force single IP' (Medium, T1110) odpala się po >15 zdarzeniach
# login.failed z jednego IP. Generujemy >15 w pętli na web-sensorze.
section "Symulacja ataku (copy-paste) — wywołanie incydentu brute-force"
WEB_FQDN="$(az containerapp show -g "$RG" -n "$CA_WEB" \
  --query 'properties.configuration.ingress.fqdn' -o tsv 2>/dev/null || true)"
if [[ -z "$WEB_FQDN" ]]; then
  info "Nie udało się odczytać FQDN web-sensora ('$CA_WEB') — podstaw własny adres ręcznie."
  WEB_FQDN="<FQDN-web>"
else
  ok "Web-sensor FQDN: $WEB_FQDN"
fi
echo
echo "  Wklej do terminala (generuje 20 zdarzeń login.failed z jednego IP):"
echo
echo "    for i in \$(seq 1 20); do"
echo "      curl -s -X POST \"https://${WEB_FQDN}/wp-login.php\" -d \"log=admin&pwd=pass\$i\" -o /dev/null"
echo "    done"
echo
info "Po ~5-10 min sprawdź incydent: ten skrypt ponownie, albo portal"
echo "      (Microsoft Sentinel -> Incidents). Progi strojone po realnym ruchu — patrz docs."

# --- Podsumowanie ---------------------------------------------------------------
section "Podsumowanie"
if [[ "$FAILURES" -eq 0 ]]; then
  echo "✅ Wszystkie kontrole zaliczone. Warstwa detekcji Tygodnia 5 wygląda dobrze."
else
  echo "❌ Liczba nieudanych kontroli: $FAILURES — przejrzyj komunikaty powyżej."
fi

exit "$([[ "$FAILURES" -eq 0 ]] && echo 0 || echo 1)"
