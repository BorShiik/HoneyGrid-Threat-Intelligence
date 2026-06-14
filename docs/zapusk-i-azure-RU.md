# HoneyGrid — запуск проекта и подключение к Azure (Track B)

Пошаговая инструкция: как запустить проект локально и как развернуть его в Azure —
какие сервисы создавать, что вписывать, куда нажимать на портале. Фокус — Track B
(дашборд, API, realtime, классификатор, профилирование), но покрыт весь деплой.

> Серьёзное предупреждение про деньги и регион:
> - Часть сервисов платная (Container Apps, Log Analytics, SignalR Standard, OpenAI).
>   Для учебного проекта выбирай самые дешёвые SKU (они уже стоят в Bicep) и **удали
>   ресурсы после защиты** (см. раздел 9).
> - На студенческих подписках (Azure for Students) регионы и Azure OpenAI часто
>   ограничены. Если деплой падает по региону/квоте — меняй регион (`swedencentral`
>   обычно ок для OpenAI) или временно отключай Track B-сервисы.

---

## 0. Что вообще нужно (один раз)

Аккаунты и доступы:
- Учётная запись Azure с активной подпиской (Azure for Students подходит).
- Права «Owner» или «Contributor + User Access Administrator» на подписке/группе
  ресурсов — потому что мы раздаём роли (RBAC). Без права назначать роли keyless-доступ
  не настроить.

Инструменты на твоём компьютере (Windows):
- **.NET SDK 10** — `winget install Microsoft.DotNet.SDK.Preview` (или с сайта dotnet).
  Проверка: `dotnet --version`.
- **Node.js 24** — `winget install OpenJS.NodeJS`. Проверка: `node --version`, `npm --version`.
- **Azure CLI** — `winget install Microsoft.AzureCLI`. Проверка: `az version`.
- **Bicep** — `az bicep install`.
- **Azure Functions Core Tools v4** — `npm i -g azure-functions-core-tools@4 --unsafe-perm true`.
  Проверка: `func --version`.
- (Опционально) **Azurite** (эмулятор Storage для локального запуска функций) —
  `npm i -g azurite`.
- Docker НЕ обязателен: образы контейнеров мы собираем прямо в облаке через `az acr build`.

Войти в Azure из терминала:
```bash
az login
az account set --subscription "<ИМЯ ИЛИ ID ПОДПИСКИ>"
az account show   # проверить, что выбрана нужная подписка
```

---

## 1. Запуск локально (без Azure)

Проект устроен так, что фронтенд работает полностью на моках (MSW) и не требует бэкенда.
Это самый быстрый способ увидеть дашборд.

