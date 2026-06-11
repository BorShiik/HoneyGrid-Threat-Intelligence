# HoneyGrid.Sensors.Web — honeypot webowy (.NET 10, Minimal API).
# Obraz wielostopniowy. Kontekst budowania = KORZEŃ repozytorium
# (potrzebny dostęp do HoneyGrid.Contracts oraz HoneyGrid.Sensors.Common).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/Dockerfile -t honeygrid-web .

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Wspólne ustawienia kompilacji (TargetFramework itd.).
COPY Directory.Build.props ./

# Najpierw pliki projektów — lepsze cache'owanie warstwy restore.
COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/HoneyGrid.Sensors.Common.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/HoneyGrid.Sensors.Web.csproj src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/
RUN dotnet restore src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/HoneyGrid.Sensors.Web.csproj

# Pełne źródła i publikacja.
COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.Common/
COPY src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/ src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/
RUN dotnet publish src/HoneyGrid.Sensors/HoneyGrid.Sensors.Web/HoneyGrid.Sensors.Web.csproj \
    -c Release -o /app --no-restore

# ---- Etap uruchomieniowy ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Kestrel nasłuchuje na 8080 (port nieuprzywilejowany — działa pod użytkownikiem non-root).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Użytkownik nie-root (obraz bazowy zawiera konto "app", UID 1654).
USER app

COPY --from=build /app .
ENTRYPOINT ["dotnet", "HoneyGrid.Sensors.Web.dll"]
