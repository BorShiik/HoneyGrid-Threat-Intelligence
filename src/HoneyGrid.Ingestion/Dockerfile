# HoneyGrid.Ingestion — worker ingestii + wzbogacania zdarzeń (.NET 10, Worker).
# Obraz wielostopniowy. Kontekst budowania = KORZEŃ repozytorium
# (potrzebny dostęp do HoneyGrid.Contracts).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Ingestion/Dockerfile -t honeygrid-ingestion .
#
# OPCJONALNE bazy GeoLite2: przed budowaniem wrzuć GeoLite2-City.mmdb i
# GeoLite2-ASN.mmdb do src/HoneyGrid.Ingestion/geoip/ (instrukcja: geoip/README.md).
# Katalog istnieje zawsze (.gitkeep), więc COPY działa także bez baz —
# worker wykryje brak plików i wyłączy wzbogacanie GeoIP (łagodna degradacja).

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./

COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Ingestion/HoneyGrid.Ingestion.csproj src/HoneyGrid.Ingestion/
RUN dotnet restore src/HoneyGrid.Ingestion/HoneyGrid.Ingestion.csproj

COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Ingestion/ src/HoneyGrid.Ingestion/
RUN dotnet publish src/HoneyGrid.Ingestion/HoneyGrid.Ingestion.csproj \
    -c Release -o /app --no-restore

# ---- Etap uruchomieniowy ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Użytkownik nie-root (konto "app" z obrazu bazowego).
USER app

COPY --from=build /app .

# Opcjonalne bazy GeoLite2 — kopiowane wprost z kontekstu, aby dało się je
# podmienić bez przebudowy etapu build (kod szuka ich względem AppContext.BaseDirectory).
COPY src/HoneyGrid.Ingestion/geoip/ /app/geoip/

ENTRYPOINT ["dotnet", "HoneyGrid.Ingestion.dll"]
