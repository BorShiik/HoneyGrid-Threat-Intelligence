# HoneyGrid.Api — host API killer-ficzy Track A (.NET 10, Minimal API + SignalR).
# Serwuje Session Replay (/api/sessions/{id}/replay) i eksport STIX/IoC
# (/api/iocs/stix). Obraz wielostopniowy. Kontekst budowania = KORZEŃ
# repozytorium (potrzebny dostęp do projektów referencyjnych API:
# HoneyGrid.Contracts, HoneyGrid.Stix, HoneyGrid.Replay).
#
# Budowanie (z korzenia repo):
#   docker build -f src/HoneyGrid.Api/Dockerfile -t honeygrid-api .

# ---- Etap budowania ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Wspólne ustawienia kompilacji (TargetFramework itd.).
COPY Directory.Build.props ./

# Najpierw pliki projektów — lepsze cache'owanie warstwy restore.
COPY src/HoneyGrid.Contracts/HoneyGrid.Contracts.csproj src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Stix/HoneyGrid.Stix.csproj src/HoneyGrid.Stix/
COPY src/HoneyGrid.Replay/HoneyGrid.Replay.csproj src/HoneyGrid.Replay/
COPY src/HoneyGrid.Api/HoneyGrid.Api.csproj src/HoneyGrid.Api/
RUN dotnet restore src/HoneyGrid.Api/HoneyGrid.Api.csproj

# Pełne źródła i publikacja.
COPY src/HoneyGrid.Contracts/ src/HoneyGrid.Contracts/
COPY src/HoneyGrid.Stix/ src/HoneyGrid.Stix/
COPY src/HoneyGrid.Replay/ src/HoneyGrid.Replay/
COPY src/HoneyGrid.Api/ src/HoneyGrid.Api/
RUN dotnet publish src/HoneyGrid.Api/HoneyGrid.Api.csproj \
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
ENTRYPOINT ["dotnet", "HoneyGrid.Api.dll"]
