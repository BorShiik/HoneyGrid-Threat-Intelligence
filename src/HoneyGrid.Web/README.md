# HoneyGrid.Web

Interfejs webowy platformy **HoneyGrid** — systemu analizy zagrożeń opartego na sieci honeypotów
(SSH / WWW / RDP). Tydzień 0: fundament aplikacji i system projektowy.

## Jak uruchomić

```bash
npm install
npm run dev
```

Aplikacja wystartuje pod adresem `http://localhost:5173`. W trybie deweloperskim wszystkie
żądania API są przechwytywane przez **MSW** (Mock Service Worker) — backend nie jest potrzebny.

Pozostałe polecenia:

| Polecenie              | Opis                                        |
| ---------------------- | ------------------------------------------- |
| `npm run build`        | Budowa produkcyjna (`tsc -b && vite build`) |
| `npm test`             | Testy jednostkowe (Vitest, jednorazowo)     |
| `npm run test:watch`   | Testy w trybie obserwowania                 |
| `npm run lint`         | ESLint (konfiguracja płaska)                |
| `npm run format`       | Formatowanie Prettierem                     |
| `npm run format:check` | Sprawdzenie formatowania                    |

## Stos technologiczny

- **Vite + React 18.2 + TypeScript** — React przypięty do `18.2.0` (wymaganie projektu).
  `React.StrictMode` jest celowo **wyłączony**: podwójne montowanie efektów w trybie dev psuje
  geometrię terminala xterm.js w przyszłej funkcji odtwarzania sesji (komentarz w `src/main.tsx`).
- **Tailwind CSS v4** — motyw definiowany w CSS przez `@theme` (brak `tailwind.config.js`).
- **shadcn/ui (Radix)** — komponenty tworzone ręcznie w `src/components/ui` według wzorców shadcn.
- **react-router-dom v7** (tryb deklaratywny), **TanStack Query v5**, **Zustand v5**,
  **@microsoft/signalr**, **MSW v2**, **lucide-react**, **framer-motion**.

## Struktura katalogów

```
src/
├── api/          # klient HTTP, hooki TanStack Query, scaffolding SignalR (/hubs/attacks)
├── components/
│   ├── layout/   # AppShell — sidebar + nagłówek ze stanem połączenia
│   ├── ui/       # komponenty shadcn/ui (button, card, badge, tabs, table, dialog, tooltip, skeleton)
│   └── ...       # SeverityBadge, PlaceholderPage
├── mocks/        # MSW: generator zdarzeń + handlery wszystkich endpointów
├── pages/        # strony tras (placeholdery „W budowie — Tydzień N")
├── stores/       # magazyny Zustand (stan połączenia SignalR)
├── test/         # konfiguracja Vitest + test dymny powłoki
└── types/        # typy TypeScript lustrzane do kontraktu OpenAPI (HoneypotEvent itd.)
```

## System projektowy

Strona deweloperska dostępna pod trasą **`/design-system`** prezentuje:

- **Kolory** — tokeny semantyczne (`--background`, `--card`, `--primary`, …) zgodne z konwencją
  shadcn/ui; akcent miodowo-bursztynowy (rodzina `#f59e0b`), tła w głębokim graficie.
- **Skalę zagrożeń** — `--severity-critical` (czerwony), `--severity-high` (pomarańczowy),
  `--severity-medium` (bursztynowy), `--severity-low` (szmaragdowy).
- **Typografię** — systemowy krój dla interfejsu, monospace (JetBrains Mono / ui-monospace) dla
  adresów IP, komend i terminala.
- **Komponenty i odznaki severity** — Krytyczny / Wysoki / Średni / Niski.

Motyw jest na razie wyłącznie ciemny (produkt klasy SOC).

## Mocki MSW

W trybie dev (`npm run dev`) worker MSW (`public/mockServiceWorker.js`) przechwytuje żądania
i serwuje realistyczne dane (adresy IP, kraje, poświadczenia typu `root/123456`, komendy
`wget …`):

- `GET /api/feed?since=&take=` — strumień zdarzeń `HoneypotEvent[]`
- `GET /api/stats/overview`, `GET /api/stats/geo`, `GET /api/stats/credentials`
- `GET /api/actors`, `GET /api/actors/{id}`
- `GET /api/sessions/{id}/replay`
- `GET /api/iocs/stix`

Generator danych: `src/mocks/generator.ts`, handlery: `src/mocks/handlers.ts`.
Hub SignalR (`/hubs/attacks`, zdarzenie `attack`) ma gotowy scaffolding w `src/api/signalr.ts` —
realne połączenie zostanie włączone, gdy powstanie backend (MSW nie przechwytuje WebSocketów).

Testy używają tych samych handlerów przez `msw/node` (`src/test/setup.ts`).
