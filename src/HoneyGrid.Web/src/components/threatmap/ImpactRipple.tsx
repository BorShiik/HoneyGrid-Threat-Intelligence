import { useMemo, useRef } from 'react';
import { useFrame } from '@react-three/fiber';
import * as THREE from 'three';
import { GLOBE_RADIUS, latLngToVector3 } from './globeMath';

/**
 * A single expanding shock-ring that lies flat on the globe's surface and pulses
 * continuously — the visual "impact" where an attack arc lands.
 *
 * Surface alignment: a `RingGeometry` lies in the local XY plane with its normal
 * along +Z. We rotate the ring so +Z points radially outward (away from the
 * globe centre), which makes it sit perfectly tangent to the sphere. The
 * rotation is computed once with `setFromUnitVectors(+Z, surfaceNormal)`.
 *
 * Animation: scale 0 → maxScale (ease-out) while opacity 1 → 0, looped via a
 * normalised phase so many ripples can stagger into a continuous impact.
 */
export interface ImpactRippleProps {
  lat: number;
  lng: number;
  /** Globe radius the ripple sits on (defaults to the shared GLOBE_RADIUS). */
  radius?: number;
  /** HDR colour (components > 1 to trigger Bloom). Tuple or CSS string. */
  color?: [number, number, number] | string;
  /** Maximum scale the ring grows to before resetting. */
  maxScale?: number;
  /** Seconds per ripple cycle. */
  period?: number;
  /** 0–1 start offset, so stacked ripples don't fire in unison. */
  phase?: number;
  /** Overall opacity multiplier (e.g. to dim non-focused sources). */
  intensity?: number;
  /** `RingGeometry` args: [innerRadius, outerRadius, thetaSegments]. */
  ringArgs?: [number, number, number];
}

export function ImpactRipple({
  lat,
  lng,
  radius = GLOBE_RADIUS,
  color = [2, 0.2, 0.2],
  maxScale = 1.6,
  period = 1.6,
  phase = 0,
  intensity = 1,
  ringArgs = [0.01, 0.05, 32],
}: ImpactRippleProps) {
  const mesh = useRef<THREE.Mesh>(null);
  const material = useRef<THREE.MeshBasicMaterial>(null);

  // Placement + orientation, computed once per coordinate. Lifted clearly above
  // the border shell (radius * 1.002) so the ring never sits coplanar with the
  // border lines — coplanar depths z-fight and flicker as the camera rotates.
  const { position, quaternion } = useMemo(() => {
    const p = latLngToVector3(lat, lng, radius * 1.012);
    const q = new THREE.Quaternion().setFromUnitVectors(
      new THREE.Vector3(0, 0, 1),
      p.clone().normalize(),
    );
    return { position: p, quaternion: q };
  }, [lat, lng, radius]);

  // HDR colour preserved (fromArray keeps values > 1, unlike Color.set).
  const colorValue = useMemo(
    () => (Array.isArray(color) ? new THREE.Color().fromArray(color) : new THREE.Color(color)),
    [color],
  );

  useFrame((state) => {
    const t = (state.clock.elapsedTime / period + phase) % 1;
    const eased = 1 - Math.pow(1 - t, 3); // ease-out: fast burst, slow settle
    if (mesh.current) mesh.current.scale.setScalar(0.0001 + eased * maxScale);
    if (material.current) material.current.opacity = (1 - t) * intensity;
  });

  return (
    <group position={position} quaternion={quaternion}>
      {/* Fixed renderOrder keeps the additive rings from re-sorting (and
          popping) against each other every frame as the camera orbits. */}
      <mesh ref={mesh} renderOrder={3}>
        <ringGeometry args={ringArgs} />
        <meshBasicMaterial
          ref={material}
          color={colorValue}
          transparent
          depthWrite={false}
          side={THREE.DoubleSide}
          blending={THREE.AdditiveBlending}
          toneMapped={false}
        />
      </mesh>
    </group>
  );
}
