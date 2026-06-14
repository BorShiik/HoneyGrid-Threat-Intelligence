# Tydzień 6 — SOAR i automatyczna mitygacja (auto-blokada atakującego)

W Tygodniu 5 detekcja zamieniła telemetrię honeypotów w **incydenty** Microsoft Sentinel
z mapowaniem MITRE ATT&CK i encjami (IP / Account / Host). Tydzień 6 domyka pętlę: incydent
przestaje czekać na operatora i **sam** uruchamia reakcję — playbook (Logic App) blokuje
atakujące IP na perymetrze (NSG dmz), dopisuje je do feedu **EDL**, powiadamia zespół
i komentuje/zamyka incydent. Całość jako kod (Bicep), odtwarzalna jedną komendą.

---

## 1. Cel

**SOAR** (Security Orchestration, Automation & Response): skrócić **MTTR**
(Mean Time To Respond) z minut/godzin pracy ręcznej do **sekund** automatyzacji — metryka
z §12 planu projektu. Operator nie loguje się do portalu i nie pisze reguły NSG ręcznie;
robi to playbook wyzwalany regułą automatyzacji Sentinela. MTTR jest **liczony** przez
playbook (od `createdTimeUtc` incydentu do momentu założenia blokady) i wpisywany w komentarz
incydentu — twardy dowód na obronę (§14.4).

---

## 2. Architektura i przepływ

```
        ┌─────────────────────────────────────────────────────────────────┐
        │  Tydzień 5: reguła analityczna Sentinela (KQL, rate-based)        │
        │  np. "brute-force z pojedynczego IP" → alert z encją IP           │
        └───────────────────────────────┬─────────────────────────────────┘
                                         │ grupowanie → INCYDENT
                                         ▼
        ┌─────────────────────────────────────────────────────────────────┐
        │  Incydent Sentinela (status New, encja IP = AttackerIp)           │
        └───────────────────────────────┬─────────────────────────────────┘
                                         │ trigger: incident created
                                         ▼
        ┌─────────────────────────────────────────────────────────────────┐
        │  Automation Rule (Microsoft.SecurityInsights/automationRules)     │
        │  → uruchom playbook 'hg-{env}-pb-block-ip'                         │
        └───────────────────────────────┬─────────────────────────────────┘
                                         │ Run playbook (przez API connection MSI)
                                         ▼
        ┌─────────────────────────────────────────────────────────────────┐
        │  Playbook — Logic App Consumption 'hg-{env}-pb-block-ip'          │
        │  tożsamość: UAMI hg-{env}-id-playbook (bezkluczowo, MSI)          │
        │  1. wyciągnij encję IP z incydentu                                │
        │  2. ▶ DODAJ regułę Deny 'Block-<ip>' do NSG dmz                   │  ◄── mitygacja
        │  3. ▶ DOPISZ IP do EDL: edl/blocked-ips.txt                       │  ◄── mitygacja
        │  4. ▶ POWIADOM: HTTP webhook (webhook.site / własny endpoint)     │
        │  5. ▶ KOMENTARZ + ZAMKNIĘCIE incydentu (z policzonym MTTR)        │
        └─────────────────────────────────────────────────────────────────┘
```

Efekt końcowy weryfikowalny bez portalu: w NSG `hg-{env}-nsg-dmz` pojawia się reguła
`Deny` o nazwie `Block-<ip>`, a IP trafia do `edl/blocked-ips.txt`. To „money shot"
sekcji 6 skryptu `verify-week6.sh`.

---

## 3. Komponenty wdrażane w Tygodniu 6

