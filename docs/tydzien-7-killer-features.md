# Tydzień 7 — killer-ficzy Track A (Session Replay + STIX/IoC)

Po Tygodniu 6 pętla SOAR jest domknięta: telemetria honeypotów staje się incydentami,
a incydenty same uruchamiają mitygację. Tydzień 7 dokłada **dwa killer-ficzy** —
funkcje, które na obronie projektu robią różnicę i pokazują wartość zebranych danych:

1. **Session Replay** — odtworzenie sesji atakującego (nagranie TTY z Cowrie) klatka po
   klatce w przeglądarce, jak terminal na żywo. To „moment-killer" demo (§14.2 planu):
   komisja widzi DOKŁADNIE, co robił bot/atakujący w honeypocie.
2. **Eksport STIX 2.1 / IoC** — standaryzowany feed wskaźników kompromitacji
   (Indicator/Malware/Attack-Pattern w postaci bundle STIX 2.1), gotowy do importu
   w innych narzędziach threat-intel (MISP, TIP, SIEM). Dowód, że platforma produkuje
   wynik **interoperacyjny**, a nie zamknięty w sobie (§14.6).

---

## 1. Cel

Zamienić surowe dane (sesje TTY, zdarzenia, IoC w Cosmos) w **dwa namacalne produkty**:
odtwarzalną sesję ataku i eksportowalny feed STIX. Obie funkcje czytają tylko dane —
żadnych zapisów — i są serwowane przez nowy, lekki **host API**.

---

## 2. Architektura

```
        ┌──────────────────────────────────────────────────────────────┐
        │  Dashboard SOC (HoneyGrid.Web)                                 │
        │   - IoC Feed UI  ── GET /api/iocs/stix ─────────────┐          │
        │   - Session Replay (xterm.js) ── GET /api/sessions/{id}/replay │
        └──────────────────────────────────────────────┬─────┴──────────┘
                                                        │ HTTPS (ingress)
                                                        ▼
        ┌──────────────────────────────────────────────────────────────┐
        │  Host API — Container App  hg-{env}-ca-api                     │
        │  (.NET 10 Minimal API; scale-to-zero, min=0 / max=2)           │
        │  tożsamość id-api (UserAssigned, TYLKO ODCZYT):                │
        │    • Cosmos DB Built-in Data Reader  → events / iocs / sessions│
        │    • Storage Blob Data Reader        → nagrania TTY (blob tty) │
        └───────────────┬─────────────────────────────┬─────────────────┘
                        │ odczyt dokumentów             │ odczyt nagrań TTY
                        ▼                               ▼
              Cosmos DB (honeygrid)            Storage (kontener 'tty')
```

### Host API (tymczasowo Track A)

- Nowy Container App `hg-{env}-ca-api` w tym samym, JEDNYM środowisku Container Apps
  (`hg-{env}-cae`, konsolidacja z Tygodnia 4 — limit subskrypcji studenckiej nie
  mieści drugiego środowiska w regionie).
- Na razie host serwuje 2 endpointy Track A; **docelowo zlewa się z API Track B**
  (REST nad Cosmos: events/actors/stats + SignalR mapa ataków). Świadomie tymczasowy.
- `cpu 0.25 / memory 0.5Gi` (najtańsza kombinacja, vCPU:GiB = 1:2).
- **Scale-to-zero** (`minReplicas: 0`, `maxReplicas: 2`): API jest **sterowane
  żądaniami** — gdy nikt nie pyta o STIX/replay, nie płacimy. To INACZEJ niż
  sensory (zawsze-on, nasłuch 24/7) i worker (ciągły konsument strumienia).
  Cena: pierwsze żądanie po uśpieniu ma zimny start — akceptowalne dla API
  analitycznego/demo.
- Ingress **HTTP external**, `targetPort 8080`, `transport 'auto'` (platforma
  terminuje HTTPS; kontener słucha czystego HTTP na 8080, jak web-sensor).

### Tożsamość id-api — read-only (least privilege)

Osobna UAMI `hg-{env}-id-api` (jak id-sensor / id-worker — izolacja przez tożsamość).
Świadoma **asymetria** względem workera: worker PISZE (Cosmos Data Contributor,
Blob Data Contributor), API tylko CZYTA:

| Rola | Zakres | Po co | Moduł |
|------|--------|-------|-------|
| Cosmos DB Built-in Data **Reader** (`...0001`) | konto Cosmos | API czyta events/iocs/sessions | `data.bicep` (sqlRoleAssignments) |
| Storage Blob Data **Reader** (`2a2b9908-...`) | konto Storage | API czyta nagrania TTY | `rbac.bicep` |
| AcrPull (`7f951dda-...`) | rejestr ACR | pull obrazu `honeygrid-api` | `app.bicep` (kolejność wdrożenia) |

Rola Cosmos to **płaszczyzna danych** (`sqlRoleAssignments`), NIE ARM — bez niej SDK
dostaje 403 (`disableLocalAuth: true` wyklucza klucze). Kompromitacja API nie daje
zapisu do danych ani uprawnień workera/sensorów.

---

