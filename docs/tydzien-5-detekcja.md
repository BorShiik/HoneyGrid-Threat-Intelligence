# Tydzień 5 — Detekcja i wizualizacja (detection engineering na Cowrie_CL)

W Tygodniu 4 telemetria honeypotów dopłynęła do Microsoft Sentinel (tabela `Cowrie_CL`,
ścieżka DCE → DCR → Logs Ingestion API). Tydzień 5 zamienia surowe logi w **detekcję**:
reguły analityczne KQL z mapowaniem MITRE ATT&CK, korelacją encji i grupowaniem incydentów,
a do tego **Workbook** — operacyjny pulpit SOC. Całość jako kod (Bicep), odtwarzalna jedną
komendą po każdym skasowaniu grupy zasobów.

---

## 1. Cel

Detection engineering na tabeli `Cowrie_CL`: przekształcić strumień zdarzeń
(`connect`, `login.failed`, `login.success`, `command`, `http.request`) w **alerty i incydenty**,
które operator SOC widzi w Microsoft Sentinel. Nacisk na:

- **wysoki sygnał, niski szum** — reguły oparte na *tempie* (rate-based), nie na pojedynczym
  zdarzeniu, żeby Hydra generująca setki prób dała **jeden** incydent, a nie setki alertów;
- **korelację encji** (IP / Account / Host) — wspólny język dla automatyzacji SOAR (Tydzień 6);
- **mapowanie MITRE ATT&CK** — każdy alert wskazuje technikę, co daje czytelny obraz kampanii;
- **wizualizację** — Workbook pokazujący wolumen, top atakujących, próbowane poświadczenia.

---

## 2. Cztery reguły analityczne

Wszystkie to reguły **Scheduled** (zaplanowane zapytania KQL), displayName zaczyna się od
`HoneyGrid — `, definiowane w `infra/bicep/modules/sentinel.bicep`.

| Reguła (displayName) | Logika (skrót KQL) | Okno / harmonogram | Próg | Severity | MITRE | Encje | Grupowanie incydentów |
|---|---|---|---|---|---|---|---|
| HoneyGrid — brute-force z pojedynczego IP | `login.failed` zgrupowane po `AttackerIp`, zliczenie prób | 1 h / co 5 min | > 15 prób z jednego IP | Medium | T1110 (Brute Force) | IP, Account | po `AttackerIp` |
| HoneyGrid — rozproszony brute-force | `login.failed` na jedno konto z wielu różnych `AttackerIp` (`dcount(AttackerIp)`) | 1 h / co 5 min | > 10 różnych IP na jedno konto | Medium | T1110 (Brute Force) | Account, IP | po `Account` |
| HoneyGrid — password spraying | jedno/few haseł próbowane na wielu kontach (`dcount(Username)` przy małym `dcount(Password)`) | 1 h / co 5 min | > 10 kont, ≤ kilka haseł | Medium | T1110.003 (Password Spraying) | IP, Account | po `AttackerIp` |
| HoneyGrid — sukces po serii porażek | `login.success` z IP, które wcześniej miało serię `login.failed` (join okienkowy) | 1 h / co 5 min | sukces po ≥ N porażkach z tego IP | High | T1078 (Valid Accounts) | IP, Account, Host | po `AttackerIp` |

> Dokładna składnia KQL, progi i okna żyją w `sentinel.bicep` (właściciel: równoległy agent).
> Powyższa tabela to kontrakt logiczny — wartości progów są **wyjściowe** i strojone po
> realnym ruchu (sekcja 8).

---

## 3. Dlaczego rate-based, a nie per-event (alert fatigue)

Naiwna reguła „alert na każde `login.failed`” przy ataku Hydrą wyprodukowałaby setki alertów
na jeden incydent — to klasyczny **alert fatigue**: operator przestaje patrzeć, bo skrzynka
tonie w szumie. Reguły HoneyGrid agregują **tempo** w oknie czasowym i emitują alert dopiero
po przekroczeniu progu.

Konkret z metryk projektu (§12): pojedynczy przebieg Hydry to ~**260 zdarzeń** `login.failed`
z jednego IP. Reguła rate-based zwija to do **1 incydentu** (grupowanie po `AttackerIp`),
z licznikiem prób jako dowodem. Stosunek **260 → 1** to różnica między pulpitem, na który
operator patrzy, a takim, który ignoruje.

