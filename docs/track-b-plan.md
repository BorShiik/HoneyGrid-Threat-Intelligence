# Track B — детальный план имплементации

**Intelligence, API & Experience** · разработчик: Anton · параллельно с Track A (товарищ)

> Этот документ — рабочий план *только для Track B*. Он привязан к фактическому состоянию
> репозитория на момент написания (ветка `main`, последний коммит «cowrie + replay»),
> а не к идеальной картине из `honeygrid-final-implementation-plan.md`. Для каждого
> компонента указано: **что уже есть**, **что надо сделать**, **какие файлы трогать**,
> **контракт с Track A**, **критерий готовности**.

---

## 0. Граница ответственности Track B (и что НЕ моё)

Согласно §8.2 финального плана, зона Track B:

**Backend / Azure**
- Cosmos data model доступ на чтение + **Change Feed processor**
- **AI Classification** (Azure OpenAI: kill-chain, sophistication, intent) через буфер Service Bus
- **API-эндпоинты дашборда**: `/api/feed`, `/api/stats/*`, `/api/actors`, `/api/actors/{id}`
- **SignalR Hub + Change Feed → SignalR** (realtime)
- **AI Threat Actor Profiling** (кластеризация + досье через OpenAI)
- Daily AI briefing (опционально, killer-фича «лёгкая»)
- Observability (App Insights / OpenTelemetry на своих сервисах)

**IaC (Bicep)** — application & data plane:
- `modules/data.bicep` (Cosmos, Blob, Service Bus) — уже в основном готов
- `modules/ai.bicep` (OpenAI, Maps) — готов
- `modules/app.bicep` (Container Apps env, API app, SWA, App Insights) — готов
- **добавить: Azure SignalR Service** (сейчас отсутствует — см. §4)

**Frontend** — каркас (уже сделан) + страницы-модули Track B:
- `DashboardPage` (обзор + графики)
- `AttackMapPage` (Live Attack Globe — Azure Maps / 3D)
- `ThreatActorsPage` (граф d3-force + досье)
- `CredentialsPage` (treemap + тренды)
- `LiveFeedPage` (виртуализированная живая лента, привязка SignalR)

**Killer-фичи Track B:** ② Live Attack Globe · ③ AI Threat Actor Profiling · ⑤ Credential Intelligence.

### Что НЕ моё (делает Track A — не трогать, только потреблять контракт)
- Сенсоры (Cowrie/Web/TCP), Event Hub producer, Ingestion Worker, Enrichment
- DCR → Sentinel, KQL-правила, SOAR (Logic Apps)
- **STIX 2.1 generation** (`src/HoneyGrid.Stix`, `StixEndpoints`) — уже реализовано Track A
- **Session Replay backend + UI** (`HoneyGrid.Replay`, `SessionEndpoints`, `SessionReplayPlayer`, `SessionsPage`, `IocPage`) — уже реализовано Track A

> ⚠️ **Разночтение, которое надо снять с товарищем на старте.**
> В `README.md` (таблица «Podział pracy») STIX 2.1 и frontend записаны на Track B,
> а Session Replay — на Track A. В детальном плане (§8.1/§8.2) — наоборот: STIX и
> Session Replay у Track A, а у Track B весь дашборд. **Код в репозитории уже следует
> детальному плану**: `HoneyGrid.Stix`, `StixEndpoints`, `SessionEndpoints`, `IocPage`,
> `SessionsPage`, `SessionReplayPlayer` написаны и помечены как Track A. Поэтому этот
> план берёт за истину детальный план + фактический код: **STIX и Session Replay — НЕ Track B.**
> Зафиксируйте это устно, чтобы не дублировать работу.

> 📝 В коде встречаются метки «Track C / Track D» в TODO-комментариях
> (`Functions/Program.cs`, `AttackHub.cs`). Это артефакт более ранней нарезки; функционально
> это всё Track B (классификатор, broadcast в SignalR). Можно по ходу заменить метки на «Track B».

---

## 1. Текущее состояние Track B (baseline)

