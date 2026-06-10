# Tydzień 1 — Wdrożenie infrastruktury HoneyGrid na Azure

Ten przewodnik prowadzi krok po kroku przez pierwsze wdrożenie infrastruktury projektu HoneyGrid
na subskrypcji **Azure for Students** ($100 kredytu). Po jego wykonaniu w Azure będzie istniała
kompletna grupa zasobów `hg-dev-rg` ze wszystkimi fundamentami: siecią, Sentinel-em, bazami danych,
messagingiem, tożsamościami zarządzanymi i Private Endpointami.

Wszystkie komendy wykonujemy z **katalogu głównego repozytorium** (`HoneyGrid-Threat-Intelligence/`),
chyba że napisano inaczej.

---

## Spis treści

1. [Wymagania wstępne](#1-wymagania-wstępne)
2. [Logowanie i subskrypcja](#2-logowanie-i-subskrypcja)
3. [Rejestracja resource providerów](#3-rejestracja-resource-providerów)
4. [What-if — sucha próba](#4-what-if--sucha-próba)
5. [Wdrożenie](#5-wdrożenie)
6. [Odczyt outputów wdrożenia](#6-odczyt-outputów-wdrożenia)
7. [Weryfikacja w portalu Azure](#7-weryfikacja-w-portalu-azure-krok-po-kroku)
8. [Weryfikacja przez CLI](#8-weryfikacja-przez-cli)
9. [Połączenie projektu lokalnego z Azure](#9-połączenie-projektu-lokalnego-z-azure)
10. [Koszty i sprzątanie](#10-koszty-i-sprzątanie)
11. [Rozwiązywanie problemów](#11-rozwiązywanie-problemów)

---

## 1. Wymagania wstępne

### 1.1. Konto Azure for Students

1. Wejdź na <https://azure.microsoft.com/free/students> i kliknij **Start free / Rozpocznij bezpłatnie**.
2. Zaloguj się kontem uczelnianym (adres e-mail w domenie uczelni). Weryfikacja statusu studenta
   odbywa się automatycznie przez domenę — **karta płatnicza NIE jest wymagana**.
3. Po aktywacji dostajesz **$100 kredytu na 12 miesięcy** + zestaw usług darmowych.
4. Sprawdź stan kredytu w portalu: <https://portal.azure.com> → wyszukaj **Cost Management** →
   **Cost analysis** (lub wejdź na <https://www.microsoftazuresponsorships.com> dla salda kredytu studenckiego).

> **Uwaga zespołowa:** subskrypcja studencka jest osobista. W zespole 2-osobowym wdrażamy na
> subskrypcji jednej osoby, a drugą osobę dodajemy jako **Contributor** do grupy zasobów
> (Portal → `hg-dev-rg` → Access control (IAM) → Add role assignment).

### 1.2. Instalacja narzędzi (macOS)

```bash
# Azure CLI przez Homebrew
brew install azure-cli

# Wbudowany kompilator Bicep (instaluje/aktualizuje binarkę bicep dla az)
az bicep install
```

Weryfikacja wersji:

```bash
az version
az bicep version
```

**Oczekiwany wynik:** `az version` wypisuje JSON z kluczami `azure-cli` (np. `2.6x.x` lub nowsza)
oraz `extensions`. `az bicep version` wypisuje np. `Bicep CLI version 0.3x.x`.
Jeśli Bicep jest stary, zaktualizuj: `az bicep upgrade`.

---

## 2. Logowanie i subskrypcja

### 2.1. Logowanie

```bash
az login
```

**Co się stanie:** otworzy się domyślna przeglądarka na stronie logowania Microsoft. Zaloguj się
kontem uczelnianym (tym, na którym aktywowano Azure for Students). Po zalogowaniu przeglądarka
pokaże komunikat o powodzeniu, a terminal wypisze listę dostępnych subskrypcji (nowsze wersje CLI
mogą poprosić o wybór subskrypcji bezpośrednio w terminalu — wybierz **Azure for Students**).

> Jeśli przeglądarka się nie otwiera (np. SSH): `az login --use-device-code` i przepisz kod na
> <https://microsoft.com/devicelogin>.

### 2.2. Wybór subskrypcji

```bash
az account list -o table
```

**Oczekiwany wynik:** tabela z co najmniej jednym wierszem, np.:

```text
Name                CloudName    SubscriptionId                        TenantId      State    IsDefault
------------------  -----------  ------------------------------------  ------------  -------  -----------
Azure for Students  AzureCloud   xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx  ...           Enabled  True
```

Jeśli `Azure for Students` nie jest domyślna (`IsDefault: False`):

```bash
az account set --subscription "Azure for Students"
```

Kontrola, na czym pracujemy:

```bash
az account show -o table
```

**Oczekiwany wynik:** `Name` = `Azure for Students`, `State` = `Enabled`. Od tej pory wszystkie
komendy `az` lecą na tę subskrypcję.

---

## 3. Rejestracja resource providerów

Świeża subskrypcja studencka ma większość providerów **niezarejestrowanych**. Wdrożenie Bicep
wywali się błędem `MissingSubscriptionRegistration`, jeśli tego nie zrobimy zawczasu.

Rejestrujemy wszystkie providery używane przez HoneyGrid jedną pętlą:

```bash
for ns in \
  Microsoft.Network \
  Microsoft.OperationalInsights \
  Microsoft.OperationsManagement \
  Microsoft.SecurityInsights \
  Microsoft.Insights \
  Microsoft.DocumentDB \
  Microsoft.Storage \
  Microsoft.EventHub \
  Microsoft.ServiceBus \
  Microsoft.App \
  Microsoft.ContainerRegistry \
  Microsoft.Web \
  Microsoft.CognitiveServices \
  Microsoft.Maps \
  Microsoft.KeyVault \
  Microsoft.ManagedIdentity \
  Microsoft.Communication
do
  echo "Rejestruję: $ns"
  az provider register --namespace "$ns"
done
```

Komenda `az provider register` jest **asynchroniczna** — wraca natychmiast, a rejestracja w tle
może potrwać **kilka minut** (zwykle 1–5 min na provider, lecą równolegle).

Sprawdzenie statusu pojedynczego providera:

```bash
az provider show -n Microsoft.App --query registrationState -o tsv
```

**Oczekiwany wynik:** `Registered`. Dopóki widzisz `Registering` — czekaj i sprawdzaj ponownie.

Sprawdzenie wszystkich naraz:

```bash
for ns in Microsoft.Network Microsoft.OperationalInsights Microsoft.OperationsManagement \
  Microsoft.SecurityInsights Microsoft.Insights Microsoft.DocumentDB Microsoft.Storage \
  Microsoft.EventHub Microsoft.ServiceBus Microsoft.App Microsoft.ContainerRegistry \
  Microsoft.Web Microsoft.CognitiveServices Microsoft.Maps Microsoft.KeyVault \
  Microsoft.ManagedIdentity Microsoft.Communication
do
  printf "%-35s %s\n" "$ns" "$(az provider show -n $ns --query registrationState -o tsv)"
done
```

Nie idź dalej, dopóki wszystkie nie pokażą `Registered`.

---

## 4. What-if — sucha próba

Zanim cokolwiek powstanie w Azure, robimy **what-if** — symulację wdrożenia, która pokazuje, co
*byłoby* utworzone/zmienione/skasowane, ale **niczego nie tworzy** i nic nie kosztuje.

Wdrożenie jest na poziomie **subskrypcji** (template sam tworzy grupę zasobów `hg-dev-rg`):

```bash
az deployment sub what-if \
  --location westeurope \
  --name hg-dev-w1 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```

**Jak czytać wynik** — każdy zasób ma prefiks zmiany:

| Symbol | Znaczenie |
|---|---|
| `+ Create` (zielony) | zasób zostanie **utworzony** — przy pierwszym wdrożeniu wszystko powinno być tutaj |
| `~ Modify` (fioletowy) | zasób istnieje i zostanie **zmieniony** (przy pierwszym wdrożeniu nie powinno wystąpić) |
| `- Delete` (pomarańczowy) | zasób zostanie **usunięty** — przy naszym trybie wdrożenia (incremental) nie powinno się pojawić; jeśli się pojawi, ZATRZYMAJ SIĘ i sprawdź template |
| `* Ignore` / `= NoChange` | bez zmian / poza zakresem analizy |

Przy pierwszym uruchomieniu oczekuj **kilkudziesięciu pozycji `+ Create`**: grupa zasobów, VNet,
3 NSG, Log Analytics, Sentinel, Cosmos DB, Storage, Event Hubs, Service Bus, Container Apps Env,
ACR, Static Web App, Key Vault, 2 tożsamości zarządzane, Private DNS zones, Private Endpoints,
przypisania ról itd.

> What-if czasem wypisuje ostrzeżenie o "nested deployment" lub niepełnej analizie modułów — to
> normalne i niegroźne. Kluczowe: brak `Delete` i brak błędów walidacji.

---

## 5. Wdrożenie

Ta sama komenda, tylko `create` zamiast `what-if`:

```bash
az deployment sub create \
  --location westeurope \
  --name hg-dev-w1 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```

**Typowy czas: ~10–15 minut.** Najdłużej trwają: Cosmos DB (5–10 min), Container Apps Environment
(kilka minut) i Private Endpointy. Terminal "wisi" na `Running ...` — to normalne.

### 5.1. Śledzenie postępu w portalu

1. Wejdź na <https://portal.azure.com>.
2. Wyszukaj **Subscriptions** → kliknij **Azure for Students**.
3. W menu po lewej: **Settings → Deployments**.
4. Kliknij wdrożenie **hg-dev-w1** — zobaczysz listę zasobów i ich statusy
   (`Created` / `Running` / `Failed`) odświeżaną na żywo. Każdy moduł Bicep (sieć, dane,
   messaging, ai...) widoczny jest jako osobne pod-wdrożenie.

Po sukcesie terminal wypisze duży JSON z `"provisioningState": "Succeeded"`.

### 5.2. Co zrobić, gdy moduł `ai` się wywali

**Azure OpenAI bywa niedostępne na subskrypcjach studenckich** (wymaga osobnej zgody / quota =
0 w `westeurope`). Typowe błędy: `SpecialFeatureOrQuotaIdRequired`, `InsufficientQuota`,
`The subscription is not allowed to create the resource` przy zasobie typu
`Microsoft.CognitiveServices/accounts` z kind `OpenAI`.

**Workaround (nie blokuje Track A — honeypot + telemetria + Sentinel działają bez AI):**

1. Otwórz `infra/bicep/main.bicep`.
2. Znajdź wywołanie `module ai ...` i **zakomentuj cały blok** (`//` na początku każdej linii
   lub `/* ... */` wokół bloku). Zakomentuj też ewentualne outputy odwołujące się do tego modułu.
3. Ponów wdrożenie tą samą komendą `az deployment sub create ...` — wdrożenie jest
   **idempotentne**: istniejące zasoby zostaną pominięte/zaktualizowane, brakujące dotworzone.

Klasyfikację AI (Tydzień 5) podłączymy później — np. po uzyskaniu dostępu do OpenAI w innym
regionie albo przez GitHub Models jako fallback.

---

## 6. Odczyt outputów wdrożenia

Template zwraca outputy (nazwy i identyfikatory zasobów), których będziemy używać w kolejnych
tygodniach. Odczyt:

```bash
az deployment sub show -n hg-dev-w1 --query properties.outputs -o json
```

**Oczekiwany wynik:** JSON w stylu `{ "eventHubNamespaceName": { "type": "String", "value": "hg-dev-ehns-..." }, ... }`.

Najważniejsze outputy i ich przeznaczenie:

| Output | Co to jest | Gdzie użyjemy |
|---|---|---|
| `eventHubNamespaceName` | nazwa namespace'u Event Hubs (FQDN: `<nazwa>.servicebus.windows.net`) | Tydzień 2 — producer telemetrii w sensorze (Cowrie → Event Hub `honeypot-events`) |
| `sensorIdentityClientId` | clientId tożsamości `hg-dev-id-sensor` | Tydzień 2 — konfiguracja Container Apps (zmienna `AZURE_CLIENT_ID` dla `DefaultAzureCredential`) |
| `playbookIdentityClientId` | clientId tożsamości `hg-dev-id-playbook` | Tydzień 4 — playbook SOAR (blokowanie IP na NSG) |
| `cosmosAccountName` / endpoint | konto Cosmos DB serverless | Tydzień 3 — procesor zapisujący ataki |
| `storageAccountName` | konto Storage (blob raw/tty/downloads + Azure Files `cowrie`) | Tydzień 2 — wolumen plikowy Cowrie; Tydzień 3 — surowe logi |
| `acrLoginServer` | adres rejestru kontenerów | Tydzień 2 — `docker push` obrazu sensora |
| `logAnalyticsWorkspaceId` | workspace Log Analytics (z Sentinel-em) | Tydzień 4 — reguły analityczne, DCR |
| `keyVaultName` | Key Vault (RBAC) | sekrety, których nie da się uniknąć (np. klucz Maps, jeśli potrzebny) |
| `serviceBusNamespaceName` | namespace Service Bus (kolejka `ai-classify`) | Tydzień 5 — kolejka do klasyfikacji AI |
| `staticWebAppName` / URL | Static Web App | Tydzień 6 — deploy dashboardu |

> Dokładny zestaw outputów zależy od finalnej wersji `main.bicep` — komenda powyżej zawsze pokaże
> aktualną listę. Zapisz sobie wynik do pliku roboczego (NIE commituj):
> `az deployment sub show -n hg-dev-w1 --query properties.outputs -o json > /tmp/hg-outputs.json`

---

## 7. Weryfikacja w portalu Azure (krok po kroku)

### 7.1. Grupa zasobów i lista zasobów

1. Portal → wyszukaj **Resource groups** → kliknij **hg-dev-rg**.
2. Na liście powinny być m.in.: `hg-dev-vnet`, 3 NSG (`hg-dev-nsg-dmz`, `hg-dev-nsg-logic`,
   `hg-dev-nsg-data`), workspace Log Analytics, konto Cosmos DB, konto Storage, namespace
   Event Hubs, namespace Service Bus, Container Apps Environment, Container Registry,
   Static Web App, Application Insights, Key Vault, Azure Maps, 2 tożsamości
   (Managed Identity), strefy Private DNS (`privatelink....`), Private Endpointy
   (+ ich interfejsy sieciowe), DCE oraz — jeśli moduł `ai` nie był zakomentowany — zasób
   Azure OpenAI.

### 7.2. Sieć: VNet i podsieci

1. Kliknij **hg-dev-vnet** → **Settings → Subnets**.
2. Powinny być 3 podsieci:
   - `snet-dmz` — `10.20.0.0/24` (NSG: `hg-dev-nsg-dmz`)
   - `snet-logic` — `10.20.1.0/24` (NSG: `hg-dev-nsg-logic`)
   - `snet-data` — `10.20.2.0/24` (NSG: `hg-dev-nsg-data`)
3. Address space całości: `10.20.0.0/16` (zakładka **Address space**).

### 7.3. NSG strefy DMZ — reguły anti-pivot (najważniejsza weryfikacja bezpieczeństwa)

1. Wróć do `hg-dev-rg` → kliknij **hg-dev-nsg-dmz**.
2. **Settings → Inbound security rules** — powinny być reguły **Allow** z Internetu na porty
   honeypota: **22, 23, 80, 443, 3389, 2222** (to są porty-przynęty — chcemy, żeby atakujący
   się łączyli).
3. **Settings → Outbound security rules** — tu siedzi mechanizm **anti-pivot**:
   - reguły **Allow** wychodzące TYLKO do service tagów **EventHub** i **AzureMonitor**
     (telemetria sensora),
   - reguła **Deny-Outbound-All** z priorytetem **4000**, kierunek Outbound, akcja **Deny**,
     destination `*` — blokuje wszystko inne. Dzięki temu nawet po przejęciu honeypota atakujący
     nie wyjdzie z niego dalej (ani do internetu, ani w głąb naszej sieci).

### 7.4. Private Endpoints

1. W `hg-dev-rg` znajdź zasoby typu **Private endpoint** (3 sztuki: Cosmos, Blob, Key Vault) —
   wszystkie w podsieci `snet-data`.
2. Kliknij każdy → na stronie Overview **Connection status** musi być **Approved**, a
   **Provisioning state** = **Succeeded**.
3. Strefy DNS: `privatelink.documents.azure.com`, `privatelink.blob.core.windows.net`,
   `privatelink.vaultcore.azure.net` — każda podlinkowana do `hg-dev-vnet`
   (strefa → **Virtual network links**).

### 7.5. Tożsamości zarządzane

1. W `hg-dev-rg` powinny być dwa zasoby typu **Managed Identity**:
   `hg-dev-id-sensor` i `hg-dev-id-playbook`.
2. Kliknij każdy → Overview pokazuje **Client ID** i **Principal ID** (przydadzą się w Tygodniu 2).

### 7.6. RBAC na Event Hubs

1. Otwórz namespace **Event Hubs** → **Access control (IAM)** → zakładka **Role assignments**.
2. Powinno być widać: **hg-dev-id-sensor** z rolą **Azure Event Hubs Data Sender** w zakresie
   tego namespace'u.
3. Analogicznie sprawdź (opcjonalnie): na koncie Storage — sensor jako **Storage Blob Data
   Contributor**; na ACR — sensor jako **AcrPull**; na grupie zasobów — sensor jako
   **Monitoring Metrics Publisher** (w Tygodniu 4 zawęzimy do DCR) oraz playbook jako
   **Microsoft Sentinel Responder**; na `hg-dev-nsg-dmz` — playbook jako **Network Contributor**.

### 7.7. Sentinel

1. Portal → wyszukaj **Microsoft Sentinel**.
2. Na liście powinien być nasz workspace Log Analytics — to znaczy, że onboarding Sentinela
   się udał. Kliknij go → otworzy się pulpit Sentinela (na razie pusty — incydenty pojawią się
   od Tygodnia 4).

---

## 8. Weryfikacja przez CLI

Najszybciej — gotowy skrypt (sprawdza wszystko z sekcji 7 automatycznie):

```bash
./infra/scripts/verify-week1.sh           # domyślnie hg-dev-rg
./infra/scripts/verify-week1.sh inna-rg   # albo z inną grupą
```

Skrypt wypisuje ✅/❌ dla każdej kontroli. Pojedyncze komendy do ręcznego sprawdzenia:

```bash
# Reguły NSG strefy DMZ (inbound + outbound, w tym Deny-Outbound-All prio 4000)
az network nsg rule list -g hg-dev-rg --nsg-name hg-dev-nsg-dmz -o table

# Wszystkie przypisania ról w grupie zasobów
az role assignment list --resource-group hg-dev-rg -o table

# Private Endpointy i ich stan
az network private-endpoint list -g hg-dev-rg -o table
```

### 8.1. Test idempotencji

Infrastruktura jako kod ma być **idempotentna** — ponowny what-if po udanym wdrożeniu nie powinien
pokazywać żadnych zmian:

```bash
az deployment sub what-if \
  --location westeurope \
  --name hg-dev-w1 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```

**Oczekiwany wynik:** brak pozycji `Create`/`Modify`/`Delete` — komunikat w stylu
`Resource changes: X no change.` (pozycje `NoChange`/`Ignore` są OK). Drobne `Modify` na
właściwościach typu tagi/wersje API bywają fałszywymi alarmami what-if — ale `Create` lub `Delete`
oznacza realny dryf i wymaga wyjaśnienia.

---

## 9. Połączenie projektu lokalnego z Azure

### 9.1. Zakres Tygodnia 1: ŻADNEGO połączenia nie potrzeba

Lokalna aplikacja .NET i frontend **w Tygodniu 1 nie łączą się z Azure w ogóle** — frontend działa
na mockach MSW, backend na fixtures. Infrastruktura stoi i czeka.

### 9.2. Jak to będzie działać dalej: architektura bezkluczowa

W HoneyGrid **nie używamy connection stringów ani kluczy w `appsettings.json`**. Uwierzytelnianie
jest tożsamościowe, przez `DefaultAzureCredential` z pakietu `Azure.Identity`:

- **Lokalnie (dev):** `DefaultAzureCredential` automatycznie podbiera poświadczenia z `az login` —
  czyli Twoje konto studenckie. Zero konfiguracji sekretów.
- **W chmurze (Container Apps):** ta sama linijka kodu użyje **Managed Identity**
  (`hg-dev-id-sensor`), bo kontener dostanie `AZURE_CLIENT_ID` tej tożsamości.

Ten sam kod, zero sekretów, zero rotacji kluczy.

### 9.3. Co trafi do appsettings w Tygodniu 2

Tylko **NAZWY** zasobów (publiczne, niesekretne), np. fully qualified namespace Event Hubs:

```json
{
  "EventHubs": {
    "FullyQualifiedNamespace": "hg-dev-ehns-XXXX.servicebus.windows.net",
    "HubName": "honeypot-events"
  }
}
```

### 9.4. Role data-plane dla własnego konta (do testów lokalnych w Tygodniu 2)

Role RBAC nadane przez Bicep dotyczą tożsamości zarządzanych, **nie Twojego konta**. Żeby lokalnie
wysłać/odebrać event, musisz nadać sobie rolę data-plane na namespace Event Hubs:

```bash
# ID namespace'u Event Hubs (podstaw nazwę z outputów wdrożenia)
EHNS_ID=$(az eventhubs namespace show -g hg-dev-rg -n <eventHubNamespaceName> --query id -o tsv)

# Nadaj zalogowanemu kontu pełną rolę data-plane na Event Hubs
az role assignment create \
  --assignee $(az ad signed-in-user show --query id -o tsv) \
  --role "Azure Event Hubs Data Owner" \
  --scope "$EHNS_ID"
```

Analogicznie w razie potrzeby: `Cosmos DB Built-in Data Contributor` (przez
`az cosmosdb sql role assignment create`), `Storage Blob Data Contributor` na koncie Storage.
Propagacja RBAC trwa do ~5 minut — jeśli dostajesz 401/403 zaraz po nadaniu roli, odczekaj chwilę.

---

## 10. Koszty i sprzątanie

### 10.1. Ile to kosztuje

| Pozycja | Koszt | Uwagi |
|---|---|---|
| **Event Hubs Basic** | **~$11/mies.** | jedyny stały koszt always-on (opłata za throughput unit) |
| **Private Endpoint** | **~$7/szt./mies.** | mamy 3 (Cosmos, Blob, KV) ≈ ~$21/mies. + grosze za GB |
| Cosmos DB serverless | ~$0 | płatność za RU — przy braku ruchu pomijalne |
| Storage / Service Bus Basic / Log Analytics | ~$0 | groszowe przy naszych wolumenach; Service Bus Basic płaci za operacje |
| Container Apps / Static Web App / ACR Basic | ~$0 / free tier | Container Apps scale-to-zero; ACR Basic ~$0.17/dzień gdy istnieje |
| **Sentinel** | $0 przez **31 dni** (free trial) | potem free tier **5 GB/dzień** ingestu — nasz wolumen się mieści |
| Azure OpenAI / Maps | ~$0 | pay-per-use, w Tygodniu 1 zero wywołań |

> **Świadomy kompromis:** Event Hubs Basic i Service Bus Basic **nie wspierają Private
> Endpointów** (to feature tieru Premium, poza budżetem studenckim). Telemetria z sensora idzie
> więc do Event Hubs przez publiczny endpoint — ale ruch jest po **TLS**, a NSG DMZ wypuszcza
> go wyłącznie na service tag **EventHub**. Zapisz to w dokumentacji projektu jako decyzję
> architektoniczną (koszt vs. izolacja).

### 10.2. Złoty nawyk: kasuj po sesji, odtwarzaj z IaC

Cała infrastruktura jest kodem — odtworzenie to ~15 minut. Żeby nie przepalać kredytu, gdy nie
pracujesz nad projektem:

```bash
# Po zakończeniu sesji pracy — kasuje WSZYSTKO w hg-dev-rg (działa w tle)
az group delete --name hg-dev-rg --yes --no-wait
```

Albo wygodniej, naszym skryptem (z potwierdzeniem):

```bash
./infra/scripts/cleanup.sh
```

Sprawdzenie, czy grupa jeszcze istnieje (kasowanie trwa kilka–kilkanaście minut):

```bash
az group exists -n hg-dev-rg
# "true"  -> jeszcze się kasuje / istnieje
# "false" -> posprzątane
```

Ponowne postawienie środowiska = sekcja 5 (jedna komenda `az deployment sub create`, ~10–15 min).

> **Pułapka Key Vault:** po skasowaniu grupy Key Vault wpada w **soft-delete** na 90 dni i jego
> nazwa pozostaje zajęta — patrz sekcja 11.4 zanim zrobisz redeploy.

---

## 11. Rozwiązywanie problemów

### 11.1. `MissingSubscriptionRegistration` / "The subscription is not registered to use namespace ..."

Pominięta sekcja 3. Zarejestruj wskazany w błędzie provider:

```bash
az provider register --namespace Microsoft.XYZ
az provider show -n Microsoft.XYZ --query registrationState -o tsv   # czekaj na "Registered"
```

i ponów wdrożenie.

### 11.2. Quota / brak dostępu do Azure OpenAI

Błędy typu `InsufficientQuota`, `SpecialFeatureOrQuotaIdRequired`, odmowa utworzenia konta
`OpenAI` — subskrypcje studenckie często nie mają dostępu do Azure OpenAI w `westeurope`.
**Rozwiązanie:** zakomentuj `module ai` w `infra/bicep/main.bicep` i ponów wdrożenie
(szczegóły: sekcja 5.2). Nie blokuje to Track A.

### 11.3. "Name already taken" (Cosmos / Key Vault / Storage / ACR)

Nazwy tych zasobów są **globalnie unikalne** w całym Azure. Jeśli ktoś na świecie (albo Ty — w
poprzedniej, skasowanej próbie) zajął nazwę, wdrożenie padnie. Template zwykle dokleja unikalny
sufiks (`uniqueString`), więc to rzadkie — ale jeśli wystąpi:

- sprawdź, czy to nie Twój własny zasób w soft-delete (KV — patrz 11.4),
- w ostateczności zmień `namePrefix` w `infra/bicep/main.dev.bicepparam` (np. `hg2`) — uwaga,
  zmieni to nazwy WSZYSTKICH zasobów.

### 11.4. Key Vault: `ConflictError` / "Vault name is already in use" po skasowaniu RG

Key Vault ma **soft-delete** (domyślnie 90 dni): skasowany vault "trzyma" swoją nazwę. Przy
ponownym wdrożeniu po `az group delete` template nie może utworzyć vaulta o tej samej nazwie.

```bash
# Lista vaultów w stanie soft-delete
az keyvault list-deleted -o table

# Trwałe usunięcie (purge) — zwalnia nazwę
az keyvault purge --name <nazwa-vaulta> --location westeurope
```

> Jeśli purge jest zablokowany, vault miał włączone **purge protection** — wtedy trzeba odczekać
> okres retencji albo (dla środowiska dev) upewnić się, że template tworzy KV z
> `enablePurgeProtection: false`. Alternatywa: niektóre template'y obsługują
> `createMode: 'recover'`, który odzyskuje vault zamiast tworzyć nowy.

### 11.5. `RoleAssignmentExists` / `RoleAssignmentUpdateNotPermitted` przy redeployu

Przypisanie roli o tym samym GUID już istnieje. Nasz Bicep generuje nazwy przypisań
**deterministycznie** funkcją `guid()` (z principalId + roli + zakresu), więc redeploy jest
idempotentny i ten błąd nie powinien wystąpić. Jeśli jednak wystąpi (np. ktoś nadał rolę ręcznie
w portalu na tym samym zakresie):

```bash
# Znajdź duplikat i usuń ręczne przypisanie
az role assignment list --resource-group hg-dev-rg -o table
az role assignment delete --assignee <principalId> --role "<rola>" --scope <zakres>
```

i ponów wdrożenie. Wariant pokrewny: po skasowaniu i odtworzeniu RG mogą wisieć "osierocone"
przypisania ze starymi principalId (widoczne jako `Identity not found`) — można je usunąć tym
samym `az role assignment delete`.

### 11.6. What-if / deploy wisi albo zrywa połączenie

Wdrożenia subskrypcyjne z wieloma modułami bywają wolne. Jeśli terminal padł, wdrożenie i tak
**leci dalej po stronie Azure** — sprawdź status:

```bash
az deployment sub show -n hg-dev-w1 --query properties.provisioningState -o tsv
```

`Running` → czekaj; `Succeeded` → gotowe; `Failed` → szczegóły błędu:

```bash
az deployment sub show -n hg-dev-w1 --query properties.error -o json
```

oraz portal: Subscriptions → Deployments → hg-dev-w1 → kliknij czerwony moduł.

---

*Koniec przewodnika Tygodnia 1. Następny krok: Tydzień 2 — obraz sensora (Cowrie) w ACR i
Container App w strefie DMZ.*
