import { useRef } from 'react';
import { useFrame } from '@react-three/fiber';
import * as THREE from 'three';

/**
 * The Core — a small glowing sphere suspended inside the globe that pulses
 * amber→red to telegraph "system stress". Driven entirely on the GPU-friendly
 * side via scale + emissive intensity in useFrame; Bloom turns it into a
 * volumetric heart.
 */
export function CorePulse() {
  const mesh = useRef<THREE.Mesh>(null);
  const mat = useRef<THREE.MeshBasicMaterial>(null);
  const halo = useRef<THREE.Mesh>(null);
  const color = useRef(new THREE.Color());

  useFrame((state) => {
    const t = state.clock.elapsedTime;
    // 0..1 stress oscillation (a slow base beat + a faster flutter).
    const beat = (Math.sin(t * 1.4) * 0.5 + 0.5) * 0.7 + (Math.sin(t * 4.2) * 0.5 + 0.5) * 0.3;

    if (mesh.current) {
      const s = 0.32 + beat * 0.06;
      mesh.current.scale.setScalar(s);
    }
    if (mat.current) {
      // Amber (#f59e0b) → rose (#f43f5e) as stress rises.
      color.current.set('#f59e0b').lerp(new THREE.Color('#f43f5e'), beat * 0.8).multiplyScalar(2.0);
      mat.current.color.copy(color.current);
    }
    if (halo.current) {
      halo.current.scale.setScalar(0.55 + beat * 0.25);
      (halo.current.material as THREE.MeshBasicMaterial).opacity = 0.08 + beat * 0.12;
    }
  });

  return (
    <group>
      <mesh ref={mesh}>
        <icosahedronGeometry args={[1, 2]} />
        <meshBasicMaterial ref={mat} color="#f59e0b" toneMapped={false} />
      </mesh>
      {/* Soft halo */}
      <mesh ref={halo}>
        <sphereGeometry args={[1, 24, 24]} />
        <meshBasicMaterial color="#f59e0b" transparent opacity={0.12} toneMapped={false} />
      </mesh>
    </group>
  );
}
