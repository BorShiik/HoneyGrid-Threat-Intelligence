import type { Severity } from '@/types/api';

/** A HoneyGrid honeypot collection point (where attack arcs terminate). */
export interface Sensor {
  id: string;
  label: string;
  lat: number;
  lng: number;
}

/** Distributed collection sensors across regions. */
export const SENSORS: readonly Sensor[] = [
  { id: 'weu', label: 'Frankfurt', lat: 50.11, lng: 8.68 },
  { id: 'eus', label: 'Virginia', lat: 39.04, lng: -77.49 },
  { id: 'sea', label: 'Singapore', lat: 1.35, lng: 103.82 },
];

/** Haversine-free squared distance is enough to pick the nearest sensor. */
export function nearestSensor(lat: number, lng: number): Sensor {
  let best = SENSORS[0];
  let bestD = Number.POSITIVE_INFINITY;
  for (const s of SENSORS) {
    const dLat = s.lat - lat;
    const dLng = s.lng - lng;
    const d = dLat * dLat + dLng * dLng;
    if (d < bestD) {
      bestD = d;
      best = s;
    }
  }
  return best;
}

/** Solid hex colour for a severity bucket (matches the design tokens). */
export const SEVERITY_HEX: Record<Severity, string> = {
  critical: '#e11d48',
  high: '#f97316',
  medium: '#f59e0b',
  low: '#3b82f6',
};

/** RGB triple for a severity bucket (for rgba ring interpolation). */
export const SEVERITY_RGB: Record<Severity, [number, number, number]> = {
  critical: [225, 29, 72],
  high: [249, 115, 22],
  medium: [245, 158, 11],
  low: [59, 130, 246],
};

/**
 * Converts an ISO 3166-1 alpha-2 country code to its flag emoji using
 * regional indicator symbols. Returns an empty string for invalid input.
 */
export function flagEmoji(countryCode?: string): string {
  if (!countryCode || countryCode.length !== 2) return '🏴';
  const cc = countryCode.toUpperCase();
  if (!/^[A-Z]{2}$/.test(cc)) return '🏴';
  const base = 0x1f1e6;
  return String.fromCodePoint(base + (cc.charCodeAt(0) - 65), base + (cc.charCodeAt(1) - 65));
}