## 3. Killer-ficzy #1 — STIX 2.1 / IoC

### Silnik (`HoneyGrid.Stix`)

- Obiekty STIX 2.1 (SDO): `Indicator`, `Malware`, `Attack-Pattern`, opakowane w `Bundle`
  (`type: "bundle"`, lista `objects`).
- Serializacja zgodna z formatem JSON platformy (camelCase, enumy jako stringi).

### Język wzorców (STIX Patterning)

Wskaźniki używają języka wzorców STIX 2.1 z operatorami obserwacji:
- **FOLLOWEDBY** — sekwencja zdarzeń (np. logowanie, potem pobranie pliku),
- **WITHIN** — okno czasowe (np. `WITHIN 60 SECONDS`),
- **REPEATS** — powtórzenia (np. brute-force: `REPEATS 5 TIMES`).

### Endpoint `/api/iocs/stix`

`GET /api/iocs/stix` → bundle STIX 2.1 z kontenera `iocs` (Cosmos). Walidacja zgodności:
**STIX 2 Pattern Validator** (oss.oasis-open.org) — wzorce muszą przechodzić walidator.

---

## 4. Killer-ficzy #2 — Session Replay

### Kopiowanie TTY → Blob

Cowrie zapisuje nagrania TTY (format „ttylog") na lokalnym wolumenie. Sidecar
`cowrie-shipper` (zmieniony w Tygodniu 7) kopiuje pliki TTY do kontenera blob `tty`.
> **TODO T3 (domknięcie):** dopięcie kopiowania TTY w shipperze — patrz parser/endpoint
> niżej; bez nagrania w blobie endpoint replay zwróci pustą sesję.

### Parser TTY → ramki (`HoneyGrid.Replay`)

Czysta biblioteka (bez zależności Azure, testowalna offline) parsująca format Cowrie
„ttylog" na ramki `ReplayFrame` (offset czasu + dane). `TtyParser` + `SafeUtf8`
(bezpieczne dekodowanie bajtów na granicach ramek).

### Endpoint `/api/sessions/{id}/replay`

`GET /api/sessions/{id}/replay` → metadane sesji (z Cosmos `sessions`) + ramki TTY
(z blob `tty`) jako JSON dla odtwarzacza.

### Odtwarzacz xterm.js (frontend)

Dashboard renderuje ramki w terminalu **xterm.js**, odtwarzając sesję z oryginalnym
timingiem. **StrictMode wyłączony** dla komponentu odtwarzacza (podwójny mount Reacta
w StrictMode dublował zapis ramek do terminala).

---

## 5. Wdrożenie

> Wdrożenie i build obrazów wykonuje UŻYTKOWNIK (RG bywa kasowane między sesjami,
> ACR Tasks wyłączone — build lokalnie amd64). Skrypt verify-week7.sh tylko czyta.

1. **Rebuild obrazów** (z korzenia repo, platforma amd64):
   - `honeygrid-api` — **NOWY** obraz (host API):
     `docker build -f src/HoneyGrid.Api/Dockerfile -t <acr>.azurecr.io/honeygrid-api:latest .`
   - `honeygrid-cowrie-shipper` — **ZMIENIONY** (kopiowanie TTY → blob).
   - `push` do ACR (`hgdevacru67w6tzuh4qgw`).
2. **Deploy** — `az deployment sub create ...` (main.bicep wpina nowe parametry —
   patrz niżej „Integracja").
3. **Verify** — `./infra/scripts/verify-week7.sh hg-dev-rg`.
4. **Demo**:
   - **moment-killer (§14.2):** otwórz Session Replay w dashboardzie i odtwórz sesję
     bota (logowanie → komendy → pobranie malware) w terminalu xterm.js.
   - **eksport bundle STIX (§14.6):** `curl https://<api-fqdn>/api/iocs/stix` →
     wklej do STIX 2 Pattern Validator lub zaimportuj w MISP.

---

## 6. Integracja (main.bicep — wiring orkiestratora)

Moduł `app.bicep` dostaje z `security.bicep`: `apiIdentityId`, `apiIdentityClientId`,
`apiIdentityPrincipalId`. Moduł `data.bicep` dostaje `apiPrincipalId`, moduł
`rbac.bicep` dostaje `apiPrincipalId` (oba z `security.outputs.apiIdentityPrincipalId`).
Nowe wyjścia: `app.outputs.apiAppName`, `app.outputs.apiAppFqdn`.

---

## 7. Ograniczenia

- **Format TTY Cowrie zależny od wersji** — parser celuje w „ttylog"; inny build Cowrie
  może zmienić układ ramek (regresja przy aktualizacji obrazu honeypota).
- **Host API tymczasowy** — Track A; docelowo łączy się z pełnym API Track B
  (events/actors/stats + SignalR). Na razie 2 endpointy.
- **Scale-to-zero** — pierwsze żądanie po uśpieniu ma zimny start (sekundy).
- **Kopiowanie TTY (TODO T3)** — dopóki shipper nie skopiuje nagrania do blob `tty`,
  endpoint replay zwróci pustą/niepełną sesję.