| Компонент | Файл(ы) | Статус |
|---|---|---|
| Контракты событий + классификации | `HoneyGrid.Contracts/*`, `docs/openapi.yaml` | ✅ готово (потреблять) |
| Cosmos: 5 контейнеров + RBAC | `infra/bicep/modules/data.bicep` | ✅ готово |
| OpenAI gpt-4o-mini + Azure Maps | `infra/bicep/modules/ai.bicep` | ✅ задеплоено |
| Container Apps env, **API app**, SWA, App Insights | `infra/bicep/modules/app.bicep` | ✅ готово |
| **Azure SignalR Service** | — | ❌ нет ресурса (SignalR сейчас in-process в API) |
| API host + DI (Cosmos/Blob keyless, CORS, health) | `HoneyGrid.Api/Program.cs` | ✅ каркас |
| API: `/api/iocs/stix`, `/api/sessions/{id}/replay` | `Features/Stix`, `Features/Sessions` | ✅ (Track A) |
| **API: `/api/feed`** | — | ❌ нет |
| **API: `/api/stats/{overview,geo,credentials}`** | — | ❌ нет |
| **API: `/api/actors`, `/api/actors/{id}`** | — | ❌ нет |
| **SignalR Hub broadcast + Change Feed** | `Hubs/AttackHub.cs` (пустой stub), Functions | ❌ нет |
| **AI Classifier** | `HoneyGrid.Functions/Program.cs` (пустой) | ❌ нет |
| **Actor profiling / correlation** | — | ❌ нет |
| Frontend каркас (routing, shadcn, тема, MSW, TanStack Query, SignalR-клиент) | `HoneyGrid.Web/src/*` | ✅ готово |
| FE: SignalR-клиент `startAttackHub` | `api/signalr.ts` | ✅ написан, но нигде не вызывается |
| **FE: DashboardPage** | `pages/DashboardPage.tsx` | ❌ placeholder (11 строк) |
| **FE: AttackMapPage (globe)** | `pages/AttackMapPage.tsx` | ❌ placeholder |
| **FE: ThreatActorsPage** | `pages/ThreatActorsPage.tsx` | ❌ placeholder |
| **FE: CredentialsPage** | `pages/CredentialsPage.tsx` | ❌ placeholder |
| **FE: LiveFeedPage** | `pages/LiveFeedPage.tsx` | 🟡 частично (83 строки, без SignalR) |
| FE: IocPage, SessionsPage, DesignSystemPage | `pages/*` | ✅ (Track A / общее) |
| MSW-моки на все эндпоинты | `mocks/handlers.ts`, `mocks/generator.ts` | ✅ готово |

**Вывод:** каркас и контракты на месте, инфраструктура почти вся задеплоена. Оставшаяся
работа Track B — это **backend-логика (классификатор, профилирование, realtime, 4 API-эндпоинта)**
и **5 страниц фронта**. Фронт можно делать целиком на MSW-моках, не дожидаясь backend.

---

## 2. Стратегия параллельной работы (контракты с Track A)

Три контракта-границы (никто никого не блокирует):

1. **Event schema** (`HoneyGrid.Contracts`, готов) — Track A пишет в Cosmos `events`,
   Track B читает. Менять схему — только совместно, через PR в `HoneyGrid.Contracts`.

2. **OpenAPI** (`docs/openapi.yaml`, готов) — фронт Track B кодируется против контракта
   на **MSW-моках**. Backend Track B реализует ровно те же shape'ы. Любое изменение
   контракта → правка `openapi.yaml` + `types/api.ts` + моков одновременно.

3. **`events.classification`** — это **точка стыка моей же работы**: STIX/SOAR у Track A
   зависят от поля `classification`. Чтобы не блокировать товарища, **классификатор
   выкатывается в два шага**: сначала **stub** (фикс-категория по `eventType`,
   тот же shape — Неделя 3), потом реальный OpenAI (Неделя 5). Stub разблокирует Track A немедленно.

**Что я отдаю Track A:** stub-классификации (shape `Classification`) — как только готов stub-классификатор.
**Что я беру у Track A:** примеры `cowrie.json` и нормализованные документы `events` (есть `fixtures/cowrie/`, `fixtures/classification/`).

