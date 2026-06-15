import * as THREE from 'three';
import { mesh } from 'topojson-client';
import worldRaw from 'world-atlas/countries-110m.json';
import { latLngToVector3 } from './globeMath';

/**
 * Builds the "Cyber-Earth" border geometry: every coastline and country
 * boundary from a low-res (110m) world map, projected onto the globe with the
 * exact same `latLngToVector3` the attack arcs use — so borders and arcs align
 * perfectly.
 *
 * `topojson.mesh` returns the *deduplicated* boundary network as a single
 * MultiLineString (shared borders aren't drawn twice), which we flatten into
 * one `LineSegments` buffer — a single draw call for the whole world.
 *
 * The decoded geometry is cached per-radius (the source data never changes), so
 * remounting the map is free.
 */

// Derive the exact parameter types `mesh` expects from its own typings, so we
// can cast the imported JSON without pulling in topojson-specification directly.
type Topology = Parameters<typeof mesh>[0];
type GeometryObj = Parameters<typeof mesh>[1];

let cached: THREE.BufferGeometry | null = null;
let cachedRadius = 0;

export function buildWorldBorderGeometry(radius: number): THREE.BufferGeometry {
  if (cached && cachedRadius === radius) return cached;

  const topology = worldRaw as unknown as Topology;
  const countries = (worldRaw as unknown as { objects: { countries: GeometryObj } }).objects.countries;
  const boundaries = mesh(topology, countries);

  const positions: number[] = [];
  for (const line of boundaries.coordinates) {
    for (let i = 0; i < line.length - 1; i += 1) {
      const a = latLngToVector3(line[i][1], line[i][0], radius);
      const b = latLngToVector3(line[i + 1][1], line[i + 1][0], radius);
      positions.push(a.x, a.y, a.z, b.x, b.y, b.z);
    }
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));

  cached = geometry;
  cachedRadius = radius;
  return geometry;
}
