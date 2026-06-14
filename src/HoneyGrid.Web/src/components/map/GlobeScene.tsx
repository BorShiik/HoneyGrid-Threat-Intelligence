import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import GlobeGL from 'react-globe.gl';

// react-globe.gl ships loose prop types; alias to a permissive component so this
// reusable wrapper stays build-safe regardless of the library's typings.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const Globe: any = GlobeGL;

export interface ArcDatum {
  id: string;
  startLat: number;
  startLng: number;
  endLat: number;
  endLng: number;
  color: string;
}

export interface RingDatum {
  id: string;
  lat: number;
  lng: number;
  rgb: [number, number, number];
}

export interface PointDatum {
  id: string;
  lat: number;
  lng: number;
  color: string;
  size: number;
}

interface GlobeSceneProps {
  arcs: ArcDatum[];
  rings: RingDatum[];
  points: PointDatum[];
}

const EARTH_TEXTURE = 'https://unpkg.com/three-globe/example/img/earth-dark.jpg';
const EARTH_BUMP = 'https://unpkg.com/three-globe/example/img/earth-topology.png';

/**
 * Interactive 3D threat globe (react-globe.gl / Three.js).
 *
 * Dark cybernetic earth with an amber atmosphere, live attack arcs that run a
 * glowing pulse from origin → sensor, and pulsing LED rings at impact points.
 * Auto-rotates when idle; full orbit + zoom on interaction.
 *
 * Loaded lazily by AttackMapPage so Three.js never enters the bundle for routes
 * (or tests) that don't show the map.
 */
export default function GlobeScene({ arcs, rings, points }: GlobeSceneProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const globeRef = useRef<any>(null);
  const [size, setSize] = useState({ w: 800, h: 600 });

  // Track the container size so the canvas fills the panel responsively.
  useLayoutEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const update = () => setSize({ w: el.clientWidth, h: el.clientHeight });
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Auto-rotation, initial point of view, gentle zoom limits.
  useEffect(() => {
    const g = globeRef.current;
    if (!g) return;
    const controls = g.controls();
    controls.autoRotate = true;
    controls.autoRotateSpeed = 0.45;
    controls.enableZoom = true;
    controls.minDistance = 180;
    controls.maxDistance = 520;
    g.pointOfView({ lat: 25, lng: 12, altitude: 2.3 }, 0);
  }, []);

  return (
    <div ref={containerRef} className="absolute inset-0">
      <Globe
        ref={globeRef}
        width={size.w}
        height={size.h}
        backgroundColor="rgba(0,0,0,0)"
        globeImageUrl={EARTH_TEXTURE}
        bumpImageUrl={EARTH_BUMP}
        showAtmosphere
        atmosphereColor="#f59e0b"
        atmosphereAltitude={0.16}
        // ── Threat arcs (origin → sensor) ──
        arcsData={arcs}
        arcColor={(d: object) => (d as ArcDatum).color}
        arcStroke={0.6}
        arcAltitudeAutoScale={0.4}
        arcDashLength={0.45}
        arcDashGap={0.25}
        arcDashInitialGap={() => Math.random()}
        arcDashAnimateTime={1500}
        arcsTransitionDuration={0}
        // ── Pulsing LED rings (impacts + sensors) ──
        ringsData={rings}
        ringColor={(d: object) => {
          const [r, g, b] = (d as RingDatum).rgb;
          return (t: number) => `rgba(${r},${g},${b},${1 - t})`;
        }}
        ringMaxRadius={4}
        ringPropagationSpeed={2.2}
        ringRepeatPeriod={900}
        // ── Sensor / origin points ──
        pointsData={points}
        pointLat={(d: object) => (d as PointDatum).lat}
        pointLng={(d: object) => (d as PointDatum).lng}
        pointColor={(d: object) => (d as PointDatum).color}
        pointAltitude={0.012}
        pointRadius={(d: object) => (d as PointDatum).size}
        pointResolution={6}
      />
    </div>
  );
}