| Zasób | Typ | Rola |
|---|---|---|
| `hg-{env}-pb-block-ip` | `Microsoft.Web/workflows` (Logic App Consumption) | Playbook auto-mitygacji; stan **Enabled** |
| `hg-{env}-id-playbook` | `Microsoft.ManagedIdentity/userAssignedIdentities` | Tożsamość playbooka (MSI, bezkluczowo) |
| `hg-{env}-con-sentinel` | `Microsoft.Web/connections` | Konektor Sentinela dla playbooka, auth **MSI** |
| automation rule | `Microsoft.SecurityInsights/automationRules` | Wyzwalacz: incydent utworzony → Run playbook |
| kontener `edl` | blob w koncie storage | Feed EDL (`blocked-ips.txt`) |

---

## 4. Bezkluczowo (MSI) i zawężony RBAC

Playbook **nie** trzyma żadnego sekretu — działa na tożsamości `hg-{env}-id-playbook`
(User-Assigned Managed Identity). To domknięcie macierzy RBAC z Tygodnia 1; role są
**zawężone do minimum** (least privilege):

| Rola | GUID | Zakres | Po co |
|---|---|---|---|
| Microsoft Sentinel Responder | `3e150937-b8fe-4cfb-8069-0eaf05ecd056` | grupa zasobów (RG) | odczyt incydentu, komentarz, zamknięcie |
| Network Contributor | `4d97b98b-1d4f-4787-a291-c67834d212e7` | **tylko NSG `hg-{env}-nsg-dmz`** | dodanie reguły Deny |
| Storage Blob Data Contributor | `ba92f5b4-2d11-453d-a403-e96b0029c9fe` | konto storage | zapis EDL (`blocked-ips.txt`) |

> Kluczowa kontrola obrony: **Network Contributor jest na samym NSG**, a nie na całej RG.
> Sekcja 4 skryptu `verify-week6.sh` wprost to sprawdza (scope kończy się na
> `networkSecurityGroups/hg-{env}-nsg-dmz`) i zgłasza ❌, jeśli rola wisi na RG.

---

## 5. EDL jako mechanizm

**EDL** (External Dynamic List) to lista adresów odpytywana cyklicznie przez firewalle
perymetryczne (PAN-OS, FortiGate) po URL — bez restartu, bez ręcznej zmiany polityki.
Playbook dopisuje zablokowane IP do `edl/blocked-ips.txt`, więc demonstrujemy
mechanizm **bez sprzętu**: pokazujemy URL i jego zawartość.

- W demo blob jest **prywatny** (czytany przez nas z `--auth-mode login`).
- W prawdziwym wdrożeniu EDL udostępniamy firewallowi przez **SAS** (token tylko-do-odczytu
  z wygasaniem) albo publiczny kontener — wybór projektu to prywatny blob + SAS, żeby nie
  wystawiać listy IP anonimowo.

---

## 6. Powiadomienie — webhook (świadomy wybór zamiast Teams)

Powiadomienie wysyłamy **HTTP POST-em na webhook** (np. `webhook.site` do demo, albo własny
endpoint). To celowy wybór projektowy: konektor **Teams** wymaga interaktywnej zgody OAuth
przy autoryzacji, która w tenancie studenckim bywa zablokowana lub niedostępna. Webhook
nie wymaga interaktywnej zgody, jest powtarzalny w deploy IaC i nadaje się do automatu.
URL podaje się parametrem `notifyWebhookUrl` przy wdrożeniu.

> Ograniczenie: webhook nie ma uwierzytelnienia — to akceptowalne **tylko w demo**.
> W produkcji: podpis HMAC nagłówkiem lub endpoint za bramką z auth.

---

## 7. Wdrożenie krok po kroku

### (a) Dzień 0 — przygotowanie parametrów (jednorazowo)

Automation rule musi wskazać **principal SP Sentinela** ("Azure Security Insights"), który
ma prawo uruchamiać playbooki. Pobierz jego `objectId`:

```bash
az ad sp list \
  --filter "appId eq '98785600-1bb7-4fb9-b9fa-19afe2c8a360'" \
  --query "[0].id" -o tsv
```

