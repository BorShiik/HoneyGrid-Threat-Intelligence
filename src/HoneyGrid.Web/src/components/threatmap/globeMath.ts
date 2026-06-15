import * as THREE from 'three';

/** Globe radius in scene units — every other measure is relative to this. */
export const GLOBE_RADIUS = 2;

/** Central "Data Center" node where all attack arcs terminate (Frankfurt). */
export const HUB = { lat: 50.11, lng: 8.68, label: 'HoneyGrid Core' };

/**
 * Maps geographic coordinates to a point on a sphere of the given radius.
 * Standard lat/lng → spherical conversion (lng+180 keeps texture-space parity).
 */
export function latLngToVector3(lat: number, lng: number, radius = GLOBE_RADIUS): THREE.Vector3 {
  const phi = (90 - lat) * (Math.PI / 180);
  const theta = (lng + 180) * (Math.PI / 180);
  return new THREE.Vector3(
    -radius * Math.sin(phi) * Math.cos(theta),
    radius * Math.cos(phi),
    radius * Math.sin(phi) * Math.sin(theta),
  );
}

/**
 * Builds a quadratic Bézier curve arcing from a surface origin up to a lifted
 * midpoint and back down to the hub — the higher the apex, the longer the hop.
 */
export function buildArcCurve(
  start: THREE.Vector3,
  end: THREE.Vector3,
): { curve: THREE.QuadraticBezierCurve3; mid: THREE.Vector3 } {
  const distance = start.distanceTo(end);
  const lift = 1 + distance * 0.35;
  const mid = start
    .clone()
    .add(end)
    .multiplyScalar(0.5)
    .normalize()
    .multiplyScalar(GLOBE_RADIUS * lift);
  return { curve: new THREE.QuadraticBezierCurve3(start, mid, end), mid };
}

/** Solid threat colour from a 0–100 intel score (matches design tokens). */
export function scoreColor(score: number): string {
  if (score >= 80) return '#f43f5e';
  if (score >= 60) return '#f97316';
  if (score >= 30) return '#f59e0b';
  return '#3b82f6';
}