**Правило для Cosmos data-plane:** API Track B имеет роль **Data Reader** (только чтение),
классификатор/процессоры — **Data Contributor**. Это уже заложено в `data.bicep`
(`apiCosmosDataReader`, `workerCosmosDataContributor`); для Functions понадобится отдельное
назначение роли (см. §5).

---

## 3. Целевая архитектура потока Track B

```
events (Cosmos, PK=/attackerIp)
   │  Change Feed
   ├─────────────► [Function: ClassifyBatch]  ──OpenAI gpt-4o-mini──► PATCH events.classification
   │                     (буфер Service Bus, батч 25–50)                + upsert actors (корреляция)
   │
   └─────────────► [Function: FanOutToSignalR] ──► Azure SignalR Service ──► /hubs/attacks ──► дашборд
                                                                              (Live Globe, Live Feed)

Timer (раз в N минут) ─► [Function: BuildAggregates] ─► Cosmos `aggregates` (overview/geo/credentials)

HoneyGrid.Api (read-only, keyless):
   GET /api/feed            ◄── Cosmos events (пагинация по since/take)
   GET /api/stats/overview  ◄── Cosmos aggregates (bucket=overview)
   GET /api/stats/geo       ◄── Cosmos aggregates (bucket=geo)
   GET /api/stats/credentials ◄── Cosmos aggregates (bucket=credentials)
   GET /api/actors          ◄── Cosmos actors
   GET /api/actors/{id}     ◄── Cosmos actors
   (STIX, sessions/replay — уже есть, Track A)
   /hubs/attacks            ◄── SignalR Hub (backplane: Azure SignalR Service)
```

Ключевое архитектурное решение: **агрегаты считаются заранее** (Timer-функция пишет в
`aggregates`), а API их только отдаёт. Это держит RU/s низкими и API быстрым. На время разработки
можно отдавать агрегаты «на лету» из `events`, а предрасчёт включить позже.

---

## 4. Инфраструктура (Bicep) — задачи Track B

Почти всё готово. Остаётся **один значимый ресурс** и пара мелочей.

### 4.1 Azure SignalR Service (НОВЫЙ ресурс) — приоритет
Сейчас SignalR работает in-process внутри API-контейнера (`builder.Services.AddSignalR()`).
Для масштабирования и для того, чтобы **Change-Feed-функция могла слать сообщения клиентам
через backplane**, нужен **Azure SignalR Service** (или его serverless-режим).

Файл: `infra/bicep/modules/app.bicep` (рядом с `staticWebApp`/`apiApp`).

```bicep
resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: '${namePrefix}-${environment}-signalr'
  location: location
  sku: { name: 'Free_F1', capacity: 1 }   // Free хватит на coursework; Standard_S1 для прод
  kind: 'SignalR'
  properties: {
    features: [ { flag: 'ServiceMode', value: 'Serverless' } ] // см. решение ниже
    cors: { allowedOrigins: [ '*' ] }
  }
}
output signalRName string = signalR.name
output signalRHostName string = signalR.properties.hostName
```

- **Проблема:** Hub-логика и Change-Feed-функция — **разные процессы** (API vs Functions).
  Если оставить Default ServiceMode, функции придётся слать сообщения через
  `Microsoft.Azure.SignalR.Management` (ServiceManager) — больше кода.
- **Рекомендация для coursework (минимум кода):** **Serverless mode** + в Functions
  **output binding** `[SignalROutput(HubName="attacks")]`. Тогда broadcast целиком в функции,
  а API раздаёт только negotiate-эндпоинт. Это убирает межпроцессную проблему.
  *Согласовать с тем, как `Program.cs` API регистрирует hub (см. §6.3).*

Назначить роли (в `rbac.bicep` или инлайн): API identity и Functions identity →
`SignalR Service Owner` (Serverless) / `App Server`.

