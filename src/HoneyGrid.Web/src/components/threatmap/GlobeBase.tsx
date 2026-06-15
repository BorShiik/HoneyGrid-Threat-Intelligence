import { useMemo } from 'react';
import * as THREE from 'three';
import { GLOBE_RADIUS } from './globeMath';
import { buildWorldBorderGeometry } from './worldBorders';

/**
 * The "Cyber-Earth" — a dark, solid planet with real geographic borders drawn
 * as quiet vector lines. Deliberately understated so the attack arcs own the
 * frame:
 *
 *   • Black Mass core   — an almost-pitch sphere that gives the planet physical
 *                          mass and occludes the far hemisphere's borders.
 *   • Borders           — dim slate lines (LineBasicMaterial = 1px), below the
 *                          Bloom threshold so they never glow.
 *   • Atmosphere        — a subtle Fresnel rim (dark steel-blue) on a back-side
 *                          shell; no muddy halo disc.
 *
 * NOT self-rotated: borders share world space with the arcs/nodes, so the spin
 * comes from the camera (see <CameraRig>).
 */

const FRESNEL_VERTEX = /* glsl */ `
  varying vec3 vNormal;
  varying vec3 vView;
  void main() {
    vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
    vNormal = normalize(normalMatrix * normal);
    vView = normalize(-mvPosition.xyz);
    gl_Position = projectionMatrix * mvPosition;
  }
`;

const FRESNEL_FRAGMENT = /* glsl */ `
  varying vec3 vNormal;
  varying vec3 vView;
  uniform vec3 uColor;
  uniform float uIntensity;
  uniform float uPower;
  void main() {
    float fresnel = pow(1.0 - abs(dot(vView, vNormal)), uPower);
    gl_FragColor = vec4(uColor * fresnel * uIntensity, fresnel);
  }
`;

function Atmosphere() {
  const uniformsInner = useMemo(
    () => ({
      uColor: { value: new THREE.Color('#38bdf8').multiplyScalar(1.5) }, // Bright cyber-blue, multiplied for HDR bloom
      uIntensity: { value: 0.8 },
      uPower: { value: 4.5 },
    }),
    [],
  );

  const uniformsOuter = useMemo(
    () => ({
      uColor: { value: new THREE.Color('#0ea5e9') },
      uIntensity: { value: 0.35 },
      uPower: { value: 6.0 },
    }),
    [],
  );

  return (
    <group>
      {/* Inner strong glow */}
      <mesh scale={1.03}>
        <sphereGeometry args={[GLOBE_RADIUS, 64, 64]} />
        <shaderMaterial
          vertexShader={FRESNEL_VERTEX}
          fragmentShader={FRESNEL_FRAGMENT}
          uniforms={uniformsInner}
          transparent
          side={THREE.BackSide}
          blending={THREE.AdditiveBlending}
          depthWrite={false}
        />
      </mesh>
      {/* Outer soft halo */}
      <mesh scale={1.12}>
        <sphereGeometry args={[GLOBE_RADIUS, 64, 64]} />
        <shaderMaterial
          vertexShader={FRESNEL_VERTEX}
          fragmentShader={FRESNEL_FRAGMENT}
          uniforms={uniformsOuter}
          transparent
          side={THREE.BackSide}
          blending={THREE.AdditiveBlending}
          depthWrite={false}
        />
      </mesh>
    </group>
  );
}

export function GlobeBase() {
  const borders = useMemo(() => buildWorldBorderGeometry(GLOBE_RADIUS * 1.002), []);

  // Dim slate, tone-mapped (NOT HDR) so it stays below the Bloom threshold and
  // reads as quiet background context rather than a neon sign.
  const borderMaterial = useMemo(
    () =>
      new THREE.LineBasicMaterial({
        color: new THREE.Color('#f59e0b'), // Brand amber color
        transparent: true,
        opacity: 0.35, // Bright enough to see the detailed borders, dim enough to not overpower arcs
        blending: THREE.AdditiveBlending, // Give it a slight glowing neon feel
      }),
    [],
  );

  // Hex grid dot pattern for the planet surface
  const hexTexture = useMemo(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 256;
    canvas.height = 256;
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = '#050505';
    ctx.fillRect(0, 0, 256, 256);
    ctx.fillStyle = '#18181b';
    for (let y = 0; y < 256; y += 16) {
      for (let x = 0; x < 256; x += 16) {
        ctx.beginPath();
        ctx.arc(x + (y % 32 === 0 ? 0 : 8), y, 2.5, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    const tex = new THREE.CanvasTexture(canvas);
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    tex.repeat.set(32, 16);
    return tex;
  }, []);

  return (
    <group>
      {/* Black Mass core with subtle cyber-grid texture */}
      <mesh>
        <sphereGeometry args={[GLOBE_RADIUS * 0.985, 64, 64]} />
        <meshBasicMaterial map={hexTexture} color="#ffffff" />
      </mesh>

      {/* Country / coastline borders — one draw call for the whole world. */}
      <lineSegments geometry={borders} material={borderMaterial} />

      {/* Cybernetic double-layer atmosphere */}
      <Atmosphere />
    </group>
  );
}
