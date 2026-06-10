# HoneyGrid — architektura

Krótki opis architektury platformy. Kontrakt API: [openapi.yaml](openapi.yaml).
Fixtures do pracy równoległej: [../fixtures/README.md](../fixtures/README.md).

## Diagram

```mermaid
flowchart TB
    subgraph VNET["VNet honeygrid (Azure)"]
        subgraph DMZ["Podsieć DMZ (sensory)"]
            COW["Honeypot SSH — Cowrie"]
            WEBHP["Honeypot Web"]
            RDPHP["Honeypot RDP"]
        end
        subgraph LOGIC["Podsieć logiki (przetwarzanie)"]
            EH["Event Hub"]
            FX["Functions: wzbogacanie<br/>GeoIP + threat intel"]
            CLS["Functions: klasyfikator AI<br/>(Azure OpenAI)"]
            AGG["Functions: agregacje"]
            SR["Azure SignalR"]
            API["API dashboardu"]
        end
        subgraph DATA["Podsieć danych"]
            COS[("Cosmos DB")]
            BLOB[("Blob Storage<br/>TTY + surowe logi")]
        end
    end

    DCR["DCR / agent AMA"]
    LAW["Log Analytics"]
    SEN["Microsoft Sentinel"]
    SOAR["Logic Apps (SOAR)"]
    WEB["Dashboard React"]

    COW & WEBHP & RDPHP -- "ścieżka 1: SIEM" --> DCR --> LAW --> SEN --> SOAR
    COW & WEBHP & RDPHP -- "ścieżka 2: realtime" --> EH --> FX --> COS
    FX --> BLOB
    COS -- Change Feed --> CLS --> COS
    COS -- Change Feed --> AGG --> COS
    COS -- Change Feed --> SR --> WEB
    API --> COS
    WEB --> API
    SEN -. import IoC (STIX 2.1) .-> API
```

## Sieć — 3 podsieci

| Podsieć | Zawartość | Zasady |
|---|---|---|
| **DMZ** | Kontenery sensorów honeypot (SSH/web/RDP) | Wystawiona do internetu na portach-przynętach; NSG blokuje cały ruch wychodzący poza Event Hub i DCR; zero dostępu do pozostałych podsieci. |
| **Logika** | Event Hub, Azure Functions, SignalR, API | Przyjmuje ruch tylko z DMZ (ingest) i od frontendu (API); tożsamości zarządzane (managed identity) do danych. |
| **Dane** | Cosmos DB, Blob Storage, Key Vault | Private endpoints; dostęp wyłącznie z podsieci logiki. |

## Dwie ścieżki telemetrii

1. **SIEM (bezpieczeństwo, minuty):** sensory → **DCR / agent AMA** → Log Analytics → **Microsoft Sentinel**. Reguły analityczne i playbooki SOAR (Logic Apps) — np. automatyczne zgłoszenie incydentu i wpis IP na listę blokad. Ta ścieżka jest niezależna od aplikacji: działa nawet, gdy potok realtime leży.
2. **Realtime (produkt, sekundy):** sensory → **Event Hub** → Functions (parsowanie + wzbogacanie GeoIP/TI) → **Cosmos DB** → **Change Feed** → (a) klasyfikator AI, (b) agregaty, (c) **SignalR** → dashboard React (hub `/hubs/attacks`, zdarzenie `attack`).

## Kontrakty pracy równoległej

Trzy kontrakty pozwalają trackom A i B pracować bez wzajemnego blokowania:

1. **Schemat zdarzeń (NuGet)** — `HoneyGrid.Contracts` z klasą `HoneypotEvent` (camelCase JSON); wersjonowany pakiet wewnętrzny, zmiany tylko za obopólną zgodą.
2. **OpenAPI + MSW** — [`openapi.yaml`](openapi.yaml) jest źródłem prawdy dla REST; backend implementuje endpointy, frontend generuje z niego mocki MSW i buduje UI bez działającego backendu.
3. **Stub-klasyfikator** — do czasu ukończenia klasyfikatora AI potok używa stuba zwracającego dane w kształcie `Classification` (przykłady: [`../fixtures/classification/mock-classifications.json`](../fixtures/classification/mock-classifications.json)); podmiana stub→AI nie zmienia żadnego kontraktu.

## Kontenery Cosmos DB

| Kontener | Klucz partycji | TTL | Zawartość |
|---|---|---|---|
| `events` | `/attackerIp` | 180 dni | Wzbogacone zdarzenia honeypot (`HoneypotEvent`). |
| `actors` | `/id` | — | Profile aktorów zagrożeń (`ThreatActor` + dossier). |
| `sessions` | `/sessionId` | 180 dni | Metadane sesji + referencje do nagrań TTY w Blob. |
| `iocs` | `/type` | — | Wskaźniki kompromitacji (źródło kanału STIX 2.1). |
| `aggregates` | `/bucket` | 30 dni | Wstępnie zliczone agregaty (geo, poświadczenia, szeregi czasowe) dla endpointów `/api/stats/*`. |
