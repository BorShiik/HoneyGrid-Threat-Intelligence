// ============================================================================
// HoneyGrid — moduł Workbook (Tydzień 5: wizualizacja SIEM)
//
// Azure Workbook = "dorosła" wizualizacja SIEM z planu (§6.5): interaktywny
// pulpit operacyjny SOC w Microsoft Sentinel, zbudowany na zapytaniach KQL
// do tabeli Cowrie_CL. W odróżnieniu od dashboardu klikanego w portalu,
// Workbook jest tu wdrażany JAKO KOD (IaC) — definicja JSON żyje w repo,
// wersjonuje się razem z infrastrukturą i odtwarza jedną komendą po każdym
// `az group delete` (złoty nawyk projektu: kasuj po sesji, odtwarzaj z IaC).
//
// Zasób: Microsoft.Insights/workbooks, kind 'shared', category 'sentinel'
// (dzięki temu pulpit pojawia się w Sentinel → Workbooks, nie tylko
// w Azure Monitor). Nazwa zasobu MUSI być GUID-em — wymóg API workbooków;
// generujemy deterministycznie z guid(), więc redeploy jest idempotentny.
// ============================================================================

@allowed(['dev', 'prod'])
param environment string
param namePrefix string
param location string
param tags object

@description('''Pełny resource ID workspace'a Log Analytics (z modułu sentinel.bicep) —
sourceId workbooka: wszystkie zapytania KQL lecą do tego workspace'a.''')
param logAnalyticsWorkspaceId string

// Sufiks środowiska w displayName — w portalu od razu widać, na co patrzymy.
var workbookDisplayName = 'HoneyGrid — Pulpit operacyjny SOC'

// ---------------------------------------------------------------------------
// Treść workbooka jako OBIEKT bicep (schemat Notebook/1.0).
// Świadomy wybór: obiekt + string() zamiast ręcznie escapowanego giganta
// JSON-owego — kompilator pilnuje składni, a diff w PR jest czytelny.
//
// Dyscyplina KQL (te zapytania NIE są walidowane przy wdrożeniu — błąd
// wyjdzie dopiero w portalu!):
//   - wyłącznie kolumny istniejące w Cowrie_CL (źródło prawdy: cowrieColumns
//     w modules/sentinel.bicep),
//   - aliasy bez polskich znaków i bez słów zastrzeżonych KQL,
//   - wartości EventType zgodne z kontraktem sensora: 'connect',
//     'login.failed', 'login.success', 'command', 'http.request'.
// ---------------------------------------------------------------------------
var workbookContent = {
  version: 'Notebook/1.0'
  items: [
    // -- 1. Nagłówek (element tekstowy, type 1) -----------------------------
    {
      type: 1
      content: {
        json: '## HoneyGrid — aktywność honeypotów\n\nPulpit operacyjny SOC: telemetria z sensorów (SSH/web/TCP) zebrana w tabeli **Cowrie_CL**. Zakres czasu ustawiasz pickerem workbooka (domyślnie 24 h). Pulpit wdrażany jako kod (Bicep) — zmiany wyłącznie przez repo, nie przez edycję w portalu.'
      }
      name: 'header'
    }
    // -- 2. Wolumen zdarzeń w czasie wg typu sensora (timechart) ------------
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'Cowrie_CL | summarize Zdarzenia = count() by bin(TimeGenerated, 15m), SensorType | render timechart'
        size: 0
        title: 'Wolumen zdarzeń w czasie wg typu sensora'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'timechart'
      }
      name: 'events-timechart'
    }
    // -- 3. Top 10 atakujących IP --------------------------------------------
    // 'unknown' to wartość zastępcza sensora przy braku adresu — odfiltrowana
    // już w transformacji DCR, ale filtr zostaje dla danych historycznych.
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'Cowrie_CL | where AttackerIp !in (\'unknown\') | summarize Zdarzenia = count(), Konta = dcount(Username) by AttackerIp | top 10 by Zdarzenia'
        size: 0
        title: 'Top 10 atakujących IP'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
      name: 'top-attacker-ips'
    }
    // -- 4. Top poświadczenia (Credential Intelligence — zalążek) -----------
    // Zalążek modułu Credential Intelligence: które loginy/hasła atakujący
    // próbują najczęściej. Pełna analityka (trendy, słowniki) — Track B.
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'Cowrie_CL | where EventType startswith \'login.\' | summarize Proby = count() by Username, Password | top 15 by Proby'
        size: 0
        title: 'Top poświadczenia (Credential Intelligence — zalążek)'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
      name: 'top-credentials'
    }
    // -- 5. Rozkład typów zdarzeń (piechart) ---------------------------------
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'Cowrie_CL | summarize Zdarzenia = count() by EventType | render piechart'
        size: 0
        title: 'Rozkład typów zdarzeń'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'piechart'
      }
      name: 'eventtype-piechart'
    }
    // -- 6. Top ASN / organizacje --------------------------------------------
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'Cowrie_CL | where isnotempty(AsnOrg) | summarize Zdarzenia = count() by AsnOrg, CountryCode | top 10 by Zdarzenia'
        size: 0
        title: 'Top ASN / organizacje (GeoIP wymaga baz MaxMind — bez nich tabela może być pusta)'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
      name: 'top-asn'
    }
  ]
}

// ---------------------------------------------------------------------------
// Workbook — nazwa zasobu to deterministyczny GUID (wymóg API), displayName
// jest tym, co człowiek widzi w portalu (Sentinel → Workbooks → My workbooks).
// ---------------------------------------------------------------------------
resource socWorkbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  // namePrefix w ziarnie GUID-a: dwa wdrożenia z różnym prefiksem (np. 'hg'
  // i 'hg2' po konflikcie nazw) dostaną różne, ale wciąż deterministyczne nazwy.
  name: guid(logAnalyticsWorkspaceId, namePrefix, 'honeygrid-workbook')
  location: location
  tags: tags
  kind: 'shared' // widoczny dla wszystkich z dostępem do RG (nie 'user' = prywatny)
  properties: {
    displayName: '${workbookDisplayName} (${environment})'
    category: 'sentinel' // sekcja Workbooks w Microsoft Sentinel
    sourceId: logAnalyticsWorkspaceId
    version: '1.0'
    // string() serializuje obiekt bicep do JSON — dokładnie tego oczekuje API.
    serializedData: string(workbookContent)
  }
}

// ---------------------------------------------------------------------------
// Wyjścia
// ---------------------------------------------------------------------------
output workbookId string = socWorkbook.id
output workbookDisplayName string = socWorkbook.properties.displayName

// ---------------------------------------------------------------------------
// WPIĘCIE DO main.bicep (gotowe do wklejenia — robi to orkiestrator/użytkownik).
// Zależność: sentinel.outputs.logAnalyticsWorkspaceId (nazwa zweryfikowana
// w modules/sentinel.bicep). Wklej PO bloku `module sentinel ...`:
//
// // Tydzień 5: Workbook — pulpit operacyjny SOC (Sentinel → Workbooks).
// module workbook 'modules/workbook.bicep' = {
//   scope: rg
//   name: 'workbook-${environment}'
//   params: {
//     environment: environment
//     namePrefix: namePrefix
//     location: location
//     tags: tags
//     logAnalyticsWorkspaceId: sentinel.outputs.logAnalyticsWorkspaceId
//   }
// }
// ---------------------------------------------------------------------------
