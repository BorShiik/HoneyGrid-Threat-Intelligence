#!/usr/bin/env bash
#
# cleanup.sh — sprzątanie po sesji pracy nad HoneyGrid.
#
# Kasuje CAŁĄ grupę zasobów (domyślnie hg-dev-rg), żeby nie przepalać kredytu
# Azure for Students między sesjami. Odtworzenie środowiska = jedno wdrożenie
# z IaC (~10-15 min):
#   az deployment sub create --location westeurope --name hg-dev-w1 \
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
read -r -p "To skasuje całą grupę ${RG}! [y/N] " ANSWER
case "${ANSWER}" in
  [yY]|[yY][eE][sS])
    ;;
  *)
    echo "Anulowano — nic nie skasowano."
    exit 0
    ;;
esac

# --- Kasowanie (asynchroniczne, w tle po stronie Azure) ----------------------------
echo "Kasuję grupę '$RG' (operacja działa w tle, potrwa kilka-kilkanaście minut)..."
az group delete --name "$RG" --yes --no-wait
echo "✅ Zlecono usunięcie grupy '$RG'."

# --- Uwaga o Key Vault (soft-delete) + podpowiedzi -----------------------------------
# Heredoc bez cudzysłowów wokół EOF: ${RG} jest podstawiane, a \$ chroni
# przykładowe podstawienie komend przed wykonaniem.
cat <<EOF

⚠️  Key Vault: po skasowaniu grupy vault przechodzi w stan soft-delete
    i jego nazwa pozostaje ZAJĘTA (domyślnie 90 dni). Przy ponownym
    wdrożeniu pod tą samą nazwą deploy może paść. Żeby zwolnić nazwę:

      az keyvault list-deleted -o table
      az keyvault purge --name <nazwa-vaulta> --location <region-z-list-deleted>

    (purge nie zadziała, jeśli vault miał włączone purge protection —
    wtedy trzeba odczekać okres retencji)

ℹ️  Sprawdzenie, czy kasowanie się zakończyło ("false" = posprzątane):

      az group exists -n ${RG}

    albo pętla czekająca do skutku:

      while [ "\$(az group exists -n ${RG})" = "true" ]; do
        echo "czekam..."; sleep 30
      done; echo "Grupa usunięta."
EOF