Grupowanie incydentów po encji (`AttackerIp` / `Account`) sprawia dodatkowo, że kolejne
przebiegi tego samego napastnika **dokładają się do istniejącego incydentu** zamiast tworzyć
nowe — jeden wątek śledczy na kampanię, nie na zapytanie.

---

## 4. Mapowanie encji jako wejście SOAR (Tydzień 6)

Każda reguła deklaruje `entityMappings` — Sentinel wyciąga z wierszy KQL konkretne **encje**:

- **IP** (`AttackerIp`) — adres napastnika; w Tygodniu 6 wejście playbooka blokującego IP na NSG;
- **Account** (`Username`) — atakowane konto; wiązanie incydentów dotyczących tej samej tożsamości;
- **Host** (`SensorId` / `SensorType`) — który honeypot oberwał.

Encje to nie kosmetyka — to **interfejs dla automatyzacji**. Playbook SOAR (Logic App,
Tydzień 6) odpala się z incydentu i czyta encję IP, żeby wiedzieć, *co* zablokować. Bez
`entityMappings` incydent jest „ślepy” dla automatyzacji. Dlatego `verify-week5.sh` twardo
sprawdza, że każda reguła ma niepuste `entityMappings`.

---

## 5. Workbook — pulpit operacyjny SOC

`infra/bicep/modules/workbook.bicep` wdraża Azure Workbook
**„HoneyGrid — Pulpit operacyjny SOC”** (kategoria `sentinel`, widoczny w
Microsoft Sentinel → Workbooks). Wdrażany **jako kod** — definicja JSON żyje w repo, nie jest
klikana w portalu, więc odtwarza się razem z resztą infrastruktury.

Co pokazuje (6 elementów, zapytania KQL do `Cowrie_CL`):

1. **Nagłówek** — opis i kontekst pulpitu.
2. **Wolumen zdarzeń w czasie wg typu sensora** (timechart, koszyki 15 min) — kiedy i przez
   który wektor (SSH/web/TCP) leci ruch.
3. **Top 10 atakujących IP** — najaktywniejsze adresy + liczba próbowanych kont.
4. **Top poświadczenia (Credential Intelligence — zalążek)** — najczęstsze pary login/hasło;
   pełna analityka słownikowa to Track B.
5. **Rozkład typów zdarzeń** (piechart) — proporcje `connect` / `login.*` / `command` / `http.request`.
6. **Top ASN / organizacje** — skąd (sieciowo) pochodzą atakujący.

> **Uwaga GeoIP:** kolumny `CountryCode`, `City`, `Asn`, `AsnOrg` wypełnia wzbogacanie GeoIP,
> które wymaga baz **MaxMind**. Bez nich element „Top ASN / organizacje” może być **pusty** —
> to udokumentowane ograniczenie, nie błąd.

---

## 6. Wierność adresu IP atakującego (ograniczenie)

Reguły korelujące po `AttackerIp` są tak dobre, jak dobry jest sam `AttackerIp`. Stan na
Tydzień 5:

- **Web-sensor (HTTP/L7):** **naprawione** — sensor czyta nagłówek `X-Forwarded-For`
  wstawiany przez ingress Container Apps, więc `AttackerIp` to realny adres klienta, a nie
  adres wewnętrzny proxy (równoległa zmiana w `src/`, Tydzień 5).
- **SSH / TCP-sensor (L4):** **ograniczenie** — ingress Container Apps na warstwie 4 (TCP)
  **nie przekazuje źródłowego IP**. Połączenia SSH/TCP widzą adres wewnętrzny platformy, nie
  napastnika. To **udokumentowany limit** infrastruktury (nie da się go obejść bez dedykowanego
  load balancera z `proxy protocol`, poza budżetem studenckim). Reguły brute-force są więc
  najwierniejsze dla wektora **web**; dla SSH/TCP traktujemy IP ostrożnie.

---

## 7. Instrukcja wdrożenia

> **Wdrożenie wykonuje użytkownik.** Grupa zasobów `hg-dev-rg` bywa kasowana między sesjami
> (oszczędność kredytu) — najpierw ją odtwórz, potem weryfikuj.

### 7.1. Jedno polecenie — wdrożenie

