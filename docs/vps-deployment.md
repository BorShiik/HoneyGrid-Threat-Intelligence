# Развёртывание сенсоров HoneyGrid на внешнем VPS

Развёртывание сенсоров HoneyGrid на дешёвых VPS (DigitalOcean, Hetzner, Senko Digital) позволяет получать **реальные IP-адреса** атакующих для чистого TCP-трафика (SSH, Telnet, RDP), обходя ограничения NAT в Azure Container Apps.

> [!IMPORTANT]
> Сенсоры (`cowrie`, `tcp-listener`, `web-honeypot`) — stateless. Вся логика и база остаются в Azure. Сенсоры на VPS только слушают порты и шлют события в твой Azure Event Hubs, а бинарные записи TTY — в Blob Storage (для Session Replay).

Готовые файлы развёртывания лежат в каталоге [`sensors/`](../sensors):
- [`sensors/docker-compose.vps.yml`](../sensors/docker-compose.vps.yml) — описание контейнеров;
- [`sensors/.env.example`](../sensors/.env.example) — шаблон конфигурации/секретов.

---

## Шаг 1: Сервисный аккаунт (Service Principal) в Azure

VPS не в Azure, поэтому Managed Identity «из коробки» недоступна — нужен Service Principal:

1. В **Azure Cloud Shell** (или там, где работает `az`) выполни:
   ```bash
   az ad sp create-for-rbac --name "HoneyGrid-External-Sensors"
   ```
2. Сохрани из вывода три значения:
   - `appId`    → `AZURE_CLIENT_ID`
   - `password` → `AZURE_CLIENT_SECRET`
   - `tenant`   → `AZURE_TENANT_ID`

> [!WARNING]
> `password` показывается **один раз**. Секрет по умолчанию истекает через 1 год — после защиты/демо его стоит ротировать (`az ad sp credential reset`).

---

## Шаг 2: Выдача прав сервисному аккаунту

Service Principal-у нужны **три** роли. Подставь свой `appId` (из шага 1), `id-подписки` и имена ресурсов.

```bash
APP_ID="<твой-appId>"
SUB="<id-подписки>"
RG="hg-dev-rg"
ACR="hgdevacrjaugmcd2wlrx2"            # имя ACR (без .azurecr.io)
EHNS="hg-dev-ehns-jaugmcd2wlrx2"        # namespace Event Hubs
STORAGE="hgdevstjaugmcd2wlrx2"          # имя Storage-аккаунта (для TTY)

# 1) Читать Docker-образы из приватного ACR (AcrPull)
az role assignment create \
  --assignee "$APP_ID" \
  --role "AcrPull" \
  --scope "/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.ContainerRegistry/registries/$ACR"

# 2) Писать события в Event Hubs (Azure Event Hubs Data Sender)
az role assignment create \
  --assignee "$APP_ID" \
  --role "Azure Event Hubs Data Sender" \
  --scope "/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.EventHub/namespaces/$EHNS"

# 3) Загружать записи TTY в Blob Storage (Storage Blob Data Contributor)
#    Нужно только для Session Replay. Если TTY не нужен — пропусти и оставь
#    HONEYGRID_BLOB_ENDPOINT пустым в .env.
az role assignment create \
  --assignee "$APP_ID" \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.Storage/storageAccounts/$STORAGE"
```

> [!NOTE]
> Раньше в команде AcrPull стоял захардкоженный GUID вместо `appId` — это была ошибка. Во **всех трёх** командах `--assignee` должен быть **один и тот же** `appId` твоего Service Principal.

---

## Шаг 3: Подготовка VPS

Подойдёт Ubuntu 22.04/24.04 (хватит тарифа ~4–5 €/мес).

1. **Перевесь настоящий SSH на другой порт** — Cowrie занимает порт 22:
   ```bash
   sudo sed -i 's/^#\?Port .*/Port 2222/' /etc/ssh/sshd_config
   sudo systemctl restart ssh
   # ВАЖНО: не закрывай текущую сессию. В НОВОМ терминале проверь вход:
   #   ssh root@<ip-vps> -p 2222
   ```

