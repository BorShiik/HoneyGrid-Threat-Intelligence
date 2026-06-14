import type { HoneypotEvent, HoneypotEventType, SensorType, Severity } from '@/types/api';

/** Polish labels for honeypot event types. */
export const EVENT_TYPE_LABELS: Record<HoneypotEventType, string> = {
  'login.failed': 'Nieudane logowanie',
  'login.success': 'Udane logowanie',
  command: 'Komenda',
  'http.request': 'Żądanie HTTP',
  connect: 'Połączenie',
};

/** Short uppercase labels for the three sensor types. */
export const SENSOR_LABELS: Record<SensorType, string> = {
  ssh: 'SSH',
  web: 'WEB',
  rdp: 'RDP',
};

const intFmt = new Intl.NumberFormat('pl-PL');

/** Formats an integer with Polish thousands separators. */
export function formatInt(value: number): string {
  return intFmt.format(Math.round(value));
}

/** Compact form (12 345 → "12,3 tys.") for KPI tiles. */
export function formatCompact(value: number): string {
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace('.', ',')} mln`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1).replace('.', ',')} tys.`;
  return formatInt(value);
}

/**
 * Derives a severity bucket for an event from its threat-intel score and the
 * event type. Used for colour-coding the live feed and the map.
 */
export function eventSeverity(event: HoneypotEvent): Severity {
  if (event.eventType === 'login.success') return 'critical';
  const score = event.threatIntel?.score ?? 0;
  if (event.threatIntel?.knownMalicious && score >= 80) return 'critical';
  if (score >= 60) return 'high';
  if (score >= 30) return 'medium';
  return 'low';
}

/** Human-readable one-line summary of an event's payload. */
export function eventDetails(event: HoneypotEvent): string {
  if (event.credentials) return `${event.credentials.username} / ${event.credentials.password}`;
  if (event.command) return event.command;
  if (event.http) return `${event.http.method} ${event.http.path}`;
  if (event.downloadHash) return `sha256:${event.downloadHash.slice(0, 16)}…`;
  return '—';
}

/** Tailwind text-colour class for a severity bucket. */
export const SEVERITY_TEXT: Record<Severity, string> = {
  critical: 'text-severity-critical',
  high: 'text-severity-high',
  medium: 'text-severity-medium',
  low: 'text-severity-low',
};

/** Tailwind background-colour class for a severity bucket. */
export const SEVERITY_BG: Record<Severity, string> = {
  critical: 'bg-severity-critical',
  high: 'bg-severity-high',
  medium: 'bg-severity-medium',
  low: 'bg-severity-low',
};
