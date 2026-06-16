# HoneyGrid — opis projektu i obrona Track B (warstwa danych, AI i realtime na Azure)

Dokument do obrony własnej części projektu kursowego (Microsoft Azure). Zawiera:
(1) pełny opis platformy i jej funkcji, (2) szczegółowy opis implementacji **Track B**
przez pryzmat usług Azure — jak każda usługa działa, jaki kod jest z nią związany,
jak usługi są spięte, (3) model kosztów, bezpieczeństwo i narzędzia, (4) mapowanie na
efekty uczenia się (W01–W07, U01–U05, K01–K02) oraz przykładowe pytania obrończe.

---

## 1. Czym jest HoneyGrid (cel i problem biznesowy)

**HoneyGrid** to rozproszona platforma *threat intelligence* (wywiad o zagrożeniach)
zbudowana w chmurze publicznej Microsoft Azure. Sieć **honeypotów** (przynęt:
SSH/Telnet, web, RDP/TCP) wystawiona do internetu wabi realne ataki, a platforma
w czasie zbliżonym do rzeczywistego:

1. **zbiera** próby ataków (logowania, komendy, żądania HTTP, pobierane pliki),
2. **wzbogaca** każde zdarzenie o geolokalizację (GeoIP/ASN) i reputację IP (threat intel),
3. **klasyfikuje** zdarzenia przy pomocy AI (faza *kill chain*, zaawansowanie, intencja),
4. **koreluje** aktywność w „aktorów zagrożeń" i generuje ich dossier,
5. **publikuje** wskaźniki kompromitacji w standardzie **STIX 2.1** (do SIEM),
6. **prezentuje** wszystko na dashboardzie React aktualizowanym **na żywo** (SignalR):
   globus 3D ataków, żywą lentę zdarzeń, profile aktorów, analizę poświadczeń, kanał IoC.

**Problem biznesowy (U01):** organizacje potrzebują taniego, skalowalnego sposobu
obserwowania realnych technik atakujących bez ryzyka dla produkcji. Klasyczny serwer
24/7 jest drogi i mało elastyczny. Rozwiązanie chmurowe (serverless + zarządzane usługi)
skaluje się od zera, płaci się tylko za realne zużycie, a bezpieczeństwo opiera się na
tożsamościach zarządzanych zamiast kluczy.

**Podział pracy (dwie osoby):**
- **Track A — Detekcja i Reagowanie**: sensory, Event Hub, ingest/wzbogacanie,
  Microsoft Sentinel (DCR, reguły KQL), SOAR (Logic Apps), STIX engine, Session Replay.
- **Track B — Wywiad, API i Doświadczenie (TA CZĘŚĆ)**: model danych Cosmos + Change Feed,
  klasyfikacja AI (Azure OpenAI), API dashboardu, realtime przez SignalR, profilowanie
  aktorów, agregaty, dobowy briefing, observability, hosting front/back na Azure,
  Infrastructure-as-Code (Bicep) dla płaszczyzny danych/aplikacji.

---

## 2. Architektura ogólna i miejsce Track B

```
            Atakujący z internetu
                    │  (próby ataków)
            ┌───────▼─────────── DMZ (Track A) ───────────┐
            │  Honeypoty: Cowrie (SSH) · Web · TCP/RDP     │
            └───────┬─────────────────────────────────────┘
                    │ telemetria (JSON)
            Event Hub ──► Worker Ingestion (Track A: GeoIP, ASN, threat intel)
                    │
        ┌───────────▼─────────────── Cosmos DB (serverless) ───────────────┐
        │  events / actors / sessions / iocs / aggregates   (Change Feed)   │
        └───┬───────────────────────────────┬──────────────────────────────┘
            │ Change Feed                    │ Change Feed / Timer
            ▼                                ▼
   [Func: ClassifyEvents]            [Func: FanOutToSignalR]   [Func: BuildAggregates]
   Azure OpenAI → PATCH               → Azure SignalR Service   [Func: CorrelateActors]
   events.classification              (Serverless) → dashboard  [Func: DailyBriefing]
            │                                                          │
            └──────────────► Cosmos (events/actors/aggregates) ◄───────┘
                                       ▲
                    HoneyGrid.Api (Container Apps, REST, tylko ODCZYT)
                                       ▲
                    Dashboard React (SPA) ── REST + SignalR (realtime)

   Wszystko bezkluczowo: Managed Identity + Microsoft Entra ID + RBAC.
   Observability: OpenTelemetry / App Insights. IaC: Bicep (zakres subskrypcji).
```

