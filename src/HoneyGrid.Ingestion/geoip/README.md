# Bazy GeoLite2 (MaxMind) — opcjonalne wzbogacanie GeoIP

Worker ingestii potrafi wzbogacać zdarzenia o geolokalizację (kraj, miasto,
współrzędne) i ASN (numer + organizacja) na podstawie lokalnych baz MaxMind
GeoLite2. Bazy są **opcjonalne** — gdy plików brakuje, worker loguje jedno
ostrzeżenie przy starcie i działa dalej bez wzbogacania GeoIP.

## Jak pobrać bazy (darmowe)

1. Załóż bezpłatne konto GeoLite2 na stronie MaxMind:
   https://www.maxmind.com/en/geolite2/signup
2. Po zalogowaniu wygeneruj klucz licencyjny:
   *Account → Manage License Keys → Generate new license key*.
3. Pobierz dwie bazy w formacie **MaxMind DB (.mmdb)** z sekcji
   *Download Databases* (format: "GeoIP2 Binary (.mmdb)"):
   - **GeoLite2 City** → plik `GeoLite2-City.mmdb`
   - **GeoLite2 ASN** → plik `GeoLite2-ASN.mmdb`
4. Rozpakuj archiwa `.tar.gz` i umieść **oba pliki .mmdb w TYM katalogu**
   (`src/HoneyGrid.Ingestion/geoip/`) **przed budowaniem obrazu Dockera**:

   ```
   src/HoneyGrid.Ingestion/geoip/GeoLite2-City.mmdb
   src/HoneyGrid.Ingestion/geoip/GeoLite2-ASN.mmdb
   ```

5. Zbuduj obraz z korzenia repo — Dockerfile skopiuje katalog do `/app/geoip/`:

   ```
   docker build -f src/HoneyGrid.Ingestion/Dockerfile -t honeygrid-ingestion .
   ```

## Licencja — UWAGA

Bazy GeoLite2 są objęte licencją użytkownika końcowego MaxMind (GeoLite2 EULA):
https://www.maxmind.com/en/geolite2/eula

- **NIE commituj plików .mmdb do repozytorium** (licencja zabrania
  redystrybucji bez rejestracji; pliki są też duże, ~60 MB).
- MaxMind aktualizuje bazy dwa razy w tygodniu — warto okresowo podmieniać.

## Konfiguracja

Ścieżki można nadpisać opcjami `Ingestion:GeoIpCityDbPath` /
`Ingestion:GeoIpAsnDbPath` (domyślnie `geoip/GeoLite2-City.mmdb` i
`geoip/GeoLite2-ASN.mmdb`, ścieżki względne — względem katalogu binarki).
