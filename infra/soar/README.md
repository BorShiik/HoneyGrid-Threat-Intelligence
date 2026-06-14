# HoneyGrid SOAR — playbook `block-ip` (Tydzień 6, Track A)

Automatyczna mitygacja zagrożenia (SOAR) sterowana incydentami Microsoft
Sentinel. Gdy reguła analityczna Sentinela utworzy incydent (Tydzień 5),
playbook (Logic App Consumption) wyciąga encję IP atakującego i blokuje go
w infrastrukturze — bez udziału człowieka, mierząc MTTR (Mean Time To Respond).

## Co to jest playbook

Playbook to przepływ pracy w **Azure Logic Apps (Consumption)** podpięty do
Sentinela jako *automation rule*. Definicja przepływu żyje w
`playbook-block-ip.json` (jedyne źródło prawdy, recenzowane osobno jak pliki
`.kql`), a infrastrukturę (Logic App + połączenie API + reguła automatyzacji)
tworzy `infra/bicep/modules/soar.bicep`.

## Przepływ (Incident → mitygacja → zamknięcie)

```
Incydent Sentinela (severity High/Medium, utworzony)
        │  automation rule "HoneyGrid — auto-mitygacja: blokuj IP atakującego"
        ▼
Trigger: Microsoft_Sentinel_incident
        ▼
Entities - Get IPs         ← wyciąga encje IP z incydentu (tablica)
        ▼
For_each_IP:
   ├─ Blokuj_w_NSG         ← PUT reguły Deny do NSG strefy DMZ (Block-<IP>)
   ├─ Upewnij_blob_EDL     ← PUT (AppendBlob) — gwarancja, że blob EDL istnieje
   ├─ Dopisz_do_EDL        ← PUT ?comp=appendblock — dopisuje linię z IP
   └─ Dopisz_IP_do_listy   ← akumuluje IP do podsumowania
        ▼
Powiadom_webhook (warunek: webhookUrl != '')
   └─ POST { incident, attackerIps, action:'blocked', nsg, mttrSeconds }
        ▼
Komentarz_do_incydentu     ← komentarz PL: zablokowane IP + NSG + EDL + MTTR
        ▼
Zamknij_incydent           ← status Closed / TruePositive / "Zautomatyzowana mitygacja SOAR"
```

## Dlaczego bezkluczowo (Managed Identity)

Playbook NIE używa żadnych sekretów ani connection stringów. Wszystkie wywołania
idą przez **istniejącą tożsamość user-assigned** `hg-{env}-id-playbook`
(utworzoną w Tygodniu 1):

- **NSG (Deny rule)** — akcja HTTP `PUT` na `management.azure.com` z
  `authentication.type = ManagedServiceIdentity` (audience
  `https://management.azure.com/`). Tożsamość ma rolę *Network Contributor*
  zawężoną do NSG DMZ. Świadomie nie używamy konektora NSG — czysty REST + MSI.
- **EDL (blob)** — akcja HTTP `PUT` na `*.blob.core.windows.net` z MSI
  (audience `https://storage.azure.com/`). Tożsamość ma rolę *Storage Blob Data
  Contributor* (dodawaną równolegle w module RBAC).
- **Sentinel (komentarz, zamknięcie)** — przez połączenie API
  `Microsoft.Web/connections` z `parameterValueType: 'Alternative'`, czyli
  konektor Sentinela uwierzytelnia się tą samą tożsamością playbooka, a nie
  OAuth/sekretem. Wiązanie `$connections` ustawia
  `connectionProperties.authentication = ManagedServiceIdentity`.

To realizuje filar architektury bezkluczowej projektu: zero sekretów w IaC,
zero rotacji, pełny audyt przez RBAC.

## EDL jako mechanizm

**EDL (External Dynamic List)** to plik tekstowy z adresami IP (jedna linia =
jeden adres) wystawiony pod stałym URL-em, który zapory **PAN-OS (Palo Alto)**
lub **FortiGate** cyklicznie odpytują i automatycznie blokują wszystko z listy.
HoneyGrid dopisuje każde zablokowane IP do bloba `edl/blocked-ips.txt` jako
**Append Blob** (`x-ms-blob-type: AppendBlob`, dopisywanie przez
`?comp=appendblock`) — operacja tania i odporna na wyścigi przy pętli po IP.

W demo blob jest **prywatny** — prawdziwy EDL udostępnia się firewallowi przez
**SAS** lub kontener publiczny. Świadomie **nie osłabiamy** storage w IaC
(żadnego `publicAccess` na kontenerze): odsłonięcie EDL to decyzja operacyjna
poza tym modułem, nie domyślny stan infrastruktury.

## Ręczne uruchomienie na incydencie (przed automation rule)

Zanim zaufasz automatyzacji, odpal playbook ręcznie na testowym incydencie:

1. Microsoft Sentinel → **Incidents** → wybierz incydent z encją IP.
2. Menu **Actions** → **Run playbook**.
3. Wybierz `hg-{env}-pb-block-ip` → **Run**.
4. Sprawdź: reguła `Block-<IP>` w NSG DMZ, linia w `edl/blocked-ips.txt`,
   POST na webhooku, komentarz i status *Closed* na incydencie.

Automation rule robi dokładnie to samo automatycznie przy utworzeniu incydentu
o severity High/Medium.

## Jak cofnąć blok

Reguły Deny są nazwane `Block-<IP>` (dwukropki w IPv6 zamienione na `-`).
Aby odblokować:

- **Portal**: NSG DMZ → *Inbound security rules* → usuń regułę `Block-<IP>`.
- **CLI**:
  ```bash
  az network nsg rule delete -g <rg> --nsg-name hg-<env>-nsg-dmz -n Block-<IP>
  ```
- **EDL**: usuń odpowiednią linię z `edl/blocked-ips.txt` (lub wyczyść blob),
  żeby firewall przestał blokować przy następnym odpytaniu.

## Webhook do testu

Ustaw parametr `notifyWebhookUrl` na publiczny adres testowy, np. z
[https://webhook.site](https://webhook.site) — dostaniesz tam JSON
`{ incident, incidentNumber, attackerIps, action, nsg, mttrSeconds }` przy każdej
mitygacji. Pusty `notifyWebhookUrl` => krok powiadomienia jest pomijany
(warunek `webhookUrl != ''` w przepływie). To NIE jest kanał Teams — webhook HTTP
jest świadomie wybranym kanałem powiadomień.

## Uwaga o walidacji

> Definicji workflow (`playbook-block-ip.json`) **nie da się zwalidować w
> runtime lokalnie** — nie ma tu Azure (RG bywa kasowana między sesjami).
> Kompilacja Bicepa potwierdza tylko, że JSON jest poprawny składniowo i
> osadza się w ARM (`loadJsonContent`). **Pierwsze ręczne uruchomienie na
> testowym incydencie** jest właściwą weryfikacją działania przepływu.
