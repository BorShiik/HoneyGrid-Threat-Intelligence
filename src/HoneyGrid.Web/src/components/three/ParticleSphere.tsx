import { lazy, Suspense } from 'react';
import { supportsWebGL } from '@/lib/webgl';

const ParticleSphereScene = lazy(() => import('./ParticleSphereScene'));

/**
 * Drop-in 3D core for a KPI card. Fills its (relatively positioned) parent.
 * Falls back to a pulsing radial glow when WebGL is unavailable, so the card
 * still has a living center.
 */
export function ParticleSphere({ color = '#f59e0b' }: { color?: string }) {
  const webgl = supportsWebGL();

  if (!webgl) {
    return (
      <div
        aria-hidden
        className="pulse-glow absolute inset-0"
        style={{
          background: `radial-gradient(circle at 50% 50%, ${color}33, transparent 60%)`,
        }}
      />
    );
  }

  return (
    <div aria-hidden className="absolute inset-0">
      <Suspense fallback={null}>
        <ParticleSphereScene color={color} />
      </Suspense>
    </div>
  );
}
