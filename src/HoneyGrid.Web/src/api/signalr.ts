import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useConnectionStore } from '@/stores/connectionStore';
import type { HoneypotEvent } from '@/types/api';

/**
 * SignalR hub URL. In dev/MSW this is unused (the live hook uses a simulator).
 * In production set VITE_SIGNALR_URL to the Serverless negotiate base hosted by
 * the Functions app, e.g. https://<funcapp>.azurewebsites.net/api/hubs/attacks
 * — the SignalR client appends "/negotiate" to it automatically.
 */
export const ATTACKS_HUB_URL = import.meta.env.VITE_SIGNALR_URL ?? '/hubs/attacks';

/** Server-to-client event name carrying a HoneypotEvent payload. */
export const ATTACK_EVENT = 'attack';

export type AttackHandler = (event: HoneypotEvent) => void;

let connection: HubConnection | null = null;

/**
 * Creates (lazily) the singleton connection to /hubs/attacks and wires the
 * connection lifecycle into the Zustand connection store, so the header
 * status dot ("Połączono / Rozłączono") reflects reality.
 *
 * Week 0: this is scaffolding only — nothing calls startAttackHub() yet,
 * because there is no real backend and MSW does not intercept WebSockets.
 */
export function getAttackHubConnection(): HubConnection {
  if (connection) return connection;

  connection = new HubConnectionBuilder()
    .withUrl(ATTACKS_HUB_URL)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  const { setStatus } = useConnectionStore.getState();
  connection.onreconnecting(() => setStatus('connecting'));
  connection.onreconnected(() => setStatus('connected'));
  connection.onclose(() => setStatus('disconnected'));

  return connection;
}

export async function startAttackHub(onAttack: AttackHandler): Promise<void> {
  const hub = getAttackHubConnection();
  hub.on(ATTACK_EVENT, onAttack);

  const { setStatus } = useConnectionStore.getState();
  setStatus('connecting');
  try {
    await hub.start();
    setStatus('connected');
  } catch {
    setStatus('disconnected');
  }
}

export async function stopAttackHub(): Promise<void> {
  if (!connection) return;
  await connection.stop();
  useConnectionStore.getState().setStatus('disconnected');
}