### 4.2 Functions App (host) — проверить наличие
Нужен Azure Functions host (Flex Consumption) для классификатора и процессоров, либо
запуск их как Container App worker. В `app.bicep` сейчас контейнерные приложения для
cowrie/web/api. **Решение:** добавить **Function App (Flex Consumption)** в `app.bicep`.
Репо уже содержит проект `HoneyGrid.Functions` (пустой) — путь через Functions согласован
со скелетом и даёт меньше кода для Change Feed (CosmosDBTrigger) и SignalR (output binding).

> **Архитектурный выбор (зафиксировать):** классификатор и Change-Feed-процессор —
> Azure Functions (CosmosDB/ServiceBus + SignalR-binding). Альтернатива — Worker Service
> в Container Apps (по аналогии с `HoneyGrid.Ingestion`), но это больше ручного кода.
> **Рекомендация: Functions.**

### 4.3 Мелочи
- Прокинуть в API настройки `HoneyGrid:CosmosEndpoint`, `HoneyGrid:BlobServiceUri`
  (уже читаются в `Program.cs`) + `HoneyGrid:SignalREndpoint` для negotiate.
- App Insights connection string → в Functions и API (observability, §8).
- Параметры `main.dev.bicepparam` / `main.prod.bicepparam`: добавить SKU SignalR.

**Критерий готовности §4:** `az deployment group ... --parameters main.dev.bicepparam` проходит,
SignalR Service виден в портале, managed identities имеют роли на Cosmos/SignalR.

---

## 5. AI Classification (killer-фича-зависимость, Service Bus + OpenAI)

**Цель:** обогатить каждое событие полем `classification` (kill-chain, category,
sophistication, intent, actorId). Контракт shape — `Classification` из `openapi.yaml`
и `HoneyGrid.Contracts` (enum `KillChainPhase` уже есть).

### Шаг 1 — Stub-классификатор (Неделя 3, разблокирует Track A)
Файл: `src/HoneyGrid.Functions/Functions/ClassifyEvents.cs` (новый).

- Триггер: **CosmosDBTrigger** на контейнере `events` (Change Feed). Для stub проще всего.
- Логика stub: маппинг по `eventType` → фиксированная `Classification`:
  - `login.failed`/`login.success` → category `brute-force`, killChain `exploitation`, sophistication 0.2
  - `command` → category `post-exploitation`, killChain `installation`, sophistication 0.5
  - `http.request` → category `web-scan`, killChain `recon`, sophistication 0.1
  - `connect` → category `recon`, killChain `recon`, sophistication 0.1
- Запись: **PATCH** документа `events` (partial update `classification`), идемпотентно
  (не переписывать, если уже есть `classification` от реального классификатора).
- Зарегистрировать `HoneyGridJson.Options` как сериализацию (TODO в `Program.cs` уже стоит).

**Готовность шага 1:** в Cosmos у новых `events` появляется `classification`; Track A видит поле.

### Шаг 2 — Реальный OpenAI-классификатор (Неделя 5)
- Пакеты: `Azure.AI.OpenAI`, `Polly` (retry/circuit breaker),
  `Microsoft.Azure.Functions.Worker.Extensions.CosmosDB` / `.ServiceBus`.
- **Буферизация батчами 25–50** через Service Bus (экономия токенов). Поток:
  Change Feed → ServiceBusOutput (очередь `classify`) → ServiceBusTrigger(батч) → OpenAI → PATCH.
  *Service Bus namespace уже есть в `data.bicep`.*
- Промпт (из §6.7 плана): системная роль «SOC-аналитик», строгий JSON-массив без markdown,
  поля `killChainPhase|category|sophistication|intent|confidence`. Модель **gpt-4o-mini**
  (deployment уже создан в `ai.bicep`).
- Парсинг: `ParseJsonSafe` с фолбэком на stub-значения при невалидном ответе (надёжность демо).
- Keyless: `DefaultAzureCredential` + endpoint OpenAI из конфигурации (Managed Identity → роль
  `Cognitive Services OpenAI User`).
- Retry/throttle: Polly — экспоненциальный бэкофф на 429.

