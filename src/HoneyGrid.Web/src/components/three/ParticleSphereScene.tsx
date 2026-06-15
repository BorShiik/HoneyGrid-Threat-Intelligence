import { useMemo, useRef } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import { Points, PointMaterial } from '@react-three/drei';
import * as THREE from 'three';

/**
 * A slowly rotating sphere of particles — sits behind the big number on the
 * "Total Events" KPI card as a living, holographic core. Heavy (three.js), so
 * mounted only via <ParticleSphere> behind a WebGL guard + React.lazy.
 */

const COUNT = 700;

/**
 * Even-ish distribution on a sphere surface (golden-spiral) with a little
 * radial jitter so it reads as a shell, not a wireframe.
 */
function makeSphereShell(): Float32Array {
  const arr = new Float32Array(COUNT * 3);
  const golden = Math.PI * (3 - Math.sqrt(5));
  for (let i = 0; i < COUNT; i += 1) {
    const y = 1 - (i / (COUNT - 1)) * 2;
    const radius = Math.sqrt(1 - y * y);
    const theta = golden * i;
    const r = 1 + (Math.random() - 0.5) * 0.12;
    arr[i * 3 + 0] = Math.cos(theta) * radius * r;
    arr[i * 3 + 1] = y * r;
    arr[i * 3 + 2] = Math.sin(theta) * radius * r;
  }
  return arr;
}

function Sphere({ color }: { color: string }) {
  const ref = useRef<THREE.Points>(null);
  const positions = useMemo(() => makeSphereShell(), []);

  useFrame((state, delta) => {
    if (!ref.current) return;
    ref.current.rotation.y += delta * 0.35;
    ref.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.2) * 0.2;
  });

  return (
    <Points ref={ref} positions={positions} frustumCulled={false}>
      <PointMaterial
        transparent
        color={color}
        size={0.035}
        sizeAttenuation
        depthWrite={false}
        opacity={0.9}
        blending={THREE.AdditiveBlending}
      />
    </Points>
  );
}

export default function ParticleSphereScene({ color = '#f59e0b' }: { color?: string }) {
  return (
    <Canvas
      dpr={[1, 1.5]}
      gl={{ antialias: true, alpha: true, powerPreference: 'low-power' }}
      camera={{ position: [0, 0, 3], fov: 50 }}
      style={{ pointerEvents: 'none' }}
    >
      <Sphere color={color} />
    </Canvas>
  );
}