2. **Установи Docker + Compose-плагин**:
   ```bash
   curl -fsSL https://get.docker.com -o get-docker.sh
   sudo sh get-docker.sh
   sudo apt-get install -y docker-compose-plugin
   ```

3. **Авторизуй Docker в ACR** (тем же Service Principal):
   ```bash
   docker login hgdevacrjaugmcd2wlrx2.azurecr.io -u "<твой-appId>" -p "<твой-password>"
   ```

---

## Шаг 4: Конфигурация и запуск

Скопируй на VPS каталог `sensors/` (или хотя бы `docker-compose.vps.yml` и `.env.example`), затем:

1. **Создай `.env` из шаблона и заполни значениями из шагов 1–2:**
   ```bash
   cd sensors
   cp .env.example .env
   nano .env
   chmod 600 .env          # секрет читает только root
   ```

   `.env` уже в `.gitignore` — секрет остаётся на VPS и не попадает в репозиторий. Сам `docker-compose.vps.yml` берёт все секреты из `.env` (через `env_file`), поэтому в нём нет паролей открытым текстом.

2. **Запусти сенсоры:**
   ```bash
   docker compose -f docker-compose.vps.yml up -d
   ```

3. **Проверь, что всё поднялось и шлёт данные:**
   ```bash
   docker compose -f docker-compose.vps.yml ps
   docker compose -f docker-compose.vps.yml logs -f cowrie-shipper
   ```
   В логах шиппера должно появиться `EventHubShipper połączony bezkluczowo ...`. Первое же подключение бота на порт 22/23/80/3389 даст событие на дашборде.

---

## Как это работает

Контейнеры скачиваются из приватного ACR, занимают порты `22`, `23`, `80`, `443`, `3389` и слушают интернет. Любой бот, постучавшийся на VPS, логируется, а его **реальный IP** уходит в Azure Event Hubs через Service Principal — на дашборде сразу вспыхивают новые гео-точки.

Для интерактивных SSH-сессий Cowrie пишет бинарную запись TTY на общий том `cowrie-logs`; сайдкар `cowrie-shipper` загружает её в Blob Storage, и сессию можно проиграть на странице **Сессии (replay)**.

### Важные детали конфигурации

- **Том `cowrie-logs` у шиппера монтируется на запись (RW), а не `:ro`.** Сайдкар создаёт каталог `/var/log/cowrie/tty` с правами `0777` до того, как Cowrie запишет первую запись: на свежем томе Cowrie сам его не создаёт и падает с `PermissionError`/`FileNotFoundError`. С `:ro` записи TTY не появятся и Session Replay будет пустым.
- **`CowrieShipper__BlobServiceUri`** (переменная `HONEYGRID_BLOB_ENDPOINT` в `.env`) обязателен для Session Replay. Если оставить пустым — события (Live Feed, карта, аналитика) работают, но без проигрывания TTY.
- **Порт 443 отдаёт обычный HTTP** (без TLS). Для honeypot это норма: цель — зафиксировать IP сканера, а не корректный TLS-handshake.
- **TCP-листенер слушает только 3389.** Порт 23 (Telnet) обслуживает Cowrie, поэтому в `tcp-listener` он отключён (`TcpSensor__Ports__0=3389`), чтобы не дублировать.

---

## Безопасность

- Файл `.env` с секретом Service Principal держи на VPS с правами `600`; никогда не коммить его.
- У Service Principal минимум прав: `AcrPull` (чтение образов), `Event Hubs Data Sender` (только отправка), `Storage Blob Data Contributor` (запись TTY). Ни одна из ролей не даёт доступа к удалению ресурсов или чтению данных платформы.
- После защиты/демо ротируй секрет: `az ad sp credential reset --id <appId>`.
- Реальный SSH перевешен на 2222 и (желательно) ограничен по ключам/файрволу — порт 22 целиком отдан honeypot-у.
