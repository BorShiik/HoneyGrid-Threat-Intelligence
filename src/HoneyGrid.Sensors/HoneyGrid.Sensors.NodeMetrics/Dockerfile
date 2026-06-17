# HoneyGrid.Sensors.NodeMetrics — agent metryk hosta VPS → Cosmos sdnNodes (.NET 10, Worker).
# Obraz wielostopniowy. Kontekst budowania = KORZEŃ repozytorium (dostęp do HoneyGrid.Contracts).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/Dockerfile -t honeygrid-node-metrics .
#
# Uruchomienie na VPS (metryki HOSTA, nie kontenera) — podmontuj /proc hosta:
#   docker run -e NodeMetrics__Site=frankfurt \
#     -e NodeMetrics__CosmosEndpoint=https://...documents.azure.com:443/ \
#     -e NodeMetrics__ProcPath=/host/proc -e AZURE_CLIENT_ID=<id-sensor clientId> \
#     -v /proc:/host/proc:ro honeygrid-node-metrics

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./

COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/HoneyGrid.Sensors.NodeMetrics.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/
RUN dotnet restore src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/HoneyGrid.Sensors.NodeMetrics.csproj

COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/
RUN dotnet publish src/HoneyGrid.Sensors/HoneyGrid.Sensors.NodeMetrics/HoneyGrid.Sensors.NodeMetrics.csproj \
    -c Release -o /app --no-restore

# ---- Etap uruchomieniowy ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Worker bez nasłuchu sieciowego — nie udostępnia portów. Użytkownik nie-root.
USER app

COPY --from=build /app .
ENTRYPOINT ["dotnet", "HoneyGrid.Sensors.NodeMetrics.dll"]
