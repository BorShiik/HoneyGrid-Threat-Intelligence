# ============================================================================
# HoneyGrid — obraz sensora Cowrie (SSH/Telnet honeypot)
#
# Budujemy CIENKĄ warstwę na oficjalnym obrazie `cowrie/cowrie`: bierzemy
# gotowy, utrzymywany upstream i nakładamy WYŁĄCZNIE naszą konfigurację
# fingerprint-evasion (etc/cowrie.cfg + etc/userdb.txt). Nie forkujemy kodu
# Cowrie — dzięki temu łatwo aktualizować obraz bazowy.
#
# Budowanie w chmurze (bez lokalnego Dockera — robi to człowiek w Tygodniu 2):
#   az acr build --registry <acrLoginServer> \
#       --image honeygrid-cowrie:latest sensors/cowrie
#
# UWAGA: ten Dockerfile dotyczy TYLKO kontenera Cowrie. Sidecar CowrieShipper
# (worker .NET, który tail-uje cowrie.json i wysyła do Event Hubs) ma własny
# Dockerfile w src/HoneyGrid.Sensors/HoneyGrid.Sensors.CowrieShipper i jest
# uruchamiany jako DRUGI kontener w tym samym Container App (wzorzec sidecar).
# ============================================================================

FROM cowrie/cowrie:latest

# Cowrie w obrazie bazowym działa jako użytkownik 'cowrie' z katalogiem
# domowym /cowrie/cowrie-git. Tam żyją etc/, var/, honeyfs/ itd.
# Nakładamy naszą konfigurację, zachowując prawa właściciela.
USER root

# --- Nasza konfiguracja (fingerprint-evasion) ------------------------------
# Nadpisujemy cowrie.cfg (fałszywy hostname, spoofowany banner OpenSSH 8.9,
# Telnet on, JSON on, porty 2222/2223) oraz userdb.txt (kontrolowane logowanie
# pod badanie post-exploitation).
# UWAGA: obraz bazowy cowrie/cowrie jest MINIMALNY (brak /bin/sh) — nie da się
# uruchomić `RUN chown`. Dlatego właściciela ustawiamy bezpośrednio w COPY
# (--chown), bez osobnego kroku RUN wymagającego powłoki.
COPY --chown=cowrie:cowrie etc/cowrie.cfg    /cowrie/cowrie-git/etc/cowrie.cfg
COPY --chown=cowrie:cowrie etc/userdb.txt    /cowrie/cowrie-git/etc/userdb.txt

USER cowrie

# ============================================================================
# PUNKTY MONTOWANIA / PERSYSTENCJA (Azure Files SMB + wolumen współdzielony)
# Faktyczne montowanie definiuje Container App (infra/bicep/modules/app.bicep).
# Tu dokumentujemy KONTRAKT, którego musi trzymać się definicja wolumenów:
#
#   1) var/lib/cowrie/tty/      -> Azure Files (share 'cowrie'), trwałe.
#        Binarne nagrania sesji TTY do Session Replay (xterm.js). Muszą
#        przeżyć restart kontenera, dlatego SMB, a nie efemeryczny dysk.
#
#   2) var/lib/cowrie/downloads/ -> Azure Files (share 'cowrie'), trwałe.
#        Pliki/malware ściągnięte przez atakujących — zachowujemy do analizy
#        statycznej. NIGDY nie uruchamiać poza sandboxem!
#
#   3) var/log/cowrie/          -> wolumen WSPÓŁDZIELONY (emptyDir) między
#        kontenerem Cowrie a sidecarem CowrieShipper W TYM SAMYM Container App.
#        Cowrie pisze cowrie.json, CowrieShipper go tail-uje i wysyła do
#        Event Hubs. emptyDir wystarczy: log to strumień tranzytowy, trwałą
#        kopię (rawRef) robi już pipeline ingestion po stronie analitycznej.
#
# Porty nasłuchu wewnątrz kontenera: 2222 (SSH), 2223 (Telnet).
# Mapowanie portów publicznych 22->2222 i 23->2223 robi ingress Container Apps.
# ============================================================================
EXPOSE 2222 2223

# Entrypoint/healthcheck dziedziczymy z obrazu bazowego cowrie/cowrie
# (uruchamia `cowrie start` w trybie foreground). Nie nadpisujemy CMD.