Track B to **„prawa strona" tego diagramu**: od momentu, gdy zdarzenie trafia do Cosmos,
aż po to, co widzi analityk w przeglądarce.

---

## 3. Track B — usługi Azure szczegółowo

Dla każdej usługi: **(a) czym jest i jak działa**, **(b) jak jej używam w projekcie**,
**(c) kluczowy kod**, **(d) który efekt uczenia pokazuje**.

### 3.1 Azure Cosmos DB (NoSQL, tryb serverless) — baza danych platformy

**(a) Jak działa.** Cosmos DB to globalnie rozproszona, w pełni zarządzana baza NoSQL.
Dane dzielone są na **partycje logiczne** wg *partition key*; dobry klucz rozkłada
ruch równomiernie i pozwala czytać dane jednej encji bez kosztownego *cross-partition
fan-out*. Przepustowość mierzona jest w **RU/s (Request Units)**. W trybie **serverless**
nie rezerwuję przepustowości — płacę za **faktycznie zużyte RU**, co idealnie pasuje do
nierównego ruchu honeypotów. **Change Feed** to wbudowany, trwały strumień zmian
(insert/update) w kontenerze — fundament architektury sterowanej zdarzeniami.

**(b) Jak używam.** Baza `honeygrid`, 5 kontenerów dobranych do wzorca dostępu:

| Kontener | Partition key | TTL | Po co |
|---|---|---|---|
| `events` | `/attackerIp` | 180 dni | znormalizowane zdarzenia; PK po IP eliminuje fan-out przy profilowaniu aktora |
| `actors` | `/id` | ∞ | profile aktorów + dossier AI |
| `sessions` | `/sessionId` | 180 dni | metadane sesji TTY (Track A) |
| `iocs` | `/type` | ∞ | wskaźniki STIX |
| `aggregates` | `/bucket` | 30 dni | predrachowane statystyki dashboardu + dobowy briefing |

**TTL** automatycznie usuwa surową telemetrię (minimalizacja danych — K01), zostawiając
indykatory i wyniki modeli. Dostęp **bezkluczowo**: `disableLocalAuth`, autoryzacja przez
**Managed Identity + rola płaszczyzny danych Cosmos** (Built-in Data Contributor dla
funkcji = zapis; Built-in Data Reader dla API = tylko odczyt — *least privilege*).

**(c) Kod.** Rejestracja klienta bezkluczowo + serializacja zgodna z kontraktem:

```csharp
// HoneyGrid.Functions/Program.cs — klient Cosmos na tożsamości zarządzanej
builder.Services.AddSingleton(sp =>
{
    var endpoint = config["CosmosConnection:accountEndpoint"];   // wstrzyknięty przez Bicep
    return new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options // camelCase, enumy jako string
    });
});
```

Odczyt w API (Minimal API, tylko SELECT):

```csharp
// HoneyGrid.Api/Features/Feed/FeedEndpoints.cs
var query = new QueryDefinition("SELECT TOP @take * FROM c ORDER BY c.timestamp DESC")
    .WithParameter("@take", limit);
using var it = container.GetItemQueryIterator<HoneypotEvent>(query);
```

Bicep tworzący kontenery z TTL warunkowym:

```bicep
// infra/bicep/modules/data.bicep
resource cosmosContainers '...sqlDatabases/containers@2024-11-15' = [for c in cosmosContainers: {
  name: c.name
  properties: { resource: union(
    { id: c.name, partitionKey: { paths: [c.partitionKey], kind: 'Hash' } },
    c.defaultTtl != null ? { defaultTtl: c.defaultTtl } : {}) }
}]
```

**(d) Efekty:** W07, U05 (integracja z bazą jako usługą zewnętrzną), U04 (architektura),
K01 (minimalizacja danych, bezklucze).

---