**Файлы:**
- `src/HoneyGrid.Functions/Functions/ClassifyEventsBatch.cs`
- `src/HoneyGrid.Functions/Ai/OpenAiClassifier.cs` (промпт, вызов, парсинг)
- `src/HoneyGrid.Functions/Ai/ClassificationPrompt.cs`
- обновить `HoneyGrid.Functions.csproj` (пакеты), `Program.cs` (DI: HttpClient, OpenAI client, Cosmos)
- роль Cosmos Data Contributor для Functions identity → `data.bicep`

**Тесты:** `tests/HoneyGrid.Functions.Tests/` (новый проект) —
парсер JSON-ответа (валидный/мусор/частичный), маппинг stub, идемпотентность PATCH.
Использовать `fixtures/classification/mock-classifications.json`.

**Готовность §5:** событие проходит Change Feed → классифицируется OpenAI → `classification`
с осмысленными значениями; при сбое OpenAI — graceful fallback на stub; есть зелёные unit-тесты.

---

## 6. API дашборда + SignalR realtime

### 6.1 `/api/feed` (Неделя 2 на моках → backend Неделя 3)
Файл: `src/HoneyGrid.Api/Features/Feed/FeedEndpoints.cs` (новый, по образцу `StixEndpoints`).

- `GET /api/feed?since=<ISO8601>&take=<n>` → `HoneypotEvent[]` (контракт в `openapi.yaml:63`).
- Cosmos query по `events`, сортировка `timestamp desc`, фильтр `c.timestamp > @since`,
  `TOP @take` (валидировать `take` ≤ 200). Continuation/пагинация по `since`.
- Read-only CosmosClient уже в DI (`Program.cs`). Зарегистрировать endpoint в `Program.cs`
  рядом с `MapStixEndpoints()`.

### 6.2 `/api/stats/{overview,geo,credentials}` (Неделя 3–4)
Файл: `src/HoneyGrid.Api/Features/Stats/StatsEndpoints.cs` (новый).

- `overview` → `StatsOverview` (totals, severity breakdown, timeseries) — `openapi.yaml:501`.
- `geo` → `GeoStats`/`GeoBucket[]` (страна, lat/lon, count) — для globe heatmap.
- `credentials` → `CredentialStats` (top logins, top passwords, top pairs) — для treemap.
- **Источник:** контейнер `aggregates` (PK `/bucket`), который наполняет Timer-функция (§7).
  На раннем этапе — агрегировать прямо из `events` (GROUP BY) и закэшировать (output caching).

### 6.3 SignalR Hub broadcast + Change Feed (Неделя 2 realtime E2E)
Файлы: `src/HoneyGrid.Api/Hubs/AttackHub.cs` (дополнить), Functions-процессор.

- **Серверный контракт уже задан фронтом:** событие называется `"attack"`
  (`api/signalr.ts: ATTACK_EVENT='attack'`, путь `/hubs/attacks`).
  ⚠️ В `AttackHub.cs` комментарий говорит про метод `"attackReceived"` — **привести к `"attack"`**,
  чтобы совпасть с фронтом, ЛИБО поправить фронт. Зафиксировать одно имя. **Рекомендую `"attack"`** (фронт уже на нём).
- Поток realtime: **CosmosDBTrigger на `events`** → функция шлёт каждое (классифицированное)
  событие в SignalR. При **Serverless SignalR** — через output binding `[SignalROutput(HubName="attacks")]`
  с `Target="attack"`. При **Default mode** — через `Microsoft.Azure.SignalR.Management` ServiceManager.
- Throttle на пиках: окно ~250 мс/буфер, чтобы не залить клиента (см. §11 плана).
- Negotiate: при Serverless добавить в API `GET /hubs/attacks/negotiate` (SignalR binding) —
  иначе клиент `withUrl('/hubs/attacks')` не получит токен. Согласовать с `signalr.ts`.

**Подключить фронт:** `startAttackHub()` сейчас не вызывается. В `LiveFeedPage` и `AttackMapPage`
поднять соединение в `useEffect`, прокидывать события в Zustand-стор и в карту/ленту.

