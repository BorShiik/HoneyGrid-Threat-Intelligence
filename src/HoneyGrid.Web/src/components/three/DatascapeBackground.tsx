import { lazy, Suspense } from 'react';
import { supportsWebGL } from '@/lib/webgl';

// Code-split the three.js scene so it never enters the bundle (or executes) on
// routes — or test environments — that don't render it.
const DatascapeScene = lazy(() => import('./DatascapeScene'));

/**
 * Full-bleed 3D backdrop for the Datascape dashboard. Renders fixed behind all
 * content (`-z-10`) so the glass panels' `backdrop-blur` picks it up.
 *
 * Degrades to a static radial-gradient nebula when WebGL is unavailable
 * (jsdom/vitest, SSR, GPU blocklist) — the UI still looks intentional, never
 * blank.
 */
export function DatascapeBackground() {
  const webgl = supportsWebGL();

  return (
    <div aria-hidden className="fixed inset-0 -z-10 overflow-hidden bg-[#09090b]">
      {/* Always-on ambient gradient — also the WebGL fallback. */}
      <div className="absolute inset-0 bg-[radial-gradient(ellipse_80%_60%_at_50%_-10%,rgba(180,83,9,0.18),transparent_60%),radial-gradient(ellipse_60%_50%_at_85%_110%,rgba(37,99,235,0.12),transparent_55%)]" />

      {webgl && (
        <Suspense fallback={null}>
          <DatascapeScene />
        </Suspense>
      )}

      {/* Vignette to seat the foreground UI. */}
      <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_center,transparent_55%,rgba(9,9,11,0.7)_100%)]" />
    </div>
  );
}