### 3.2 Azure Functions (.NET isolated, plan Flex Consumption) — konwejer zdarzeń

**(a) Jak działa.** Azure Functions to *serverless compute* — kod uruchamiany przez
**wyzwalacze (triggers)**, bez zarządzania serwerem. W modelu **isolated worker** funkcje
działają w osobnym procesie .NET (pełna kontrola DI, własna wersja .NET 10). Wybrałem
plan **Flex Consumption (FC1)** — skaluje od zera, rozlicza per czas wykonania i pamięć,
ma **pełne wsparcie .NET 10 isolated** oraz bezkluczowy *host storage* (klasyczny Linux
Consumption Y1 nie startuje workera .NET 10 — to udokumentowana decyzja w `functions.bicep`).
Funkcje pobierają kod z **kontenera blob** (Flex OneDeploy), a nie ze zmiennej
`WEBSITE_RUN_FROM_PACKAGE`.

**(b) Jak używam — sześć funkcji Track B:**

| Funkcja | Wyzwalacz | Rola |
|---|---|---|
| `ClassifyEvents` | CosmosDBTrigger (`events`, prefix dzierżaw `classify`) | klasyfikacja AI + fallback stub → PATCH `classification` |
| `FanOutToSignalR` | CosmosDBTrigger (`events`, prefix `fanout`) | rozsyłanie zdarzeń do dashboardu przez SignalR |
| `BuildAggregates` | TimerTrigger (co 5 min) | predrachowanie statystyk → `aggregates` |
| `CorrelateActors` | TimerTrigger (co 30 min) | odciski → klastrowanie → profile aktorów + backlink |
| `DailyBriefing` | TimerTrigger (06:00) | dobowe podsumowanie → `aggregates`/App Insights |
| `negotiate` | HttpTrigger | wydanie tokenu połączenia SignalR (tryb Serverless) |

**Ważny szczegół inżynierski:** dwa wyzwalacze Change Feed na tym samym kontenerze
**muszą mieć osobne prefiksy dzierżaw** (`classify` vs `fanout`), inaczej procesory
„kradną sobie" dzierżawy partycji.

**(c) Kod.** Wyzwalacz Change Feed + częściowy zapis (PATCH — tani w RU):

```csharp
// HoneyGrid.Functions/Functions/ClassifyEvents.cs
[Function(nameof(ClassifyEvents))]
public async Task Run(
    [CosmosDBTrigger(databaseName:"%CosmosDatabase%", containerName:"events",
        Connection="CosmosConnection", LeaseContainerName="leases",
        LeaseContainerPrefix="classify", CreateLeaseContainerIfNotExists=true)]
    IReadOnlyList<HoneypotEvent> changes, CancellationToken ct)
{
    var pending = changes.Where(e => e.Classification is null).ToList();  // brak pętli: PATCH ma classification
    var aiResults = _classifier.Enabled ? await _classifier.ClassifyAsync(pending, ct) : new ...;
    for (var i = 0; i < pending.Count; i++)
    {
        var c = IsUsable(aiResults[i]) ? aiResults[i]! : StubClassifier.Classify(pending[i]); // AI lub fallback
        await container.PatchItemAsync<HoneypotEvent>(pending[i].Id.ToString(),
            new PartitionKey(pending[i].AttackerIp),
            [PatchOperation.Add("/classification", c)], cancellationToken: ct);
    }
}
```

Bicep (Flex + bezkluczowy storage + stos w `functionAppConfig`, nie w `appSettings`):

```bicep
// infra/bicep/modules/functions.bicep
functionAppConfig: {
  deployment: { storage: { type: 'blobContainer'
    value: '${storage.properties.primaryEndpoints.blob}app-package'
    authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: functionsIdentityId } } }
  runtime: { name: 'dotnet-isolated', version: '10.0' }
  scaleAndConcurrency: { instanceMemoryMB: 2048, maximumInstanceCount: 40 }
}
```

