# HoneyGrid — Infrastruktura jako kod (Bicep)

Szkielet infrastruktury (Tydzień 0) rozproszonej platformy threat-intelligence
**HoneyGrid** na Azure. Każdy moduł jest poprawny składniowo i wdrażalny;
zasoby z tygodni feature'owych (playbooki, definicje Container Apps, DCR)
są zakomentowanymi stubami z markerami `// TODO (Tydzień N, Track A/B)`.

## Struktura

```
infra/bicep/
├── main.bicep              # orkiestrator (zakres: subskrypcja) — tworzy RG i wpina moduły
├── main.dev.bicepparam     # parametry środowiska dev
├── main.prod.bicepparam    # parametry środowiska prod
└── modules/
    ├── network.bicep       # VNet hub-and-spoke, 3 podsieci, NSG, stuby Private DNS
    ├── data.bicep          # Cosmos DB (serverless), Storage, Event Hubs, Service Bus
    ├── app.bicep           # Container Apps Env, ACR, Static Web App, App Insights
    ├── sentinel.bicep      # Log Analytics, Microsoft Sentinel, DCE (+ stub DCR)
    ├── ai.bicep            # Azure OpenAI (gpt-4o-mini), Azure Maps, Key Vault (RBAC)
    └── rbac.bicep          # macierz najmniejszych uprawnień (role wbudowane)
```

## Jak wdrożyć

Wymagania: Azure CLI z modułem Bicep (`az bicep install`), zalogowanie
(`az login`) i wybrana subskrypcja (`az account set -s <id>`).

```bash
# Walidacja / kompilacja
az bicep build --file infra/bicep/main.bicep

# Podgląd zmian (what-if) — środowisko dev
az deployment sub what-if \
  --location westeurope \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam

# Wdrożenie dev
az deployment sub create \
  --name honeygrid-dev \
  --location westeurope \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam

# Wdrożenie prod (demo/zaliczenie)
az deployment sub create \
  --name honeygrid-prod \
  --location westeurope \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.prod.bicepparam
```

Wdrożenie ma zakres **subskrypcji** — `main.bicep` samo tworzy grupę zasobów
`hg-<env>-rg`, a moduły wdraża już w jej zakresie.

> Uwaga (Azure for Students): Azure OpenAI może wymagać dostępności w regionie /
> zatwierdzonego dostępu. Jeśli wdrożenie modułu `ai` się nie powiedzie,
> zakomentuj moduł w `main.bicep` i wróć do niego w Tygodniu 3.

## Konwencje

- nazwy zasobów: `${namePrefix}-${environment}-<usługa>` (np. `hg-dev-vnet`);
  Storage/ACR bez myślników + sufiks `uniqueString()` dla nazw globalnych,
- parametry w lowerCamelCase,
- tagi na każdym zasobie: `{ project: 'HoneyGrid', environment, track }`,
- komentarze w plikach `.bicep` po polsku,
- każdy moduł wystawia `output`-y (id, endpointy, nazwy) przepinane przez `main.bicep`.

## Opis modułów

| Moduł | Zawartość (Tydzień 0) | Stuby na później |
|---|---|---|
| `network.bicep` | VNet `10.20.0.0/16`, podsieci dmz/logic/data, 3 NSG, polityki Private Endpoint na `snet-data` | strefy Private DNS, Private Endpoints (T2) |
| `data.bicep` | Cosmos DB **serverless** + baza `honeygrid` + 5 kontenerów z TTL, Storage (blob: `raw`/`tty`/`downloads`, Files: `cowrie`), Event Hubs Basic (`honeypot-events`), Service Bus Basic (`ai-classify`) | Private Endpoints, `publicNetworkAccess: Disabled` (T2) |
| `app.bicep` | Container Apps Environment (Consumption, scale-to-zero), ACR Basic, Static Web App Free, App Insights na wspólnym workspace | container apps: `cowrie` (T1), `web-honeypot` (T1), `tcp-listener` (T2), `api` (T3) |
| `sentinel.bicep` | Log Analytics (PerGB2018, limit 5 GB/dzień), onboarding Sentinela, **DCE (realny)** | tabela `Cowrie_CL` + DCR `Direct` ze strumieniem `Custom-CowrieStream` (T4), reguły analityczne (T5) |
| `ai.bicep` | Azure OpenAI S0 + deployment `gpt-4o-mini`, Azure Maps Gen2/G2, Key Vault (tryb RBAC, bez access policies) | Communication Services — e-mail (T5) |
| `rbac.bicep` | GUID-y ról jako zmienne, warunkowe przypisania (puste `principalId` ⇒ nic się nie wdraża) | wpięcie principalId tożsamości MI, zawężenie zakresów do DCR/NSG (T2–T5) |

### Kontenery Cosmos DB (baza `honeygrid`)

| Kontener | Klucz partycji | TTL |
|---|---|---|
| `events` | `/attackerIp` | 15552000 s (180 dni) |
| `actors` | `/id` | brak |
| `sessions` | `/sessionId` | 15552000 s (180 dni) |
| `iocs` | `/type` | brak |
| `aggregates` | `/bucket` | 2592000 s (30 dni) |

## Topologia hub-and-spoke