Wynik wstaw do `infra/bicep/main.dev.bicepparam` jako `sentinelAutomationPrincipalId`.
Ustaw też `notifyWebhookUrl` (np. świeży adres z `webhook.site`):

```bicep
param sentinelAutomationPrincipalId = '<objectId z polecenia wyżej>'
param notifyWebhookUrl = 'https://webhook.site/<twoj-unikalny-id>'
```

### (b) Wdrożenie

```bash
az deployment sub create \
  --location swedencentral \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```

### (c) Weryfikacja statyczna

```bash
./infra/scripts/verify-week6.sh            # domyślnie hg-dev-rg
```

Skrypt potwierdza: playbook (Enabled, UAMI), connection, automation rule, 3 zawężone role,
kontener `edl`. Sekcja 6 będzie jeszcze pusta — to normalne **przed** pierwszym incydentem.

### (d) Wywołanie reakcji — symulacja ataku LUB ręczny Run playbook

- **Symulacja** (mini-Hydra; FQDN web-sensora wypisuje skrypt w sekcji 7):

  ```bash
  for i in $(seq 1 25); do
    curl -s -X POST "https://<FQDN-web>/wp-login.php" -d "log=admin&pwd=pass$i" -o /dev/null
  done
  ```

  Po ~5–10 min powstaje incydent → automation rule → playbook.

- **Ręcznie** (test playbooka przed poleganiem na automation rule):
  Microsoft Sentinel → **Incidents** → wybierz incydent → **Run playbook** →
  `hg-{env}-pb-block-ip`.

### (e) Dowód działania (na obronę §14.4)

Uruchom `verify-week6.sh` ponownie i/lub sprawdź:

- regułę `Block-<ip>` w NSG (zrzut **przed/po** — porównanie pokazuje auto-mitygację),
- zawartość `edl/blocked-ips.txt`,
- powiadomienie na endpointe webhooka,
- komentarz w incydencie z policzonym **MTTR**.

---

## 8. Cofnięcie blokady (rollback)

```bash
az network nsg rule delete -g hg-dev-rg --nsg-name hg-dev-nsg-dmz -n Block-<ip>
```

W razie potrzeby wyczyść również wpis w `edl/blocked-ips.txt` (blob upload nadpisujący).

---

## 9. Etyka

Blokujemy **wyłącznie ruch przychodzący na własnym NSG honeypotów** — w segmencie, w którym
i tak wszystko jest celowo dozwolone atakującym. Nie ma wpływu na osoby trzecie ani na ruch
produkcyjny poza naszym laboratorium. Zachowujemy **retencję** wpisów (kto, kiedy, na jakiej
podstawie — komentarz incydentu) i **transparentność** (każda blokada jest udokumentowana
i odwracalna poleceniem z sekcji 8).

---

## 10. Ograniczenia i ryzyka

- **Konektor Sentinela + MSI** — autoryzacja `hg-{env}-con-sentinel` weryfikuje się przy
  pierwszym uruchomieniu playbooka; jeśli `overallStatus != Connected`, odpal playbook raz
  ręcznie (sekcja 7d).
- **Webhook bez auth** — wyłącznie do demo (patrz §6).
- **Priorytety reguł NSG** — `Block-*` korzystają z puli numerów priorytetu; kolizja
  (ten sam atakujący zablokowany ponownie) **nadpisuje** ten sam blok zamiast tworzyć
  duplikat — świadome zachowanie idempotentne, ale warto pilnować rozmiaru puli.
- **Okno czasowe** — incydent powstaje 5–10 min po ataku (okno reguły + ingestia);
  mitygacja sama jest szybka (sekundy), więc MTTR liczymy od incydentu, nie od pakietu.

---

## 11. Następny krok (Tydzień 7)

Session Replay (odtwarzanie sesji TTY → `xterm.js` w panelu) oraz feed **STIX/IoC** —
wzbogacenie EDL i incydentów o zewnętrzną threat intelligence.
