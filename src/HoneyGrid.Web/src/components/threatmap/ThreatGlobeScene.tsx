import { Canvas } from '@react-three/fiber';
import { Stars } from '@react-three/drei';
import { GlobeBase } from './GlobeBase';
import { CorePulse } from './CorePulse';
import { AttackArcs } from './AttackArcs';
import { CameraRig } from './CameraRig';
import type { ThreatSource } from './types';

/**
 * Root R3F canvas for the Threat Map. Heavy (three + postprocessing), so it is
 * code-split behind React.lazy and only mounted by <ThreatGlobe> when WebGL is
 * available and the user hasn't requested reduced motion.
 *
 * Everything emissive (dot-matrix shell, core, arcs) is pushed through an
 * EffectComposer → Bloom pass so the neon glow is physically additive rather
 * than faked with CSS shadows.
 */
import React from 'react';

const StaticGlobe = React.memo(() => (
  <>
    <Stars radius={60} depth={40} count={1400} factor={3} saturation={0} fade speed={0.5} />
    <GlobeBase />
    <CorePulse />
  </>
));

export default function ThreatGlobeScene({ sources }: { sources: ThreatSource[] }) {
  return (
    <Canvas
      dpr={[1, 1.75]}
      gl={{ antialias: true, alpha: false, powerPreference: 'high-performance' }}
      camera={{ position: [0, 1.4, 6], fov: 45 }}
    >
      <color attach="background" args={['#050505']} />
      <fog attach="fog" args={['#050505', 7, 16]} />

      {/* Static environment elements isolated from rapid React updates */}
      <StaticGlobe />

      {/* Dynamic elements that update with live data */}
      <AttackArcs sources={sources} />
      <CameraRig sources={sources} />
    </Canvas>
  );
}
