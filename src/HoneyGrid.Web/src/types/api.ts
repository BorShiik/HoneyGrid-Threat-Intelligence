/**
 * TypeScript types mirroring the shared HoneyGrid event schema
 * (kept in sync with the OpenAPI contract in HoneyGrid.Contracts).
 */

export type SensorType = 'ssh' | 'web' | 'rdp';

export type HoneypotEventType =
  | 'login.failed'
  | 'login.success'
  | 'command'
  | 'http.request'
  | 'connect';

export type Severity = 'critical' | 'high' | 'medium' | 'low';

export interface GeoInfo {
  country: string;
  countryName: string;
  city: string;
  lat: number;
  lon: number;
  asn: number;
  org: string;
}

export interface CredentialPair {
  username: string;
  password: string;
}

export interface HttpRequestInfo {
  method: string;
  path: string;
  userAgent: string;
}

export interface ThreatIntelInfo {
  knownMalicious: boolean;
  sources: string[];
  score: number;
}

export interface ClassificationInfo {
  killChainPhase: string;
  category: string;
  sophistication: string;
  intent: string;
  actorId?: string;
}

export interface HoneypotEvent {
  id: string;
  attackerIp: string;
  sensorId: string;
  sensorType: SensorType;
  timestamp: string;
  eventType: HoneypotEventType;
  sessionId?: string;
  geo?: GeoInfo;
  credentials?: CredentialPair;
  command?: string;
  downloadHash?: string;
  http?: HttpRequestInfo;
  threatIntel?: ThreatIntelInfo;
  classification?: ClassificationInfo;
  ttyRef?: string;
  rawRef?: string;
}

/* ── Stats ─────────────────────────────────────────────────────────── */

export interface StatsOverview {
  totalEvents: number;
  eventsLast24h: number;
  uniqueAttackers: number;
  activeSessions: number;
  topCountries: { country: string; countryName: string; count: number }[];
  eventsBySensorType: Record<SensorType, number>;
  eventsByType: Record<HoneypotEventType, number>;
}

export interface GeoStatPoint {
  country: string;
  countryName: string;
  lat: number;
  lon: number;
  count: number;
}

export interface GeoStats {
  points: GeoStatPoint[];
  updatedAt: string;
}

export interface CredentialStat {
  username: string;
  password: string;
  count: number;
}

export interface CredentialStats {
  topUsernames: { username: string; count: number }[];
  topPasswords: { password: string; count: number }[];
  topPairs: CredentialStat[];
  totalAttempts: number;
}

/* ── Threat actors ─────────────────────────────────────────────────── */

export interface ThreatActor {
  id: string;
  name: string;
  firstSeen: string;
  lastSeen: string;
  eventCount: number;
  knownIps: string[];
  countries: string[];
  sophistication: string;
  intent: string;
  severity: Severity;
  description?: string;
}

/* ── Session replay ────────────────────────────────────────────────── */

export interface SessionSummary {
  sessionId: string;
  attackerIp: string;
  sensorId: string;
  startedAt: string;
  durationMs: number;
  commandCount: number;
  hasTty: boolean;
  country: string;
  countryName: string;
}

export interface SessionReplayFrame {
  /** Offset from session start, in milliseconds. */
  offsetMs: number;
  /** 'i' = input (attacker), 'o' = output (honeypot). */
  type: 'i' | 'o';
  data: string;
}

export interface SessionReplay {
  sessionId: string;
  attackerIp: string;
  sensorId: string;
  startedAt: string;
  durationMs: number;
  frames: SessionReplayFrame[];
}

/* ── STIX IoC bundle (simplified) ──────────────────────────────────── */

export interface StixObject {
  type: string;
  id: string;
  created: string;
  modified: string;
  pattern?: string;
  labels?: string[];
  [key: string]: unknown;
}

export interface StixBundle {
  type: 'bundle';
  id: string;
  objects: StixObject[];
}
