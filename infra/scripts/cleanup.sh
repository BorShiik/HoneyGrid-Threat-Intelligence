#!/usr/bin/env bash
#
# cleanup.sh — sprzątanie po sesji pracy nad HoneyGrid.
#
# Kasuje CAŁĄ grupę zasobów (domyślnie hg-dev-rg) ORAZ czyści zasoby z
# soft-delete (Key Vault + Cognitive Services / Azure OpenAI), żeby nie zostały
# w "koszu" z sekretami/kluczami i nie blokowały nazw przy ponownym wdrożeniu.
# Nie przepalamy kredytu Azure for Students między sesjami.
#
# Odtworzenie środowiska = jedno wdrożenie z IaC (~10-15 min):
#   az deployment sub create --location swedencentral --name hg-dev-w1 \
#     --template-file infra/bicep/main.bicep --parameters infra/bicep/main.dev.bicepparam
#
# Użycie:
#   ./infra/scripts/cleanup.sh            # domyślna grupa: hg-dev-rg
#   ./infra/scripts/cleanup.sh moja-rg    # inna grupa zasobów

set -euo pipefail

RG="${1:-hg-dev-rg}"

# --- Wymagania wstępne ---------------------------------------------------------
if ! command -v az >/dev/null 2>&1; then
  echo "❌ Brak Azure CLI (az). Zainstaluj: brew install azure-cli" >&2
  exit 1
fi
if ! az account show >/dev/null 2>&1; then
  echo "❌ Nie jesteś zalogowany/-a. Uruchom: az login" >&2
  exit 1
fi

# --- Czy jest co kasować ---------------------------------------------------------
if [[ "$(az group exists -n "$RG")" != "true" ]]; then
  echo "ℹ️  Grupa zasobów '$RG' nie istnieje — nie ma czego sprzątać."
  exit 0
fi

# --- Potwierdzenie (domyślnie NIE) ------------------------------------------------
echo "Subskrypcja: $(az account show --query name -o tsv)"
read -r -p "To skasuje całą grupę ${RG} i wyczyści soft-delete (Key Vault + OpenAI)! [y/N] " ANSWER
case "${ANSWER}" in
  [yY]|[yY][eE][sS])
    ;;
  *)
    echo "Anulowano — nic nie skasowano."
    exit 0
    ;;
esac

# --- Inwentaryzacja zasobów z soft-delete PRZED skasowaniem grupy -----------------
# Nazwy mają unikalny sufiks (uniqueString), więc odczytujemy je dynamicznie:
# para "nazwa<TAB>lokalizacja" w wierszu. Po skasowaniu grupy te zasoby trafiają
# do "kosza" i trzeba je dobić osobno (purge) — wtedy `az ... show` już nie działa,
# dlatego zbieramy listę TERAZ.
echo "Inwentaryzuję zasoby z soft-delete w '$RG'..."
KV_ENTRIES="$(az keyvault list -g "$RG" --query "[].[name,location]" -o tsv 2>/dev/null || true)"
COG_ENTRIES="$(az cognitiveservices account list -g "$RG" --query "[].[name,location]" -o tsv 2>/dev/null || true)"

# --- Kasowanie grupy (asynchronicznie, czekamy w pętli) ----------------------------
echo "Kasuję grupę '$RG' (potrwa kilka-kilkanaście minut)..."
az group delete --name "$RG" --yes --no-wait
while [[ "$(az group exists -n "$RG")" == "true" ]]; do
  echo "  …czekam na usunięcie grupy…"
  sleep 30
done
echo "✅ Grupa '$RG' usunięta."

# --- Purge Key Vault (soft-delete) -------------------------------------------------
# Bez purge nazwa vaulta jest ZAJĘTA (domyślnie 90 dni) i ponowny deploy pod tą
# samą nazwą może paść. Purge NIE zadziała przy włączonym purge protection —
# wtedy trzeba odczekać okres retencji (skrypt to zgłosi, ale nie przerwie).
if [[ -n "$KV_ENTRIES" ]]; then
  while IFS=$'\t' read -r name loc; do
    [[ -z "$name" ]] && continue
    echo "🔑 Purge Key Vault: $name ($loc)"
    az keyvault purge --name "$name" --location "$loc" \
      || echo "⚠️  Nie udało się purge Key Vault '$name' (purge protection? sprawdź ręcznie)."
  done <<< "$KV_ENTRIES"
else
  echo "ℹ️  Brak Key Vaultów do purge."
fi

# --- Purge Cognitive Services / Azure OpenAI (soft-delete) -------------------------
if [[ -n "$COG_ENTRIES" ]]; then
  while IFS=$'\t' read -r name loc; do
    [[ -z "$name" ]] && continue
    echo "🧠 Purge Cognitive Services/OpenAI: $name ($loc)"
    az cognitiveservices account purge --location "$loc" --resource-group "$RG" --name "$name" \
      || echo "⚠️  Nie udało się purge '$name' — sprawdź ręcznie."
  done <<< "$COG_ENTRIES"
else
  echo "ℹ️  Brak kont Cognitive Services do purge."
fi

# --- Weryfikacja końcowa -----------------------------------------------------------
echo ""
echo "── Weryfikacja (wszystko poniżej powinno być puste / false) ──"
echo "Grupa istnieje:        $(az group exists -n "$RG")"
echo "Key Vaulty w koszu:    $(az keyvault list-deleted --query "[?resourceGroup=='$RG'].name" -o tsv 2>/dev/null | tr '\n' ' ')"
echo "OpenAI w koszu:        $(az cognitiveservices account list-deleted --query "[?resourceGroup=='$RG'].name" -o tsv 2>/dev/null | tr '\n' ' ')"
echo "✅ Sprzątanie zakończone."
