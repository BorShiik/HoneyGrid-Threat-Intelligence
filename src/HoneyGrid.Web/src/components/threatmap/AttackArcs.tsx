import { useMemo, useRef } from 'react';
import { useFrame } from '@react-three/fiber';
import { QuadraticBezierLine } from '@react-three/drei';
import * as THREE from 'three';
import { buildArcCurve, HUB, latLngToVector3 } from './globeMath';
import { ImpactRipple } from './ImpactRipple';
import { useThreatMapStore } from '@/stores/threatMapStore';
import type { ThreatSource } from './types';

/** Staggered phases for the continuous impact at the hub endpoint. */
const HUB_RIPPLE_PHASES = [0, 0.33, 0.66];

/**
 * 3D attack traffic — one glowing quadratic Bézier per source country, each
 * carrying a travelling "packet" pulse from origin → hub. Hovering a country in
 * the HUD (via the store) spotlights its arc and dims the rest.
 */

function arcColor(ratio: number): string {
  if (ratio > 0.66) return '#f43f5e';
  if (ratio > 0.33) return '#f59e0b';
  return '#3b82f6';
}

/** Deterministic 0–1 phase from a country code, so packets stagger without RNG. */
function phaseFromCode(code: string): number {
  let h = 0;
  for (let i = 0; i < code.length; i += 1) h = (h * 31 + code.charCodeAt(i)) % 997;
  return h / 997;
}

function Arc({
  source,
  color,
  hovered,
  showRipple,
}: {
  source: ThreatSource;
  color: string;
  hovered: string | null;
  showRipple: boolean;
}) {
  const pulse = useRef<THREE.Group>(null);
  const offset = useMemo(() => phaseFromCode(source.country), [source.country]);

  const { start, end, mid, curve } = useMemo(() => {
    const s = latLngToVector3(source.lat, source.lng);
    const e = latLngToVector3(HUB.lat, HUB.lng);
    const { curve, mid } = buildArcCurve(s, e);
    return { start: s, end: e, mid, curve };
  }, [source.lat, source.lng]);

  // HDR (>1) copy of the arc colour so the emissive nodes/packet punch through
  // the raised Bloom threshold and pop against the dark planet.
  const emissive = useMemo(() => new THREE.Color(color).multiplyScalar(2.4), [color]);

  const focused = hovered === source.country;
  const dimmed = hovered != null && !focused;
  const opacity = dimmed ? 0.06 : focused ? 1 : 0.6;

  useFrame((state) => {
    if (!pulse.current) return;
    pulse.current.visible = !dimmed;
    const t = (state.clock.elapsedTime * 0.45 + offset) % 1;
    pulse.current.position.copy(curve.getPoint(t));
    const s = focused ? 0.045 : 0.028;
    pulse.current.scale.setScalar(s);
  });

  return (
    <group>
      <QuadraticBezierLine
        start={start}
        end={end}
        mid={mid}
        color={emissive}
        lineWidth={focused ? 2.4 : 1.3}
        transparent
        opacity={opacity}
      />
      {/* Origin node */}
      <mesh position={start}>
        <sphereGeometry args={[0.024, 10, 10]} />
        <meshBasicMaterial color={emissive} transparent opacity={dimmed ? 0.15 : 1} toneMapped={false} />
      </mesh>
      {/* Active-source ripple at the launch point. Rendered for all, but invisible if !showRipple to prevent React tearing. */}
      <ImpactRipple
        lat={source.lat}
        lng={source.lng}
        color={color}
        maxScale={0.85}
        period={2.2}
        phase={offset}
        intensity={showRipple ? (dimmed ? 0.12 : focused ? 1 : 0.45) : 0}
        ringArgs={[0.008, 0.03, 32]}
      />
      {/* Travelling packet with fake glow halo */}
      <group ref={pulse}>
        <mesh>
          <sphereGeometry args={[1, 8, 8]} />
          <meshBasicMaterial color={emissive} toneMapped={false} />
        </mesh>
        <mesh>
          <sphereGeometry args={[2.5, 12, 12]} />
          <meshBasicMaterial
            color={emissive}
            transparent
            opacity={0.15}
            depthWrite={false}
            blending={THREE.AdditiveBlending}
            toneMapped={false}
          />
        </mesh>
      </group>
    </group>
  );
}

export function AttackArcs({ sources }: { sources: ThreatSource[] }) {
  const hovered = useThreatMapStore((s) => s.hoveredCountry);

  const colored = useMemo(() => {
    const max = Math.max(1, ...sources.map((s) => s.count));
    return sources.map((s) => ({ source: s, color: arcColor(s.count / max) }));
  }, [sources]);

  const hub = useMemo(() => latLngToVector3(HUB.lat, HUB.lng), []);

  return (
    <group>
      {colored.map(({ source, color }, i) => (
        <Arc
          key={source.country}
          source={source}
          color={color}
          hovered={hovered}
          showRipple={i < 6}
        />
      ))}

      {/* The Data Center hub node (emerald = our infrastructure) */}
      <mesh position={hub}>
        <sphereGeometry args={[0.05, 16, 16]} />
        <meshBasicMaterial color="#10b981" toneMapped={false} />
      </mesh>

      {/* Continuous neon-red impact at the endpoint where every arc lands.
          An array of staggered ripples reads as one rolling shockwave. */}
      {HUB_RIPPLE_PHASES.map((phase) => (
        <ImpactRipple
          key={phase}
          lat={HUB.lat}
          lng={HUB.lng}
          color={[2, 0.2, 0.2]}
          maxScale={2}
          period={1.6}
          phase={phase}
          ringArgs={[0.015, 0.06, 48]}
        />
      ))}
    </group>
  );
}