```
                          INTERNET (atakujący)
                                  │
              porty przynęty: 22 23 80 443 3389 2222
                                  │ allow inbound
            ┌─────────────────────▼──────────────────────┐
            │  snet-dmz  10.20.0.0/24   [nsg-dmz]         │
            │  sensory: cowrie / web-honeypot / tcp       │
            │                                             │
            │  OUTBOUND: DENY ALL  ───────────────┐       │
            │  wyjątek: EventHub + AzureMonitor   │       │  « ANTY-PIVOT:
            └─────────────────────────────────────┼───────┘    skompromitowany
                          ║ telemetria            ✕ deny       sensor nie wejdzie
                          ║ (Event Hubs / DCE)    │            do warstwy analitycznej
            ┌─────────────▼───────────────────────┼───────┐
            │  snet-logic  10.20.1.0/24  [nsg-logic]      │
            │  Container Apps: api, klasyfikator AI       │
            │  BEZ publicznych IP; deny inbound z DMZ     │
            └─────────────┬───────────────────────────────┘
                          │ tylko Private Link
            ┌─────────────▼───────────────────────────────┐
            │  snet-data  10.20.2.0/24   [nsg-data]       │
            │  Private Endpoints: Cosmos / Storage / SB   │
            │  privateEndpointNetworkPolicies: Disabled   │
            └─────────────────────────────────────────────┘

   Poza VNet (PaaS):  Log Analytics + Sentinel ◄─ DCE/DCR
                      Azure OpenAI · Azure Maps · Key Vault (RBAC)
                      Static Web App (dashboard) · ACR
```

Kluczowe decyzje:
1. **Anty-pivot** — outbound z DMZ domyślnie zablokowany (reguła `Deny-Outbound-All`,
   priorytet 4000); jedyny kanał wyjścia to telemetria (tag `EventHub`/`AzureMonitor`).
   Druga linia: `nsg-logic` i `nsg-data` jawnie odrzucają ruch z podsieci DMZ.
2. **Warstwa danych tylko przez Private Link** — w T2 Cosmos/Storage/Service Bus
   przechodzą na Private Endpoints w `snet-data` i `publicNetworkAccess: Disabled`.
3. **Architektura w pełni bezkluczowa** — `disableLocalAuth: true` (Cosmos, Event Hubs,
   Service Bus, OpenAI, Maps), ACR bez konta admin, Key Vault wyłącznie w trybie RBAC;
   dostęp przez Managed Identity. Jedyne wyjątki: klucz Storage dla Azure Files/SMB
   (wymóg protokołu, persystencja Cowrie) i `sharedKey` Log Analytics dla logów
   konsoli Container Apps (wymóg platformy).

## Macierz RBAC (least privilege)

| Tożsamość | Rola (GUID) | Zakres | Cel |
|---|---|---|---|
| MI sensora (cowrie/tcp-listener) | Monitoring Metrics Publisher `3913510d-…` | DCR (T0: RG) | wysyłka logów przez Logs Ingestion API |
| Analityk / CI | Microsoft Sentinel Contributor `ab8e14d6-…` | Resource Group | reguły analityczne, watchlisty |
| Tożsamość Sentinela | Microsoft Sentinel Automation Contributor `f4c81013-…` | Resource Group | automation rules → uruchamianie playbooków |
| Analityk / CI | Logic App Contributor `87a39d53-…` | Resource Group | tworzenie/edycja playbooków |
| MI playbooka | Microsoft Sentinel Responder `3e150937-…` | Resource Group | aktualizacja incydentów |
| MI playbooka | Network Contributor `4d97b98b-…` | NSG dmz (T0: RG) | playbook dopisuje regułę blokującą IP |

Role płaszczyzny danych (Cosmos Data Contributor, Event Hubs Data Sender,
Service Bus Data Sender/Receiver, Storage Blob Data Contributor, OpenAI User,
AcrPull) dochodzą w tygodniach feature'owych — lista w `rbac.bicep`.

## Koszty (Azure for Students, ~100 USD kredytu)

| Decyzja | Efekt kosztowy |
|---|---|
| Cosmos DB **serverless** (`EnableServerless`) | zero opłaty stałej; płacisz za RU i GB — przy ruchu honeypotowym grosze |
| TTL na kontenerach (`events`/`sessions` 180 dni, `aggregates` 30 dni) | składowanie nie rośnie bez końca |
| Event Hubs **Basic** (1 TU) | ~11 USD/mies. — najdroższy stały element; rozważ wyłączanie poza demo |
| Service Bus **Basic** | opłata za operacje (~0,05 USD/mln), brak opłaty stałej |
| Container Apps **Consumption + scale-to-zero** (`minReplicas: 0`) | API i klasyfikator nie kosztują nic, gdy nikt nie atakuje; darmowy grant 180k vCPU-s/mies. |
| Log Analytics: `dailyQuotaGb = 5` | twardy bezpiecznik — ingestia stop po 5 GB/dzień; Sentinel **darmowy do 5 GB/dzień przez 31 dni triala** |
| Static Web App **Free**, ACR **Basic**, Storage **LRS** | najtańsze tiery, wystarczające na coursework |
| Azure Maps **Gen2/G2** | darmowy wolumen transakcji na mapę dashboardu |
| gpt-4o-mini, **GlobalStandard** | rozliczenie per token, najtańszy model; brak opłaty stałej |
| `retentionInDays = 90`, soft-delete KV 7 dni | krótkie retencje = niższe składowanie |

**Praktyka:** po zajęciach `az group delete -n hg-dev-rg --no-wait` — cały szkielet
odtwarzasz jednym `az deployment sub create` (idempotentnie). Środowisko `prod`
wdrażaj tylko na czas demo.