### 6.4 `/api/actors` + `/api/actors/{id}` (Неделя 5–6)
Файл: `src/HoneyGrid.Api/Features/Actors/ActorEndpoints.cs` (новый).

- `GET /api/actors` → `ThreatActor[]` (без тяжёлого `dossier`/`fingerprint` — список) — `openapi.yaml:621`.
- `GET /api/actors/{id}` → `ThreatActor` с `dossier` + `fingerprint`.
- Источник: контейнер `actors` (PK `/id`), который наполняет процессор корреляции (§7.2).

**Готовность §6:** все 4 группы эндпоинтов отдают данные в форме `openapi.yaml`;
фронт переключается с MSW на реальный API сменой `VITE_API_BASE`; SignalR гонит события на дашборд.

---

## 7. Aggregates + AI Threat Actor Profiling

### 7.1 Timer-агрегатор (Неделя 4)
Файл: `src/HoneyGrid.Functions/Functions/BuildAggregates.cs` (новый, TimerTrigger, напр. `0 */5 * * * *`).
- Считает overview/geo/credentials из `events` и **upsert** в `aggregates` с `/bucket` =
  `overview` | `geo` | `credentials` (+ время). TTL 30 дней (уже в контейнере).
- API §6.2 просто отдаёт эти документы — быстро и дёшево по RU.

### 7.2 Actor correlation + AI dossier (Неделя 5–6, killer-фича ③)
Файлы:
- `src/HoneyGrid.Functions/Functions/CorrelateActors.cs` (TimerTrigger, напр. раз в 15–30 мин)
- `src/HoneyGrid.Functions/Profiling/ActorFingerprint.cs` (построение отпечатка)
- `src/HoneyGrid.Functions/Profiling/ActorClustering.cs` (similarity-кластеризация)
- `src/HoneyGrid.Functions/Ai/DossierGenerator.cs` (OpenAI → `ActorDossier`)

Логика:
1. **Fingerprint сессии/IP:** порядок команд, тайминг попыток, набор использованных кредов,
   ASN/страна, типы сенсоров. Источник — `events` (PK `/attackerIp` уже исключает fan-out).
2. **Кластеризация в «актёров»:** similarity по отпечатку (Jaccard по командам/кредам +
   совпадение ASN + близость тайминга). Порог → один `actor`. Для coursework достаточно
   жадной кластеризации (без ML).
3. **Запись `actors`** (shape `ThreatActor` из `openapi.yaml:621`): `id`, сгенерированное имя
   (читаемое, напр. «Miedziany Skorpion»), `eventCount`, `firstSeen/lastSeen`, `countries`,
   `asns`, `sophistication` (среднее), `fingerprint`.
4. **AI-досье (OpenAI, `ActorDossier`):** по агрегированному отпечатку → `summary`, `archetype`,
   `goals`, `killChain[]`, `threatLevel`. Кэшировать: перегенерировать только при заметном
   приросте активности (экономия токенов).

**Тесты:** кластеризация (два похожих IP → один актёр; разные → разные), генерация имени
детерминирована по seed, парсинг досье-JSON устойчив к мусору.

**Готовность §7:** в `actors` появляются профили с досье; `ThreatActorsPage` рисует граф и карточки.

---

## 8. Observability (Неделя 7)
- App Insights connection string → Functions + API (через Bicep output → env).
- `Serilog` + `OpenTelemetry` (как в стеке плана) на API и Functions: трассировка
  Change Feed → классификатор → SignalR. Корреляция по `attackerIp`/`sessionId`.
- Метрики для демо: events/min, latency классификации, % fallback на stub, # актёров.

---

## 9. Frontend Track B — постранично

Каркас готов (Vite + React 18.2 + Tailwind v4 + shadcn + TanStack Query + Zustand +
MSW + SignalR-клиент). Хуки данных уже написаны в `api/queries.ts` (`useFeed`,
`useStatsOverview`, `useStatsGeo`, `useStatsCredentials`, `useActors`, `useActor`).
Всё делается на **MSW-моках**, переключение на реальный API — позже, без правок страниц.