### 1.1. Только фронтенд (дашборд на моках) — 2 минуты
```bash
cd src/HoneyGrid.Web
npm ci
npm run dev
```
Открой адрес из консоли (обычно http://localhost:5173). Увидишь все страницы Track B:
пульт, карту атак, живой поток, актёров, поσвiadczenia. Живой поток и карта работают
через встроенный симулятор (в dev WebSocket-бэкенд не нужен).

Тесты и сборка фронта:
```bash
npm test          # vitest
npm run build     # tsc + vite build (производственная сборка)
```

### 1.2. Сборка и тесты бэкенда (.NET)
```bash
# из корня репозитория
dotnet build HoneyGrid.sln
dotnet test HoneyGrid.sln
# только мои Track B-тесты (классификатор/агрегаты/профили актёров):
dotnet test tests/HoneyGrid.Functions.Tests
```

### 1.3. Запуск API локально
API читает Cosmos/Blob бесключево. Локально проще указать на облачный Cosmos
(после раздела 2) или поднять эмулятор Cosmos. Минимально:
```bash
cd src/HoneyGrid.Api
# задать эндпоинты (PowerShell):
$env:HoneyGrid__CosmosEndpoint = "https://<твой-cosmos>.documents.azure.com:443/"
$env:HoneyGrid__CosmosDatabase = "honeygrid"
dotnet run
# проверка:
# http://localhost:5xxx/health  и  http://localhost:5xxx/api/feed
```
Если Cosmos недоступен — эндпоинты вернут пустые данные (а не ошибку), это by design.

### 1.4. Запуск функций локально
```bash
cd src/HoneyGrid.Functions
copy local.settings.sample.json local.settings.json   # и впиши значения
# нужен Azurite в отдельном окне (AzureWebJobsStorage=UseDevelopmentStorage=true):
azurite
func start
```
Без `OpenAIEndpoint` классификатор работает в режиме stub. Без `AzureSignalRConnectionString__serviceUri`
функции SignalR не поднимутся — для чисто локальной классификации это нормально,
просто не используй FanOut/negotiate локально.

---

## 2. Развёртывание в Azure — РЕКОМЕНДУЕМЫЙ путь (Bicep, одной командой)

В репозитории уже есть готовая инфраструктура как код: `infra/bicep/`. Она создаёт
группу ресурсов `hg-<env>-rg` и все сервисы. Это надёжнее ручного кликанья.

### 2.1. Выбрать регион
Открой `infra/bicep/main.dev.bicepparam` и при необходимости поменяй `location`
на разрешённый твоей подпиской (для Track B/OpenAI — `swedencentral`).
Чтобы включить Track B-сервисы (OpenAI, Maps, Static Web App), поставь:
```bicep
param deployTrackB = true
```

### 2.2. Проверка и деплой
```bash
az bicep build --file infra/bicep/main.bicep              # синтаксис
az deployment sub what-if \                                # предпросмотр
  --location swedencentral \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam

az deployment sub create \                                 # сам деплой
  --name honeygrid-dev \
  --location swedencentral \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```
После успеха в группе `hg-dev-rg` появятся: Cosmos (serverless, БД `honeygrid`, 5
контейнеров), Storage, Event Hubs, Service Bus, Container Apps Environment, ACR,
App Insights, Log Analytics + Sentinel, а при `deployTrackB=true` — Azure OpenAI
(деплой `gpt-4o-mini`), Azure Maps, Static Web App.

> Чего ещё НЕТ в Bicep и что делаем руками ниже: **Azure SignalR Service** (он добавлен
> в `app.bicep` — проверь, что задеплоился), **Function App** (хост для функций Track B)
> и назначение ролей для его identity. См. разделы 4–6.

После Bicep сразу переходи к разделу 5 (сборка/деплой кода) и 6 (роли). Раздел 3 —
это альтернатива «через портал», если Bicep по какой-то причине не идёт.

---

## 3. Альтернатива: создание сервисов Track B вручную через портал

Делай это, только если Bicep не подходит. Порядок важен (одни сервисы зависят от других).
Везде: «Create a resource» = синяя кнопка «+ Создать ресурс» вверху слева на portal.azure.com.

Общая преамбула — группа ресурсов:
1. Портал → строка поиска вверху → «Resource groups» → «+ Create».
2. Subscription: твоя; Resource group: `hg-dev-rg`; Region: `Sweden Central` → «Review + create» → «Create».

### 3.1. Cosmos DB (serverless) + контейнеры
1. + Create a resource → поиск «Azure Cosmos DB» → «Create» → вариант «Azure Cosmos DB for NoSQL» → «Create».
2. Basics: Resource group `hg-dev-rg`; Account Name `hg-dev-cosmos-<любой-суффикс>`;
   Region `Sweden Central`; Capacity mode → **Serverless**.
3. (Вкладка Networking) пока «All networks» — упростит отладку (потом можно закрыть).
4. «Review + create» → «Create». Ждать ~5 мин.
5. После создания: ресурс Cosmos → слева «Data Explorer» → «New Database» → id `honeygrid`.
6. Создать 5 контейнеров (New Container) с такими Partition key:
   - `events` → `/attackerIp`
   - `actors` → `/id`
   - `sessions` → `/sessionId`
   - `iocs` → `/type`
   - `aggregates` → `/bucket`
   (плюс служебный для функций создастся сам: `leases`).
7. Включить keyless: Cosmos → «Settings» → найти «Disable local authentication» (или через CLI:
   `az cosmosdb update -n <acc> -g hg-dev-rg --disable-key-based-metadata-write-access true`).
   Доступ дадим через роли в разделе 6.

### 3.2. Azure OpenAI + модель
1. + Create → «Azure OpenAI» → «Create». Region — где доступен OpenAI (напр. `Sweden Central`).
   Name `hg-dev-openai`; Pricing tier `Standard S0`. «Review + create» → «Create».
2. После создания: ресурс → «Go to Azure AI Foundry portal» (или «Model deployments» → «Manage Deployments»).
3. «+ Deploy model» → выбрать `gpt-4o-mini` → Deployment name оставить `gpt-4o-mini` → Deploy.
4. Запиши **Endpoint** (вид `https://hg-dev-openai.openai.azure.com/`) — он пойдёт в настройки функций (`OpenAIEndpoint`).

### 3.3. Azure Maps (для карты атак)
1. + Create → «Azure Maps» → «Create». Name `hg-dev-maps`; Pricing tier `Gen2`. Create.
2. Для фронтенда нужна AAD-аутентификация карты (без ключей в коде). На демо можно временно
   взять Shared Key: Maps → «Authentication» → скопировать «Primary Key» и пробросить
   как `VITE_AZURE_MAPS_KEY` при сборке фронта. (Карта в проекте работает и на статичных
   гео-данных, ключ нужен только для подложки Azure Maps.)

### 3.4. Azure SignalR Service (realtime, режим Serverless)
1. + Create → «SignalR Service» → «Create».
2. Name `hg-dev-signalr`; Region `Sweden Central`; Pricing tier `Free` (хватит для демо).
3. **Service mode → Serverless** (это критично: наш FanOut/negotiate рассчитаны на Serverless).
4. (Networking) оставь публичный доступ; (Settings → CORS) добавь Allowed origins: `*`
   (или конкретный адрес SWA).
5. Create. Запиши **URL/hostname** (вид `https://hg-dev-signalr.service.signalr.net`).

### 3.5. Application Insights
1. + Create → «Application Insights» → Name `hg-dev-appi`; Resource Mode «Workspace-based»;
   выбрать/создать Log Analytics workspace. Create.
2. Скопировать «Connection String» — пойдёт в функции (`APPLICATIONINSIGHTS_CONNECTION_STRING`).

### 3.6. Container Registry (ACR) + Container Apps Environment
1. + Create → «Container Registry» → Name `hgdevacr<суффикс>` (без дефисов); SKU `Basic`. Create.
2. + Create → «Container Apps Environment» (или создастся при создании Container App) →
   Name `hg-dev-cae`; Region `Sweden Central`. Create.

### 3.7. Storage (для функций) + Static Web App
1. + Create → «Storage account» → Name `hgdevstor<суффикс>`; Performance Standard; Redundancy LRS. Create.
   (нужно для `AzureWebJobsStorage` функций).
2. Static Web App создадим в разделе 5.4 (привязка к репозиторию/CI).

---

## 4. Создать Function App (хост функций Track B)

Функции Track B (ClassifyEvents, FanOutToSignalR, BuildAggregates, CorrelateActors,
negotiate) разворачиваются в Azure Functions (.NET isolated).

Через портал:
1. + Create → «Function App» → вариант **«Consumption»** (или «Flex Consumption»).
2. Basics: Resource group `hg-dev-rg`; Function App name `hg-dev-func`;
   Runtime stack **.NET**, Version **10 (isolated)**; Region `Sweden Central`;
   Operating System **Linux**.
3. Hosting: Storage account → выбрать `hgdevstor...` из 3.7.
4. Monitoring: Application Insights → выбрать `hg-dev-appi`.
5. «Review + create» → «Create».
6. После создания включи **системную управляемую identity**: Function App →
   «Settings → Identity» → System assigned → Status **On** → Save. Эта identity получит роли в разделе 6.

CLI-эквивалент:
```bash
az functionapp create -g hg-dev-rg -n hg-dev-func \
  --storage-account hgdevstor<суффикс> \
  --consumption-plan-location swedencentral \
  --runtime dotnet-isolated --runtime-version 10 --functions-version 4 \
  --app-insights hg-dev-appi --os-type Linux
az functionapp identity assign -g hg-dev-rg -n hg-dev-func
```

---

## 5. Сборка и деплой кода

### 5.1. Образ API → ACR → Container App
```bash
# собрать образ прямо в облаке (Docker локально не нужен):
az acr build --registry hgdevacr<суффикс> \
  --image honeygrid-api:latest \
  --file src/HoneyGrid.Api/Dockerfile .

# создать Container App для API (если не создан Bicep'ом):
az containerapp create -g hg-dev-rg -n hg-dev-ca-api \
  --environment hg-dev-cae \
  --image hgdevacr<суффикс>.azurecr.io/honeygrid-api:latest \
  --ingress external --target-port 8080 \
  --registry-server hgdevacr<суффикс>.azurecr.io \
  --system-assigned
```
Затем задать настройки приложения (Container App → «Settings → Containers → Environment variables»,
или CLI `az containerapp update --set-env-vars ...`):
```
HoneyGrid__CosmosEndpoint   = https://hg-dev-cosmos-...documents.azure.com:443/
HoneyGrid__CosmosDatabase   = honeygrid
HoneyGrid__BlobServiceUri   = https://hgdevstor....blob.core.windows.net
HoneyGrid__SignalREndpoint  = https://hg-dev-signalr.service.signalr.net
ASPNETCORE_URLS             = http://+:8080
```
(API только ЧИТАЕТ Cosmos — роль Data Reader, см. раздел 6.)

### 5.2. Деплой функций
```bash
cd src/HoneyGrid.Functions
func azure functionapp publish hg-dev-func
```
Затем настройки приложения функции (Function App → «Settings → Environment variables» →
вкладка «App settings», добавлять по одной «+ Add»):
```
FUNCTIONS_WORKER_RUNTIME              = dotnet-isolated
CosmosDatabase                        = honeygrid
CosmosConnection__accountEndpoint     = https://hg-dev-cosmos-...documents.azure.com:443/
OpenAIEndpoint                        = https://hg-dev-openai.openai.azure.com/
OpenAIDeployment                      = gpt-4o-mini
AzureSignalRConnectionString__serviceUri = https://hg-dev-signalr.service.signalr.net
APPLICATIONINSIGHTS_CONNECTION_STRING = <строка из 3.5>
```
> Имена с `__` (двойное подчёркивание) важны — так Azure отображает вложенную
> конфигурацию (identity-based соединения). Не переименовывай.

### 5.3. CORS на Function App (чтобы браузер мог звать negotiate)
Function App → «Settings → CORS» → добавить адрес фронтенда (URL Static Web App,
например `https://hg-dev-swa.azurestaticapps.net`) → Save.

### 5.4. Фронтенд → Static Web App
Вариант A (через портал + GitHub, рекомендуется):
1. + Create → «Static Web App». Name `hg-dev-swa`; Plan `Free`.
2. Source `GitHub` → авторизоваться → выбрать репозиторий и ветку `main`.
3. Build details: Build Presets `Custom`; App location `src/HoneyGrid.Web`;
   Output location `dist`. Create. Azure заведёт GitHub Action, который соберёт и
   задеплоит фронт автоматически.
4. Environment variables (SWA → «Settings → Environment variables») для прод-сборки:
   ```
   VITE_SIGNALR_URL = https://hg-dev-func.azurewebsites.net/api/hubs/attacks
   ```
   (Это базовый URL negotiate; клиент сам добавит `/negotiate`.)
5. Чтобы `/api/*` фронта попадал в наш Container App API, привяжи бэкенд:
   SWA → «APIs» → «Link» → выбрать Container App `hg-dev-ca-api`. Тогда вызовы
   `/api/feed`, `/api/stats/*`, `/api/actors` идут на API на том же origin.

Вариант B (вручную, без GitHub): собери локально `npm run build` в `src/HoneyGrid.Web`
и задеплой папку `dist` через `swa deploy` (Azure Static Web Apps CLI) с deployment-токеном
из портала (SWA → «Manage deployment token»).

---

## 6. Роли (RBAC) — сердце бесключевой архитектуры

Без правильных ролей всё компилируется, но в рантайме «403/Unauthorized». Каждой
managed identity нужно выдать минимальные права. Делается так (одинаково для каждой роли):

Портал-путь: открыть КОНКРЕТНЫЙ ресурс (Cosmos/OpenAI/SignalR/...) → слева
«Access control (IAM)» → «+ Add» → «Add role assignment» → выбрать роль → «Next» →
Assign access to «Managed identity» → «+ Select members» → выбрать identity нужного
приложения (hg-dev-ca-api / hg-dev-func) → «Select» → «Review + assign».

Матрица ролей Track B:

| Ресурс | Identity | Роль |
|---|---|---|
| Cosmos DB | Function App (`hg-dev-func`) | **Cosmos DB Built-in Data Contributor** (чтение+запись) |
| Cosmos DB | API (`hg-dev-ca-api`) | **Cosmos DB Built-in Data Reader** (только чтение) |
| Azure OpenAI | Function App | **Cognitive Services OpenAI User** |
| SignalR Service | Function App | **SignalR Service Owner** |
| Storage account | Function App | **Storage Blob Data Owner** (для AzureWebJobsStorage/чекпойнтов) |
| Container Registry | Container App API | **AcrPull** |

> Важная тонкость: роли «Cosmos DB Built-in Data …» — это роли ПЛОСКОСТИ ДАННЫХ Cosmos,
> их НЕ видно в обычном IAM. Назначай их через CLI:
> ```bash
> # Data Contributor для функций:
> az cosmosdb sql role assignment create -g hg-dev-rg -a hg-dev-cosmos-<…> \
>   --role-definition-id 00000000-0000-0000-0000-000000000002 \
>   --principal-id <objectId identity функции> \
>   --scope "/"
> # Data Reader для API:
> az cosmosdb sql role assignment create -g hg-dev-rg -a hg-dev-cosmos-<…> \
>   --role-definition-id 00000000-0000-0000-0000-000000000001 \
>   --principal-id <objectId identity API> \
>   --scope "/"
> ```
> objectId identity берётся: ресурс приложения → «Identity → System assigned → Object (principal) ID».

Остальные роли (OpenAI/SignalR/Storage/ACR) — обычные Azure RBAC, их можно через портал
(IAM, как описано выше) или CLI `az role assignment create --assignee <objectId> --role "<имя роли>" --scope <resourceId>`.

---

## 7. Порядок «правильного» прохода (чек-лист деплоя)

1. `az login`, выбрать подписку.
2. Bicep-деплой `hg-dev-rg` (раздел 2) ИЛИ вручную сервисы (раздел 3).
3. Создать SignalR Service (Serverless) — если не из Bicep.
4. Создать Function App + включить identity (раздел 4).
5. `az acr build` образ API; создать Container App API; задать env (5.1).
6. `func azure functionapp publish` функции; задать env (5.2); CORS (5.3).
7. Создать Static Web App, env `VITE_SIGNALR_URL`, привязать API (5.4).
8. Раздать роли RBAC (раздел 6).
9. Проверить (раздел 8).

---

## 8. Проверка, что всё живо

- API: открыть `https://<ca-api>.<region>.azurecontainerapps.io/health` → `ok`.
- Feed: `.../api/feed` → JSON-массив (пустой, если событий ещё нет — это норм).
- Функции: Function App → «Functions» → видны ClassifyEvents, FanOutToSignalR,
  BuildAggregates, CorrelateActors, negotiate. Вкладка «Monitor» у каждой — логи.
- Realtime: открыть дашборд (SWA) → страница «Strumień na żywo». Если в Cosmos
  пишутся события (Track A с сенсорами или вручную), они появятся без обновления.
  В dev/без бэкенда поток идёт от встроенного симулятора — индикатор покажет
  «(tryb demonstracyjny)».
- Классификатор: добавь тестовое событие в контейнер `events` (Data Explorer →
  New Item, минимально `id`, `attackerIp`, `sensorId`, `timestamp`, `eventType`) →
  через секунды у документа появится поле `classification` (от OpenAI или stub).
- Агрегаты: контейнер `aggregates` → через ~5 мин появятся документы `overview`/`geo`/`credentials`.
- Актёры: контейнер `actors` → через ~30 мин (таймер) появятся профили; видны на
  странице «Aktorzy zagrożeń».

Где смотреть ошибки: Application Insights → «Logs» (KQL: `traces | order by timestamp desc`),
или у каждой функции вкладка «Monitor». У Container App: «Monitoring → Log stream».

---

## 9. Стоимость и удаление (ВАЖНО)

Что капает деньгами даже в простое: Container Apps (если minReplicas>0), Log Analytics
(приём логов), SignalR Standard (Free — бесплатно), OpenAI (по токенам — только при вызовах).
Сенсоры Track A (always-on контейнеры) — главный постоянный расход; для Track B-демо
их можно не держать.

Удалить всё одним движением после защиты:
```bash
az group delete --name hg-dev-rg --yes --no-wait
```
Проверь, что не осталось ресурсов вне группы (SWA/идентичности обычно внутри неё).

---

## 10. Частые проблемы

- **403 / Unauthorized из API или функций** → не выданы роли RBAC (раздел 6) или
  identity не включена. Проверь Object ID и роли плоскости данных Cosmos.
- **Деплой Bicep падает на модуле `ai`** → OpenAI недоступен в регионе/нет квоты:
  поставь регион `swedencentral` или `deployTrackB=false`, OpenAI добавь позже.
- **negotiate возвращает 500** → не задан `AzureSignalRConnectionString__serviceUri`
  или функции нет роли «SignalR Service Owner», или SignalR не в режиме Serverless.
- **Фронт не видит API** → не привязан бэкенд в SWA (5.4 п.5) или CORS; проверь, что
  `/api/feed` открывается с того же домена, что и дашборд.
- **Классификатор всегда stub** → не задан `OpenAIEndpoint`, либо нет роли
  «Cognitive Services OpenAI User», либо имя деплоя ≠ `OpenAIDeployment`.
- **CosmosDBTrigger не срабатывает** → не создан контейнер `leases` (создастся сам при
  первом запуске, если у identity есть право записи), либо у двух триггеров одинаковый
  префикс дзержав (у нас разные: `classify` и `fanout`).

---

Готово. Для учебной защиты достаточно: Bicep-деплой (раздел 2) + Function App (4) +
деплой кода (5) + роли (6) + проверка (8). Карта/OpenAI/SignalR — это «на пятёрку»,
но и без них базовый дашборд на моках уже демонстрируем (раздел 1.1).
