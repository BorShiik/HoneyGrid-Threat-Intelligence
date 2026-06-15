import { useMemo, useRef } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import { Grid, Points, PointMaterial } from '@react-three/drei';
import * as THREE from 'three';

/**
 * The 3D "Datascape" — an abstract, slow-moving backdrop the glass UI floats
 * over. Heavy (pulls in three.js), so it is code-split behind React.lazy and
 * only mounted by <DatascapeBackground> when WebGL is available.
 *
 * Composition:
 *   • a receding neon grid floor (amber section lines) for depth + horizon,
 *   • a drifting particle nebula that parallaxes with the camera,
 *   • a couple of faint volumetric "data motes" that bob independently.
 *
 * Deliberately calm: nothing competes with the foreground data. Tuned for a
 * locked 1–1.5 DPR and a modest particle count so it idles cheaply.
 */

const PARTICLE_COUNT = 1800;

/** Builds a stable random point cloud inside a wide, shallow box. */
function makeParticleCloud(): Float32Array {
  const arr = new Float32Array(PARTICLE_COUNT * 3);
  for (let i = 0; i < PARTICLE_COUNT; i += 1) {
    arr[i * 3 + 0] = (Math.random() - 0.5) * 28; // x
    arr[i * 3 + 1] = (Math.random() - 0.5) * 14; // y
    arr[i * 3 + 2] = (Math.random() - 0.5) * 22; // z
  }
  return arr;
}

function ParticleField() {
  const ref = useRef<THREE.Points>(null);
  const positions = useMemo(() => makeParticleCloud(), []);

  useFrame((state, delta) => {
    if (!ref.current) return;
    // Gentle yaw + a breathing tilt driven by elapsed time.
    ref.current.rotation.y += delta * 0.02;
    ref.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.05) * 0.06;
  });

  return (
    <Points ref={ref} positions={positions} frustumCulled={false}>
      <PointMaterial
        transparent
        color="#fbbf24"
        size={0.045}
        sizeAttenuation
        depthWrite={false}
        opacity={0.55}
        blending={THREE.AdditiveBlending}
      />
    </Points>
  );
}

function DriftingMotes() {
  const group = useRef<THREE.Group>(null);
  useFrame((state) => {
    if (!group.current) return;
    group.current.children.forEach((child, i) => {
      child.position.y = Math.sin(state.clock.elapsedTime * 0.3 + i * 2) * 1.5;
    });
  });
  return (
    <group ref={group}>
      {[
        { x: -6, z: -4, c: '#f43f5e' },
        { x: 7, z: -6, c: '#3b82f6' },
        { x: 2, z: 3, c: '#f59e0b' },
      ].map((m, i) => (
        <mesh key={i} position={[m.x, 0, m.z]}>
          <sphereGeometry args={[0.06, 12, 12]} />
          <meshBasicMaterial color={m.c} toneMapped={false} />
        </mesh>
      ))}
    </group>
  );
}

export default function DatascapeScene() {
  return (
    <Canvas
      dpr={[1, 1.5]}
      gl={{ antialias: true, alpha: true, powerPreference: 'low-power' }}
      camera={{ position: [0, 1.4, 10], fov: 55 }}
      style={{ pointerEvents: 'none' }}
    >
      <fog attach="fog" args={['#09090b', 9, 26]} />
      <ambientLight intensity={0.4} />

      <ParticleField />
      <DriftingMotes />

      {/* Receding grid floor — the "horizon" of the datascape. */}
      <Grid
        position={[0, -3.2, 0]}
        args={[60, 60]}
        cellSize={0.8}
        cellThickness={0.6}
        cellColor="#27272a"
        sectionSize={4}
        sectionThickness={1}
        sectionColor="#b45309"
        fadeDistance={30}
        fadeStrength={4}
        infiniteGrid
      />
    </Canvas>
  );
}
