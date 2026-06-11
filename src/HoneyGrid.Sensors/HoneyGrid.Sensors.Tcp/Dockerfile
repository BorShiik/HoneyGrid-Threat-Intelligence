# HoneyGrid.Sensors.Tcp — generyczny nasłuch TCP/RDP/Telnet (.NET 10, Worker).
# Obraz wielostopniowy. Kontekst budowania = KORZEŃ repozytorium
# (potrzebny dostęp do HoneyGrid.Contracts oraz HoneyGrid.Sensors.Common).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/Dockerfile -t honeygrid-tcp .

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./

COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/HoneyGrid.Sensors.Common.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/HoneyGrid.Sensors.Tcp.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/
RUN dotnet restore src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/HoneyGrid.Sensors.Tcp.csproj

COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/
RUN dotnet publish src/HoneyGrid.Sensors/HoneyGrid.Sensors.Tcp/HoneyGrid.Sensors.Tcp.csproj \
    -c Release -o /app --no-restore

# ---- Etap uruchomieniowy ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Nasłuch na portach Telnet (23) i RDP (3389). UWAGA: port 23 jest uprzywilejowany;
# uruchamianie jako non-root wymaga mapowania portu (np. publish 2323->23) lub NET_BIND_SERVICE.
EXPOSE 23 3389

# Użytkownik nie-root (konto "app" z obrazu bazowego).
USER app

COPY --from=build /app .
ENTRYPOINT ["dotnet", "HoneyGrid.Sensors.Tcp.dll"]