Зависимости фронта, которые надо добавить (в `HoneyGrid.Web/package.json`):
`azure-maps-control` + `react-azure-maps` (globe), `react-globe.gl` (опц. 3D),
`@tanstack/react-virtual` (лента), `recharts` + `@visx/treemap` (графики/treemap),
`d3-force` (граф актёров), `framer-motion` (анимации). Образец готовой страницы —
`IocPage.tsx` (262 строки) и `SessionsPage.tsx`.

### 9.1 `LiveFeedPage` (Неделя 2) — частично есть, доделать
- Виртуализированный список (TanStack Virtual) последних событий.
- Источник: `useFeed()` для истории + **`startAttackHub(onAttack)`** в `useEffect` для realtime
  (prepend новых событий, дедуп по `id`). Статус соединения уже в `connectionStore`.
- Бейдж severity (`SeverityBadge` уже есть), фильтр по типу сенсора/категории.

### 9.2 `AttackMapPage` — Live Attack Globe (Неделя 4, killer-фича ②)
- Azure Maps через `react-azure-maps` (`AzureMapsProvider`/`DataSourceProvider`/`LayerProvider`),
  **AAD-auth** (ключ Maps не хардкодить — взять anon-token эндпоинт или AAD; SKU G2 уже в `ai.bicep`).
- `SymbolLayer` + `HeatMapLayer` по `useStatsGeo()`; realtime-апдейты из SignalR (новые точки).
- Опциональная вкладка 3D-глобуса (`react-globe.gl`) с дугами атак (цвет = категория,
  ≤150 активных дуг для WebGL-перфоманса).
- ⚠️ Azure Maps требует ключ/AAD — на моках рисовать со статичными гео-данными; интеграцию ключа
  оставить на момент, когда инфра выдаст token-эндпоинт.

### 9.3 `ThreatActorsPage` (Неделя 6, killer-фича ③)
- Граф на `d3-force`: узел = актёр (размер = `eventCount`), клик → карточка-досье.
- Данные: `useActors()` (список) + `useActor(id)` (досье/отпечаток в диалоге — `dialog.tsx` есть).
- Карточка: `summary`, `archetype`, `threatLevel` (бейдж), `killChain` (таймлайн), топ-команды/креды.

### 9.4 `CredentialsPage` (Неделя 6, killer-фича ⑤)
- Treemap (`@visx/treemap`) по топ-логинам/паролям/парам из `useStatsCredentials()`.
- Тренды (Recharts line/bar), инсайт-карточки (доля совпадений с топ-паттернами утечек).
- Таблица топ-пар login:password (`table.tsx` уже есть).

### 9.5 `DashboardPage` (Неделя 2 → дополняется) — главный обзор
- KPI-карточки (`useStatsOverview`): всего событий, уник. IP, стран, активных актёров.
- Мини-таймсерия атак, severity-распределение, последние события (срез ленты),
  мини-карта (ссылка на globe). Связывает остальные модули.

**Готовность §9:** все 5 страниц работают на MSW; `npm run dev` без backend показывает
живой дашборд; тесты (`vitest`) проходят (есть `app.smoke.test.tsx`, `ioc.test.tsx`).

---

## 10. Таймлайн Track B (8 недель)

| Нед. | Backend / Infra | Frontend | Killer |
|---|---|---|---|
| 0 | (совместно) репо, Contracts, Cosmos, Bicep-скелет, OpenAPI — ✅ уже сделано | каркас, MSW — ✅ | — |
| 1 | SignalR Service в Bicep (§4.1); проверить Functions host (§4.2); RBAC ролей | DashboardPage скелет + KPI на моках | — |
| 2 | `/api/feed` (моки→backend); SignalR Hub broadcast + Change Feed (realtime E2E §6.3) | LiveFeedPage + привязка `startAttackHub` | — |
| 3 | **Stub-классификатор** (§5 шаг 1) — разблокирует Track A; `/api/stats/overview` | графики overview (Recharts) | — |
| 4 | Timer-агрегатор (§7.1); `/api/stats/geo`+`/credentials`; Maps token-эндпоинт | **Live Globe** (Azure Maps) | ② |
| 5 | **AI-классификатор OpenAI** (§5 шаг 2); `/api/actors` | — | — |
| 6 | Actor correlation + AI dossier (§7.2); Daily briefing (опц.) | ThreatActorsPage (d3-force) + CredentialsPage (treemap) | ③ ⑤ |
| 7 | Observability (§8); throttle/output caching; 3D-глобус backend готовность | Globe 3D-вкладка; полировка графиков | ② |
| 8 | (совместно) интеграция A+B, нагрузочное, метрики, репетиция демо | UX-шлиф, переключение MSW→реальный API | — |

