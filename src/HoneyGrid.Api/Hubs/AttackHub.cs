using Microsoft.AspNetCore.SignalR;

namespace HoneyGrid.Api.Hubs;

/// <summary>
/// Hub SignalR strumieniujący zdarzenia ataków do dashboardu (mapa na żywo).
/// Klienci: HoneyGrid.Web (Blazor). Serwer wysyła metodę "attackReceived"
/// z payloadem HoneypotEvent po każdym sklasyfikowanym zdarzeniu.
/// </summary>
public sealed class AttackHub : Hub
{
    // TODO (Track D, Tydzień 6): grupy per-region/per-sensor (Groups.AddToGroupAsync),
    //                            aby dashboard mógł filtrować strumień.
    // TODO (Track D, Tydzień 6): metoda serwera BroadcastAttack(HoneypotEvent evt)
    //                            wywoływana z pipeline'u ingestii (backplane: Azure SignalR Service).
}
