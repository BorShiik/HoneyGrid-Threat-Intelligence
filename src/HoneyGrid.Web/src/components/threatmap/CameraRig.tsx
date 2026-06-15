import { useMemo, useRef } from 'react';
import { useFrame, useThree } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import type { OrbitControls as OrbitControlsImpl } from 'three-stdlib';
import * as THREE from 'three';
import { latLngToVector3 } from './globeMath';
import { useThreatMapStore } from '@/stores/threatMapStore';
import type { ThreatSource } from './types';

/**
 * OrbitControls + an auto-focus rig.
 *
 * Idle: the globe slowly auto-rotates. When the HUD sets `hoveredCountry`, the
 * camera smoothly orbits (keeping its current zoom distance) until that country
 * faces the viewer, then holds. Releasing hover resumes auto-rotation.
 *
 * Reads the store via `getState()` inside `useFrame` so per-frame focus never
 * triggers a React re-render of the canvas.
 */
export function CameraRig({ sources }: { sources: ThreatSource[] }) {
  const controls = useRef<OrbitControlsImpl>(null);
  const { camera } = useThree();
  const desired = useRef(new THREE.Vector3());

  // Precompute each country's surface direction once.
  const dirs = useMemo(() => {
    const map = new Map<string, THREE.Vector3>();
    for (const s of sources) {
      map.set(s.country, latLngToVector3(s.lat, s.lng).normalize());
    }
    return map;
  }, [sources]);

  useFrame(() => {
    const c = controls.current;
    if (!c) return;
    const hovered = useThreatMapStore.getState().hoveredCountry;
    const dir = hovered ? dirs.get(hovered) : undefined;

    if (dir) {
      c.autoRotate = false;
      // Aim the camera along the country's normal, preserving zoom distance,
      // nudged slightly "north" so the focus sits in the upper-third of view.
      desired.current
        .copy(dir)
        .multiplyScalar(camera.position.length())
        .add(new THREE.Vector3(0, 0.4, 0));
      camera.position.lerp(desired.current, 0.06);
    } else {
      c.autoRotate = true;
    }
    c.update();
  });

  return (
    <OrbitControls
      ref={controls}
      makeDefault
      enablePan={false}
      enableZoom
      autoRotate
      autoRotateSpeed={0.45}
      enableDamping
      dampingFactor={0.08}
      minDistance={3}
      maxDistance={9}
      rotateSpeed={0.5}
    />
  );
}