Warstwa detekcji to część `main.bicep` (po wpięciu modułu `workbook` — patrz sekcja 9). Wdrażamy
całość tą samą komendą subskrypcyjną co w Tygodniu 1:

```bash
az deployment sub create \
  --location swedencentral \
  --name hg-dev-w5 \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.dev.bicepparam
```

Wdrożenie jest idempotentne — dokłada reguły analityczne i Workbook, istniejące zasoby pomija.

### 7.2. Weryfikacja

```bash
./infra/scripts/verify-week5.sh            # domyślnie hg-dev-rg
./infra/scripts/verify-week5.sh inna-rg    # inna grupa
```

Skrypt sprawdza: istnienie RG i workspace'a, ≥ 4 reguły `HoneyGrid` (Scheduled, wszystkie
włączone), niepuste `entityMappings` per reguła, Workbook kategorii `sentinel`, liczbę i tytuły
incydentów, oraz wypisuje gotowy blok symulacji ataku z realnym FQDN web-sensora. Wszystkie
wywołania są bez rozszerzeń `az` (`az rest` / `az resource`).

### 7.3. Symulacja ataku (wywołanie incydentu)

`verify-week5.sh` wypisuje gotowca z podstawionym FQDN. Ręcznie (mini-Hydra, > 15 `login.failed`
z jednego IP):

```bash
for i in $(seq 1 20); do
  curl -s -X POST "https://<FQDN-web>/wp-login.php" -d "log=admin&pwd=pass$i" -o /dev/null
done
```

> **Cierpliwość:** incydent pojawia się do **~10 min** po przekroczeniu progu — to suma okna
> harmonogramu reguły (co 5 min) i opóźnienia ingestii (2–15 min, pierwsza ingestia do świeżej
> tabeli bywa wolniejsza).

### 7.4. Gdzie kliknąć w portalu

Portal Azure → **Microsoft Sentinel** → wybierz workspace `hg-dev-law`:

- **Analytics** → zakładka **Active rules** — 4 reguły `HoneyGrid — ...` (status Enabled,
  severity, MITRE).
- **Incidents** — incydenty wygenerowane po symulacji (tytuł, severity, encje, oś czasu).
- **Workbooks** → **My workbooks** → **„HoneyGrid — Pulpit operacyjny SOC”** — pulpit z 6
  elementami.

---

## 8. Strojenie progów po realnym ruchu

Progi w tabeli z sekcji 2 to wartości **wyjściowe**, dobrane „na sucho”. Po zebraniu realnego
ruchu na honeypotach należy je dostroić:

- **za nisko** → fałszywe alarmy (np. skaner internetowy robiący kilka prób ≠ atak ukierunkowany);
- **za wysoko** → przegapione wolne ataki (low-and-slow brute force schodzący pod próg).

Praktyka: po kilku dniach ruchu zapytać `Cowrie_CL` o rozkład liczby `login.failed` na IP w oknie
1 h (`summarize count() by AttackerIp, bin(TimeGenerated, 1h)`), zobaczyć, gdzie kończy się „szum
tła” (skanery), a zaczyna ogon ataków, i ustawić próg powyżej szumu. Każda zmiana progu to edycja
`sentinel.bicep` + ponowne wdrożenie (IaC — żadnej ręcznej edycji reguły w portalu, bo redeploy
ją nadpisze).

---

## 9. Co musi zrobić orkiestrator (wpięcie modułu workbook)

Moduł `workbook.bicep` jest gotowy, ale **nie jest jeszcze wpięty** w `main.bicep`. Wklej po
bloku `module sentinel ...` (zależność: `sentinel.outputs.logAnalyticsWorkspaceId`):

```bicep
// Tydzień 5: Workbook — pulpit operacyjny SOC (Sentinel → Workbooks).
module workbook 'modules/workbook.bicep' = {
  scope: rg
  name: 'workbook-${environment}'
  params: {
    environment: environment
    namePrefix: namePrefix
    location: location
    tags: tags
    logAnalyticsWorkspaceId: sentinel.outputs.logAnalyticsWorkspaceId
  }
}
```

Opcjonalnie wystaw output: `output workbookId string = workbook.outputs.workbookId`.

---

*Koniec przewodnika Tygodnia 5. Następny krok: Tydzień 6 — SOAR (playbooki Logic Apps wpinane
w incydenty przez automation rules, blokowanie IP na NSG).*