**(d) Efekty:** U02 (C#/.NET integrowane z Azure), U03 (konfiguracja i uruchomienie),
W02 (szczegóły platformy), W03 (model serverless), K02 (problemy zarządzania — np.
dzierżawy, wersja runtime).

---

### 3.3 Azure OpenAI (deployment `gpt-5.4-nano`) — klasyfikacja AI

**(a) Jak działa.** Azure OpenAI udostępnia modele językowe jako zarządzaną usługę
(SLA, sieć prywatna, RBAC Entra ID). Tworzę **deployment** konkretnego modelu
(`gpt-5.4-nano`) w trybie **GlobalStandard** (rozliczenie **per token**, bez opłaty
stałej). Wywołania to *chat completions*. Dostęp **bezkluczowo** — rola
*Cognitive Services OpenAI User* dla tożsamości funkcji.

**(b) Jak używam.** Funkcja `ClassifyEvents` zbiera batch zdarzeń i jednym wywołaniem
modelu prosi o klasyfikację (faza kill chain, kategoria, zaawansowanie 0–1, intencja).
Logika jest **odporna**: budowanie promptu i parsowanie odpowiedzi to czyste,
testowalne klasy; przy braku konfiguracji lub błędzie modelu następuje **łagodna
degradacja** do deterministycznego `StubClassifier` (kontrakt danych się nie zmienia).

**(c) Kod.** Klient bezkluczowy + retry na 429/5xx:

```csharp
// HoneyGrid.Functions/Ai/OpenAiClassifier.cs
var client = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
_chat = client.GetChatClient(deployment);          // deployment z OpenAIDeployment (gpt-5.4-nano)
...
ClientResult<ChatCompletion> r = await _chat.CompleteChatAsync(messages,
    new ChatCompletionOptions { Temperature = 0f }, ct);
return ClassificationResponseParser.Parse(r.Value.Content[0].Text, batch.Count); // odporny parser
```

Parser toleruje markdown, niepełne tablice, synonimy faz — żeby „nieprzewidywalność LLM"
nie wywaliła konwejera (klasy `ClassificationPrompt`, `ClassificationResponseParser`
mają testy jednostkowe).

**(d) Efekty:** U01 (dobór usługi AI do problemu), U02 (integracja C# ↔ Azure OpenAI),
W02, K01 (treści ataków, content safety / odporność).

---

### 3.4 Azure SignalR Service (tryb Serverless) — realtime na dashboard

**(a) Jak działa.** SignalR Service to zarządzany *backplane* WebSocket/SSE — utrzymuje
tysiące trwałych połączeń z przeglądarkami i pozwala serwerowi rozsyłać komunikaty.
W trybie **Serverless** nie ma trwałego huba w aplikacji serwerowej: klient pyta
**endpoint `negotiate`** o adres usługi + krótkotrwały token, łączy się **bezpośrednio**
z usługą SignalR, a **funkcje** rozsyłają wiadomości przez *output binding*.

**(b) Jak używam.** Funkcja `FanOutToSignalR` (Change Feed) wypycha każde zdarzenie do
huba `attacks`, metoda `attack` — dokładnie tej nazwy nasłuchuje front. Endpoint
`negotiate` (HttpTrigger + `SignalRConnectionInfoInput`) wydaje token. Połączenie z
usługą SignalR jest **bezkluczowe** (`AzureSignalRConnectionString__serviceUri` + rola
*SignalR Service Owner*).

**(c) Kod.**

```csharp
// HoneyGrid.Functions/Functions/FanOutToSignalR.cs
[Function(nameof(FanOutToSignalR))]
[SignalROutput(HubName = "attacks")]
public SignalRMessageAction[] Run([CosmosDBTrigger(... LeaseContainerPrefix="fanout" ...)]
    IReadOnlyList<HoneypotEvent> changes)
    => changes.Select(e => new SignalRMessageAction("attack", [e])).ToArray();
```

```csharp
// HoneyGrid.Functions/Functions/NegotiateSignalR.cs (Serverless negotiate)
[Function("negotiate")]
public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous,"get","post", Route="hubs/attacks/negotiate")] HttpRequestData req,
    [SignalRConnectionInfoInput(HubName="attacks")] string connectionInfo) { /* zwraca token */ }
```

Front łączy się i — gdy realtime niedostępny (np. lokalny dev na mockach) — **uczciwie**
przełącza się na symulator i pokazuje znacznik *demo mode* (`lib/liveAttacks.ts`).

**(d) Efekty:** U02, U05 (rozszerzenie aplikacji o usługę zewnętrzną realtime), W05
(aplikacje internetowe), W02.

---

### 3.5 Azure Container Apps — host REST API dashboardu

**(a) Jak działa.** Container Apps to serverless dla kontenerów (na Kubernetes/KEDA pod
spodem, ale bez zarządzania klastrem). Skalowanie **scale-to-zero** — przy braku żądań
nie płacę za nic; pierwsze żądanie po uśpieniu ma zimny start (akceptowalne dla API
analitycznego).

**(b) Jak używam.** `HoneyGrid.Api` (ASP.NET Core **Minimal API**) wystawia REST tylko do
**odczytu** Cosmos: `/api/feed`, `/api/stats/{overview|geo|credentials}`, `/api/actors`,
`/api/actors/{id}` (Track B) oraz `/api/iocs/stix`, `/api/sessions/{id}/replay` (Track A).
Tożsamość API ma rolę **Cosmos Data Reader** (least privilege). Endpointy są **defensywne**
— błąd Cosmos zwraca pustą odpowiedź, nigdy 500, więc dashboard pozostaje użyteczny.

**(c) Kod.**

```csharp
// HoneyGrid.Api/Program.cs — bezkluczowo, observability, CORS, endpointy
if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();   // 3.6
builder.Services.AddSingleton(new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), opts));
app.MapFeedEndpoints(); app.MapStatsEndpoints(); app.MapActorEndpoints();
```

Szybka ścieżka statystyk: API najpierw czyta **predrachowany agregat** z `aggregates`
(tani odczyt po kluczu), a dopiero przy jego braku liczy „na żywo".

**(d) Efekty:** U02, U03, W05, U04 (warstwa aplikacyjna strefy docelowej).

---

### 3.6 Application Insights + OpenTelemetry — obserwowalność

**(a) Jak działa.** Application Insights (na bazie Log Analytics) zbiera ślady, metryki,
logi i zależności. **OpenTelemetry** to otwarty standard instrumentacji; pakiet
*Azure Monitor OpenTelemetry* eksportuje telemetrię do App Insights jedną linijką.

**(b) Jak używam.** API: `AddOpenTelemetry().UseAzureMonitor()` (czyta
`APPLICATIONINSIGHTS_CONNECTION_STRING`). Funkcje: *Application Insights worker service*
+ `ConfigureFunctionsApplicationInsights()`. Dzięki temu widzę przepływ:
Change Feed → klasyfikator → SignalR, korelację po `attackerIp`/`sessionId`, latencję
OpenAI, odsetek fallbacku na stub.

**(d) Efekty:** K02 (zarządzanie i diagnoza problemów), U03, W02.

---

### 3.7 Microsoft Entra ID + Managed Identity + RBAC — architektura BEZKLUCZOWA

**(a) Jak działa.** Zamiast kluczy/haseł w konfiguracji, każda usługa obliczeniowa ma
**tożsamość zarządzaną** (Managed Identity). Przy wywołaniu pobiera **token OAuth2 z
Entra ID** i przedstawia go usłudze docelowej; ta sprawdza **przypisanie roli RBAC**.
Sekrety nie istnieją → nie ma czego wykraść ani rotować.

**(b) Jak używam.** `DefaultAzureCredential` w całym kodzie .NET (lokalnie `az login`,
w chmurze Managed Identity). Function App ma **tożsamość user-assigned** (`id-functions`)
— świadomie, bo `principalId` musi być znany **na starcie wdrożenia** do nazw przypisań
ról (`guid(...)`, problem BCP120 przy system-assigned). Macierz ról (least privilege):

| Tożsamość | Zasób | Rola |
|---|---|---|
| Functions | Cosmos | Built-in Data Contributor (zapis) |
| Functions | Azure OpenAI | Cognitive Services OpenAI User |
| Functions | SignalR | SignalR Service Owner |
| Functions | Storage | Blob Data Owner + Queue Data Contributor (host keyless) |
| API | Cosmos | Built-in Data Reader (tylko odczyt) |

**(c) Kod (Bicep).**

```bicep
// rola płaszczyzny danych Cosmos dla funkcji (00000000-...-0002 = Data Contributor)
resource cosmosDataContributor '...sqlRoleAssignments@2024-11-15' = {
  parent: cosmos
  name: guid(cosmos.id, functionsIdentityPrincipalId, 'data-contributor')
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: functionsIdentityPrincipalId
    scope: cosmos.id
  }
}
```

**(d) Efekty:** K01 (bezpieczeństwo systemów), U04 (strefa docelowa), W02, K02.

---

### 3.8 Azure Storage — magazyn pomocniczy

**(a/b) Jak działa i jak używam.** Konto Storage pełni trzy role: **AzureWebJobsStorage**
(stan runtime Functions — keyless: `accountName` + `credential=managedidentity`),
**kontener `app-package`** na paczkę wdrożeniową Flex (OneDeploy), oraz **Blob** na
nagrania TTY/surowe logi (głównie Track A). Wszystko bezkluczowo (role Storage powyżej).

**(d) Efekty:** W02, K01.

---

### 3.9 Sieć: VNet + Private Endpoint + Private DNS — dostęp do danych

**(a) Jak działa.** Private Endpoint nadaje usłudze PaaS **prywatny adres IP w VNet**;
strefa **Private DNS** rozwiązuje publiczny FQDN (np. `*.documents.azure.com`) na ten
prywatny IP. Ruch nie wychodzi do internetu.

**(b) Jak używam.** Function App jest **zintegrowana z VNet** przez delegowaną podsieć
`snet-func` (`vnetRouteAllEnabled: true`), więc Change Feed sięga Cosmos przez **Private
Endpoint** w `snet-data` (bez integracji change-feed dostaje 403). SignalR/OpenAI/Storage
wychodzą publicznie (świadomy kompromis, opisany w `functions.bicep`).

**(d) Efekty:** U04 (architektura strefy docelowej), K01, W02.

---

### 3.10 Hosting frontu (Static Web Apps / `npm preview`) + frontend jako aplikacja internetowa

**(a/b).** Dashboard to **SPA** (React 18 + Vite + TypeScript + Tailwind v4). W docelowej
konfiguracji region subskrypcji nie wspiera **Static Web Apps**, więc front uruchamiamy
przez `npm run preview` z `VITE_API_BASE`/`VITE_SIGNALR_URL` wskazującymi na Container App
i Function App (`negotiate`). Plik `public/staticwebapp.config.json` zapewnia *SPA
fallback* i routing `/api/*`, gdyby SWA było dostępne. Funkcje frontu Track B:
pulpit (KPI + wykresy recharts), **globus 3D ataków** (react-globe.gl, łuki i kolce
zdarzeń), żywa lenta (SignalR), profile aktorów, analiza poświadczeń (treemap), kanał
STIX, paleta poleceń ⌘K, i18n PL/EN/RU.

**(d) Efekty:** W05 (budowa stron i aplikacji internetowych), W06 (narzędzia), U03.

---

## 4. Przepływ danych Track B — od ataku do ekranu (jeden scenariusz)

1. Atakujący loguje się na honeypot SSH → Track A zapisuje **zdarzenie** do Cosmos `events`
   (PK `/attackerIp`).
2. **Change Feed** budzi **dwie** funkcje:
   - `ClassifyEvents` → woła **Azure OpenAI** (gpt-5.4-nano) → **PATCH** `events.classification`
     (faza kill chain, kategoria, zaawansowanie, intencja); przy błędzie — `StubClassifier`.
   - `FanOutToSignalR` → wypycha zdarzenie przez **Azure SignalR** na dashboard (metoda `attack`).
3. Co 5 min `BuildAggregates` przelicza statystyki → `aggregates` (overview/geo/credentials).
4. Co 30 min `CorrelateActors` buduje odciski, klastruje IP w „aktorów", zapisuje profile
   do `actors` i **dowiązuje** `classification.actorId` zdarzeniom (backlink).
5. O 06:00 `DailyBriefing` zapisuje dobowe podsumowanie do `aggregates`/App Insights.
6. **HoneyGrid.Api** (Container Apps) odczytuje `aggregates`/`actors`/`events` (read-only).
7. **Dashboard** pobiera REST + słucha **SignalR** → globus i lenta aktualizują się **na żywo**.

Cała ścieżka **bezkluczowa**, instrumentowana **OpenTelemetry**, opisana w **Bicep**.

---

## 5. Koszty — model rozliczeń w chmurze (W04)

Chmura rozlicza **zużycie**, nie posiadanie. W Track B dobrałem usługi tak, by koszt
spoczynkowy był bliski zera:

| Usługa | Model rozliczeń | Dlaczego tanio |
|---|---|---|
| Cosmos DB serverless | per **RU** zużyte + GB magazynu | brak rezerwacji; PATCH zamiast full-replace = mniej RU |
| Functions Flex Consumption | per czas wykonania + GB-s pamięci | skala od zera; płacę gdy są zdarzenia |
| Azure OpenAI | per **token** (in/out) | batchowanie 25–50 zdarzeń na wywołanie = mniej tokenów |
| SignalR Service | **Free F1** (limit połączeń/wiadomości) | wystarcza na demo |
| Container Apps (API) | per vCPU-s/GiB-s, **scale-to-zero** | brak ruchu = brak kosztu |
| App Insights / Log Analytics | per GB **ingest** | limit dzienny, próbkowanie |
| Storage | per GB + transakcje | minimalne |

**Optymalizacje świadome:** TTL czyści dane (mniej magazynu), agregaty predrachowane
(API tanie po RU), PATCH częściowy, scale-to-zero, batch do OpenAI. **Bezklucze** to
też oszczędność operacyjna (brak rotacji sekretów, mniejszy koszt incydentu — K01).

---

## 6. Bezpieczeństwo (K01) — w skrócie do obrony

- **Architektura w pełni bezkluczowa**: `disableLocalAuth` na Cosmos/OpenAI/SignalR/Storage;
  dostęp tylko przez Managed Identity + RBAC. Brak sekretów w kodzie/konfiguracji.
- **Least privilege**: API tylko *Reader* na Cosmos; funkcje *Contributor* tam, gdzie zapisują.
- **Sieć**: Cosmos za Private Endpoint, Function App zintegrowana z VNet.
- **Minimalizacja danych**: TTL 180/30 dni; pobrane artefakty trzymane jako hashe (Track A),
  nigdy nie uruchamiane; IP atakujących jako dane telemetryczne o ograniczonej retencji.
- **Pasywność**: honeypoty tylko rejestrują; platforma nie wykonuje akcji ofensywnych.
- **TLS 1.2+, HTTPS only**, CORS ograniczony.

---

## 7. Narzędzia IDE i CLI (W06)

- **IDE**: Visual Studio / VS Code (C#, debug Functions/API), edytor TS/React.
- **dotnet CLI**: `dotnet build/test/run` (6 projektów testowych, 232 testy).
- **Azure CLI (`az`)**: `az login`, `az deployment sub create` (Bicep), `az acr build`
  (budowa obrazów w chmurze), `az functionapp deploy` (zip Flex), `az role assignment`.
- **Azure Functions Core Tools (`func`)**: `func start` lokalnie, `func azure functionapp publish`.
- **Bicep CLI**: `az bicep build` (walidacja IaC).
- **npm / Vite**: `npm ci`, `npm run build/test/preview`.

---

## 8. Infrastructure as Code (Bicep) i strefa docelowa (U04)

Cała infrastruktura jest **kodem** w `infra/bicep`, wdrażana **jednym poleceniem** na
zakresie **subskrypcji** (`main.bicep` sam tworzy Resource Group i wpina moduły):
`network` → `security` → `sentinel` → `data` → `app` → `ai` → `functions` →
`privatelink` → `rbac`. Track B włącza flaga `deployTrackB=true` (OpenAI, SignalR,
Function App). Modułowość = czytelna **strefa docelowa** (landing zone): sieć,
tożsamości, dane, aplikacje, AI, obserwowalność — rozdzielone, z jawnymi zależnościami
przez `outputs`, bez cykli.

```bash
az deployment sub create -l swedencentral -f infra/bicep/main.bicep \
  -p infra/bicep/main.dev.bicepparam     # deployTrackB=true
```

---

## 9. Mapa efektów uczenia się → gdzie to pokazać na obronie

| Efekt | Gdzie w projekcie |
|---|---|
| **W01** definicje chmury | sekcja 1–2 (serverless, PaaS, regiony, RU, Managed Identity) |
| **W02** elementy Azure | sekcja 3 — każda usługa z osobna |
| **W03** działanie chmury publicznej | model serverless/scale-to-zero/Change Feed (3.1, 3.2) |
| **W04** koszty | sekcja 5 (per RU / per token / per GB-s / scale-to-zero) |
| **W05** strony/aplikacje internetowe | API (3.5) + dashboard SPA (3.10) |
| **W06** IDE/CLI | sekcja 7 |
| **W07** integracja z usługami i bazami | Cosmos (3.1), OpenAI (3.3), SignalR (3.4) |
| **U01** dobór usługi do problemu | dlaczego Cosmos serverless, Flex, Serverless SignalR (3.1–3.4) |
| **U02** C#/ASP.NET ↔ Azure | kod w 3.1–3.6 (DefaultAzureCredential, triggery, bindingi) |
| **U03** konfiguracja i uruchomienie | Bicep + app settings + deploy (3.2, 3.5, 8) |
| **U04** architektura strefy docelowej | diagram (2), moduły Bicep (8), sieć (3.9) |
| **U05** rozszerzenie o usługi/bazy zewnętrzne | OpenAI, SignalR, Cosmos (3.1, 3.3, 3.4) |
| **K01** bezpieczeństwo | sekcja 6 (bezklucze, least privilege, Private Endpoint, TTL) |
| **K02** tworzenie i zarządzanie + problemy | dzierżawy Change Feed, wersja .NET na planie, fallback AI, observability (3.2, 3.6) |

---

## 10. Przykładowe pytania obrończe (i krótkie odpowiedzi)

- **Czemu Cosmos, a nie SQL?** Nierówny, półstrukturalny strumień zdarzeń; serverless =
  płacę za RU; Change Feed daje architekturę zdarzeniową za darmo; PK `/attackerIp`
  eliminuje fan-out przy profilowaniu aktora.
- **Czemu Functions, a nie ciągły worker?** Zdarzenia są nieregularne; serverless skaluje
  od zera; triggery (Change Feed/Timer/HTTP) eliminują kod „pętli".
- **Czemu Flex Consumption, a nie zwykły Consumption?** Plan Linux Consumption Y1 nie
  uruchamia workera .NET 10; Flex ma pełne wsparcie .NET 10 i bezkluczowy host storage.
- **Jak działa „bezklucze"?** Managed Identity → token z Entra ID → RBAC u usługi
  docelowej; `disableLocalAuth` wyłącza klucze. Zero sekretów.
- **Czemu SignalR Serverless, a nie hub w API?** Hub żyje w procesie funkcji (output
  binding) — mniej kodu, lepsze skalowanie; API tylko czyta dane (scale-to-zero).
- **Co jeśli OpenAI padnie?** Łagodna degradacja do `StubClassifier`; kontrakt `classification`
  bez zmian; w logach widać odsetek AI vs stub.
- **Co jest demo, a co prawdziwe?** Live Feed/Threat Map/Aktorzy/Poświadczenia/IoC działają
  na realnym konwejerze (lub uczciwie oznaczonym symulatorze). Panele MCP i SDN są
  **ilustracyjne** — mają jawny baner „demo mode" (uczciwość wobec oceniającego).
- **Jak liczę koszt?** Per zużycie: RU (Cosmos), GB-s (Functions/Container Apps), token
  (OpenAI), GB ingest (App Insights); spoczynkowo blisko zera dzięki scale-to-zero i TTL.

---

*Dokument odzwierciedla stan kodu w repozytorium `HoneyGrid-Threat-Intelligence`
(moduły `infra/bicep/*`, `src/HoneyGrid.Functions/*`, `src/HoneyGrid.Api/*`,
`src/HoneyGrid.Web/*`). Fragmenty kodu są skrócone dla czytelności — pełne wersje w repo.*