> Сенсоры Track A задеплоить как можно раньше — к защите накопятся реальные данные,
> на которых классификатор и профилирование смотрятся убедительнее.

---

## 11. Риски и решения

| Риск | Решение |
|---|---|
| Межпроцессный SignalR (функция ≠ API) | Serverless SignalR + output binding `[SignalROutput]`; negotiate в API (§4.1/§6.3) |
| Несовпадение имени события (`attack` vs `attackReceived`) | Зафиксировать `"attack"` (фронт уже на нём); поправить `AttackHub.cs` |
| Метки Track C/D в коде путают нарезку | Это всё Track B; заменить метки по ходу |
| STIX/Replay по README числятся за B | По коду и детальному плану — это Track A; снять разночтение устно |
| OpenAI 429 / невалидный JSON | Polly backoff + `ParseJsonSafe` fallback на stub-значения |
| Зависимость A от моей классификации | Stub-классификатор на Неделе 3 разблокирует A немедленно |
| Azure Maps требует ключ/AAD | На моках статичная гео; token-эндпоинт интегрировать на Неделе 4 |
| RU/s на агрегатах | Предрасчёт в `aggregates` (Timer) + output caching на API |
| React.StrictMode ломает терминал/глобус | Для globe/3D — те же предосторожности; StrictMode выборочно |

---

## 12. Чеклист готовности Track B «на 5»

- [ ] SignalR Service в Bicep + роли; деплой `dev` проходит
- [ ] `/api/feed`, `/api/stats/{overview,geo,credentials}`, `/api/actors`, `/api/actors/{id}` отдают контракт `openapi.yaml`
- [ ] Change Feed → SignalR: событие появляется на дашборде < 1 c без рефреша
- [ ] Stub-классификатор (Неделя 3) + реальный OpenAI-классификатор (Неделя 5) с fallback
- [ ] Actor profiling: `actors` с AI-досье; граф + карточки на фронте
- [ ] 5 страниц Track B работают на MSW и на реальном API (смена `VITE_API_BASE`)
- [ ] 3 killer-фичи (Globe, Actor Profiling, Credential Intelligence) демонстрируемы
- [ ] Unit-тесты: классификатор, кластеризация, API-проекции, фронт vitest — зелёные
- [ ] Observability: трассировка пайплайна в App Insights, метрики для демо
- [ ] README/нарезка согласованы с товарищем (STIX/Replay = A)

---

## 13. Первые шаги (эта неделя)

1. **Снять разночтения с товарищем** (15 мин): STIX/Replay = Track A; имя SignalR-события = `"attack"`; режим SignalR (рекомендую Serverless + binding).
2. **Bicep:** добавить `Microsoft.SignalRService/signalR` в `app.bicep` + роли; задеплоить `dev`.
3. **Backend:** `FeedEndpoints.cs` (`/api/feed`) по образцу `StixEndpoints.cs`; зарегистрировать в `Program.cs`.
4. **Realtime:** дополнить `AttackHub.cs`; CosmosDBTrigger-функция `FanOutToSignalR`; подключить `startAttackHub()` в `LiveFeedPage`.
5. **Frontend:** довести `DashboardPage` и `LiveFeedPage` на моках (KPI + виртуальная лента).
6. **Stub-классификатор** в `HoneyGrid.Functions` (CosmosDBTrigger → PATCH `classification`) — отдать Track A как можно раньше.
