using HoneyGrid.Contracts;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// HoneyGrid.Functions — funkcje Azure (.NET isolated worker).
// Planowane funkcje:
//  - EventHubTrigger: klasyfikacja zdarzeń w czasie rzeczywistym
//  - TimerTrigger:   agregacje dzienne / korelacja aktorów
//  - HttpTrigger:    eksport STIX 2.1 (TAXII-lite)

var builder = FunctionsApplication.CreateBuilder(args);

// TODO (Track C, Tydzień 5): dodać pakiet Microsoft.Azure.Functions.Worker.Extensions.EventHubs
//                            i funkcję ClassifyEvent (EventHubTrigger) używającą HoneypotEvent.
// TODO (Track C, Tydzień 6): dodać pakiet ...Extensions.Timer i funkcję DailyActorCorrelation.
// TODO (Track C, Tydzień 6): rejestracja HoneyGridJson.Options jako wspólnych opcji serializacji.

builder.Build().Run();
