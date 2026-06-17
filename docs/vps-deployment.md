# Deploying HoneyGrid Sensors on External VPS

Развертывание сенсоров HoneyGrid на дешевых VPS (DigitalOcean, Hetzner, Senko Digital) позволяет получать реальные IP-адреса хакеров для чистого TCP трафика (SSH, Telnet, RDP), обходя ограничения NAT в Azure Container Apps.

> [!IMPORTANT]  
> Сенсоры (`cowrie`, `tcp-listener`, `web-honeypot`) являются stateless (без состояния). Вся логика и база данных остаются в Azure. Сенсоры на VPS только "слушают" порты и шлют события в твой Azure Event Hubs.

## Шаг 1: Создание сервисного аккаунта (Service Principal) в Azure

Поскольку VPS не находятся в Azure, они не могут использовать Managed Identity "из коробки". Нам нужен Service Principal:

1. Открой **Azure Cloud Shell** (или терминал, где работает `az`) и выполни команду:
   ```bash
   az ad sp create-for-rbac --name "HoneyGrid-External-Sensors"
   ```
2. Команда выдаст JSON. Сохрани из него эти 3 значения:
   - `appId` (это будет `AZURE_CLIENT_ID`)
   - `password` (это будет `AZURE_CLIENT_SECRET`)
   - `tenant` (это будет `AZURE_TENANT_ID`)

## Шаг 2: Выдача прав

Твоему новому Service Principal нужно два разрешения:

1. Читать образы Docker из твоего Azure Container Registry (ACR).
2. Писать события в Event Hubs.

Выполни в консоли (подставь свои имена ресурсов и `appId` из предыдущего шага):

```bash
# Даем право читать Docker образы (AcrPull)
az role assignment create \
  --assignee "7c964e11-067c-4fed-820e-459ed7c19bfd" \
  --role "AcrPull" \
  --scope "/subscriptions/<id-подписки>/resourceGroups/hg-dev-rg/providers/Microsoft.ContainerRegistry/registries/hgdevacrjaugmcd2wlrx2"

# Даем право писать в Event Hubs (Azure Event Hubs Data Sender)
az role assignment create \
  --assignee "<твой-appId>" \
  --role "Azure Event Hubs Data Sender" \
  --scope "/subscriptions/<id-подписки>/resourceGroups/hg-dev-rg/providers/Microsoft.EventHub/namespaces/<твой-eventhub-namespace>"
```

## Шаг 3: Настройка VPS

Купив сервер на Ubuntu 22.04/24.04 (подойдет даже тариф за 4.49 €), выполни базовую подготовку:

1. **Перенеси настоящий SSH на другой порт**.
   Cowrie нужен порт 22, поэтому настоящий SSH мы перевесим, например, на 2222:

   ```bash
   sudo sed -i 's/#Port 22/Port 2222/' /etc/ssh/sshd_config
   sudo systemctl restart ssh
   # ВАЖНО: Не закрывай текущую сессию, открой новый терминал и проверь,
   # что можешь зайти по порту 2222: ssh root@<ip-vps> -p 2222
   ```

2. **Установи Docker и Docker Compose**:

   ```bash
   curl -fsSL https://get.docker.com -o get-docker.sh
   sudo sh get-docker.sh
   sudo apt-get install docker-compose-plugin
   ```

3. **Авторизуй Docker в твоем ACR**:
   ```bash
   docker login hgdevacrjaugmcd2wlrx2.azurecr.io -u "<твой-appId>" -p "<твой-password>"
   ```

## Шаг 4: Запуск сенсоров (Docker Compose)

Создай файл `docker-compose.yml` на VPS:

```bash
nano docker-compose.yml
```

Вставь в него следующий код. **Обязательно замени плейсхолдеры** в блоке `x-azure-auth` и `HoneyGrid__EventHubFullyQualifiedNamespace`:

```yaml
version: "3.8"

# Общие переменные окружения для авторизации в Azure
x-azure-auth: &azure-auth
  AZURE_TENANT_ID: "e80a627f-ef94-4aa9-82d6-c7ec9cfca324"
  AZURE_CLIENT_ID: "<твой-appId>"
  AZURE_CLIENT_SECRET: "<твой-password>"
  HoneyGrid__EventHubFullyQualifiedNamespace: "hg-dev-ehns-jaugmcd2wlrx2.servicebus.windows.net"
  HoneyGrid__EventHubName: "honeypot-events"
  HoneyGrid__LocalLogOnly: "false"

services:
  # 1. SSH Honeypot (Cowrie)
  cowrie:
    image: hgdevacrjaugmcd2wlrx2.azurecr.io/honeygrid-cowrie:latest
    container_name: cowrie
    restart: always
    ports:
      - "22:2222" # Принимаем атаки на порту 22
      - "23:2223" # Telnet
    volumes:
      - cowrie-logs:/cowrie/cowrie-git/var/log/cowrie

  # 1.1 Шиппер для Cowrie (отправляет логи Cowrie в Azure)
  cowrie-shipper:
    image: hgdevacrjaugmcd2wlrx2.azurecr.io/honeygrid-cowrie-shipper:latest
    container_name: cowrie-shipper
    restart: always
    environment:
      <<: *azure-auth
      HoneyGrid__SensorId: "cowrie-vps-frankfurt"
      HoneyGrid__SensorType: "ssh"
      CowrieShipper__LogPath: "/var/log/cowrie/cowrie.json"
    volumes:
      - cowrie-logs:/var/log/cowrie:ro
    depends_on:
      - cowrie

  # 2. WEB Honeypot (Fake WP/Admin login)
  web-honeypot:
    image: hgdevacrjaugmcd2wlrx2.azurecr.io/honeygrid-web:latest
    container_name: web-honeypot
    restart: always
    ports:
      - "80:8080"
      - "443:8080"
    environment:
      <<: *azure-auth
      HoneyGrid__SensorId: "web-vps-frankfurt"
      HoneyGrid__SensorType: "web"

  # 3. TCP Listener (RDP, generic ports)
  tcp-listener:
    image: hgdevacrjaugmcd2wlrx2.azurecr.io/honeygrid-tcp:latest
    container_name: tcp-listener
    restart: always
    ports:
      - "3389:3389" # RDP Honeypot
    environment:
      <<: *azure-auth
      HoneyGrid__SensorId: "tcp-vps-frankfurt"
      HoneyGrid__SensorType: "tcp"

volumes:
  cowrie-logs:
```

Запусти всё одной командой:

```bash
docker compose up -d
```

## Как это работает?

Контейнеры скачаются из твоего приватного Azure Container Registry (ACR), запустятся на VPS, займут порты `22`, `80`, `443`, `3389` и начнут слушать интернет.
Любой бот, постучавшийся на VPS, будет залогинен, а его сырой **реальный IP-адрес** будет мгновенно отправлен в твой Azure Event Hubs через Service Principal. На дашборде сразу же вспыхнут новые гео-локации!
