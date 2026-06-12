# Sensor Cowrie — SSH/Telnet honeypot (HoneyGrid)

Ten katalog zawiera konfigurację i obraz sensora **Cowrie** — wysoko-/średnio-
interakcyjnego honeypota SSH i Telnet. To główny sensor projektu HoneyGrid:
emuluje prawdziwy serwer Linux, nagrywa pełne sesje atakujących i wysyła
telemetrię do Event Hubs, skąd trafia do Cosmos DB, Blob i Sentinela.

## Co to jest i jak działa

Cowrie udaje serwer SSH/Telnet. Atakujący (najczęściej botnety i automaty)
łączy się, próbuje zgadnąć hasło, a po "udanym" logowaniu trafia do **emulowanej
powłoki** — może wpisywać komendy (`uname -a`, `wget ...`, `cat /etc/passwd`),
a Cowrie zwraca wiarygodne odpowiedzi z fałszywego systemu plików. Nic z tego
nie dzieje się na prawdziwym systemie — to piaskownica.

Bierzemy oficjalny obraz `cowrie/cowrie` i nakładamy na niego cienką warstwę
naszej konfiguracji (patrz `Dockerfile`).

## Fingerprint-evasion (dlaczego skanery nas nie demaskują)

Honeypot jest wart tyle, ile długo atakujący wierzy, że to prawdziwy host.
Skanery (Shodan, Censys) i skrypty rozpoznają domyślne Cowrie po sygnaturach.
Nasze przeciwdziałania (`etc/cowrie.cfg`):

| Element                     | Domyślne Cowrie (demaskujące)        | Nasze ustawienie (wiarygodne)                       |
| --------------------------- | ------------------------------------ | --------------------------------------------------- |
| Banner SSH                  | `SSH-2.0-OpenSSH_6.0p1 Debian...`    | `SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.4` (Ubuntu 22.04) |
| Hostname                    | `svr04`                              | `srv-prod-01` (wygląda jak realny serwer prod)      |
| Logowanie                   | często "akceptuje wszystko"          | wąska lista realnie słabych haseł + reszta odrzucana |
| Telnet                      | często wyłączony                     | włączony (łapie botnety IoT/Mirai na porcie 23)     |

Logika poświadczeń jest w `etc/userdb.txt`: **celowo** wpuszczamy kilka par
(`root:123456`, `admin:admin`, `root:root`), żeby studiować fazę
**post-exploitation**; odrzucamy pary "testowe", którymi skanery sprawdzają,
czy host wpuszcza cokolwiek (to by nas zdemaskowało).

## Mapowanie portów

Kontener Cowrie nie ma uprawnień roota, więc nasłuchuje na portach >1024.
Mapowanie portów publicznych na wewnętrzne robi ingress Container Apps
(`infra/bicep/modules/app.bicep`):

| Publiczny (Internet) | Wewnętrzny (kontener) | Protokół |
| -------------------- | --------------------- | -------- |
| 22                   | 2222                  | SSH      |
| 23                   | 2223                  | Telnet   |

> UWAGA dot. ekspozycji portu 22/23 na zewnątrz: środowisko Container Apps typu
> Consumption ma ograniczenia w wystawianiu zewnętrznych portów TCP na niskich
> numerach. Szczegóły i wybrane obejście opisano w komentarzach
> `infra/bicep/modules/app.bicep` (sekcja `UWAGA / TODO` przy aplikacji cowrie).

## Znane ograniczenie: brak prawdziwego IP atakującego za TCP-ingress

Ruch TCP do sensorów (Cowrie, tcp-listener) przechodzi przez ingress Container
Apps (proxy envoy). Z punktu widzenia kontenera **adresem źródłowym połączenia
jest wewnętrzny adres proxy (100.100.0.x), a nie realny adres atakującego** —
to samo zjawisko potwierdzono na żywych danych w tabeli `Cowrie_CL`
(Log Analytics).

Dla sensora webowego problem rozwiązuje nagłówek `X-Forwarded-For`, który
ingress dokleja do żądań HTTP (patrz `HoneyGrid.Sensors.Web`). **Dla czystego
TCP takiego mechanizmu nie ma**: jedynym standardowym sposobem przekazania
adresu klienta przez proxy TCP jest *proxy protocol* (HAProxy PROXY v1/v2),
a ingress Azure Container Apps go **nie wspiera** — nie da się go włączyć po
stronie envoya zarządzanego przez platformę, więc informacja o źródłowym IP
ginie zanim połączenie dotrze do kontenera.

Konsekwencje i stan na dziś:

- pola `src_ip` w zdarzeniach Cowrie i tcp-listenera zawierają adres proxy
  (100.100.0.x) — **nie nadają się do atrybucji ani geolokacji**,
- korelację atakujących dla SSH/Telnet trzeba opierać na innych sygnałach
  (poświadczenia, fingerprint klienta SSH/HASSH, komendy sesji),
- pełnowartościowe źródłowe IP w tym projekcie daje sensor webowy (HTTP).

Ewentualne wyjście wymagałoby zmiany platformy hostingu sensorów TCP
(np. VM / kontener z publicznym IP zamiast Container Apps) — świadomie
**nie obchodzimy** tego ograniczenia w obecnej architekturze.

## Jak CowrieShipper czyta cowrie.json

Cowrie zapisuje każde zdarzenie jako jedną linię JSON do
`var/log/cowrie/cowrie.json` (JSON Lines). W tym samym Container App działa
**drugi kontener — sidecar `CowrieShipper`** (worker .NET,
`src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper`), który:

1. tail-uje `cowrie.json` ze **wspólnego wolumenu** (emptyDir) współdzielonego
   z kontenerem Cowrie,
2. deserializuje każde zdarzenie (bez regexów — czysty JSON),
3. wysyła je do Event Hubs (`honeypot-events`) **bezkluczowo**, przez
   Managed Identity (`DefaultAzureCredential` → User-Assigned MI w chmurze).

Konfiguracja sidecara (zmienne środowiskowe ustawiane w `app.bicep`):
FQDN namespace Event Hubs, nazwa Event Huba, `SensorId`, `SensorType`.

## Persystencja przez Azure Files

Stan zmienny sensora leży na udziale **Azure Files (SMB) `cowrie`**
(tworzonym w `infra/bicep/modules/data.bicep`):

- `var/lib/cowrie/tty/` — binarne nagrania sesji TTY (Session Replay w xterm.js),
- `var/lib/cowrie/downloads/` — pliki/malware ściągnięte przez atakujących
  (do analizy statycznej — **nigdy nie uruchamiać poza sandboxem**).

Log tranzytowy `var/log/cowrie/cowrie.json` leży na wolumenie współdzielonym
(emptyDir) — trwałą kopię zdarzeń robi już pipeline ingestion po stronie
analitycznej (Blob `raw/`), więc tu trwałość nie jest potrzebna.

## Pliki w katalogu

| Plik             | Rola                                                          |
| ---------------- | ------------------------------------------------------------ |
| `etc/cowrie.cfg` | Główna konfiguracja: banner, hostname, porty, JSON, Telnet   |
| `etc/userdb.txt` | Polityka poświadczeń (kontrolowane logowanie)                |
| `Dockerfile`     | Cienka warstwa nad `cowrie/cowrie` + kontrakt montowań       |
| `README.md`      | Ten plik                                                     |
