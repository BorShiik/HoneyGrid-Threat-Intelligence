# HoneyGrid.Sensors.CowrieShipper — most logu JSON Cowrie → Event Hub (.NET 10, Worker).
# Obraz wielostopniowy. Kontekst budowania = KORZEŃ repozytorium
# (potrzebny dostęp do HoneyGrid.Contracts oraz HoneyGrid.Sensors.Common).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/Dockerfile -t honeygrid-cowrie-shipper .
#
# Uruchomienie zwykle z zamontowanym wolumenem logów Cowrie (read-only):
#   -v cowrie-logs:/cowrie/var/log/cowrie:ro

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./

COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/HoneyGrid.Sensors.Common.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/HoneyGrid.Sensors.CowrieShipper.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/
RUN dotnet restore src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/HoneyGrid.Sensors.CowrieShipper.csproj

COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/
RUN dotnet publish src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper/HoneyGrid.Sensors.CowrieShipper.csproj \
    -c Release -o /app --no-restore

# ---- Etap uruchomieniowy ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Worker bez nasłuchu sieciowego — nie udostępnia portów.
# Użytkownik nie-root (konto "app" z obrazu bazowego).
USER app

COPY --from=build /app .
ENTRYPOINT ["dotnet", "HoneyGrid.Sensors.CowrieShipper.dll"]
