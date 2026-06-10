# Fixtures — stałe punkty styku pracy równoległej

Ten katalog zawiera **uzgodnione przykładowe dane wymieniane między trackami**
(plan §8.3), dzięki którym oba tracki pracują równolegle i nie czekają na siebie.
Zmiana formatu któregokolwiek pliku wymaga zgody obu stron — to są kontrakty,
nie zwykłe dane testowe.

## Kto co komu daje

| Plik | Od kogo | Dla kogo | Po co |
|---|---|---|---|
| `cowrie/cowrie-sample.json` | Track A | Track B | Realne przykłady surowych logów Cowrie — Track B buduje na nich parser/mapper i stub-klasyfikator, zanim potok ingest będzie gotowy. |
| `classification/mock-classifications.json` | Track B | Track A | Przykładowe wyniki klasyfikacji AI — Track A wpina je w potok wzbogacania i UI, zanim prawdziwy klasyfikator (Azure OpenAI) będzie gotowy. |

## `cowrie/cowrie-sample.json`

Format **JSON Lines** (jedna linia = jeden wpis logu Cowrie), dokładnie tak,
jak sensor SSH wysyła zdarzenia do Event Huba. Zawiera dwie kompletne sesje:

1. **Sesja `a1b2c3d4e5f6`** (185.220.101.42) — pełny łańcuch ataku:
   `cowrie.session.connect` → 3× `cowrie.login.failed` → `cowrie.login.success`
   → rozpoznanie hosta (`cowrie.command.input`) → pobranie koparki
   (`cowrie.session.file_download` z `shasum`) → utrwalenie przez crontab
   → `cowrie.log.closed` (z `ttylog` i `duration` — podstawa odtwarzania sesji).
2. **Sesja `f6e5d4c3b2a1`** (43.156.88.201) — krótki, nieudany brute-force:
   `connect` → 3× `login.failed` → `log.closed`.

Kluczowe pola: `eventid`, `src_ip`, `session`, `sensor`, `timestamp`,
`username`/`password` (logowania), `input` (polecenia), `url`/`shasum`/`destfile`
(pobrania), `ttylog`/`duration` (zamknięcie logu TTY).

## `classification/mock-classifications.json`

Tablica 5 przykładowych wyników klasyfikatora AI. Pole `classification`
jest **dokładnie zgodne** ze schematem `Classification` z
[`docs/openapi.yaml`](../docs/openapi.yaml):
`killChainPhase` (enum: recon, weaponization, delivery, exploitation,
installation, c2, actions), `category`, `sophistication` (0–1), `intent`,
`actorId`. Pole `eventId` wskazuje zdarzenie (`HoneypotEvent.id`), którego
dotyczy klasyfikacja.

Do czasu ukończenia prawdziwego klasyfikatora Track A używa
**stub-klasyfikatora**, który zwraca te wpisy (lub losowy z nich) w tym samym
kształcie danych.
