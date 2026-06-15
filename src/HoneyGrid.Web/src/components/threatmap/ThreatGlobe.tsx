import { lazy, Suspense } from 'react';
import { Globe2 } from 'lucide-react';
import { supportsWebGL } from '@/lib/webgl';
import { useReducedMotion } from '@/lib/useReducedMotion';
import type { ThreatSource } from './types';

const ThreatGlobeScene = lazy(() => import('./ThreatGlobeScene'));

function GlobeFallback({ reason }: { reason: 'loading' | 'unsupported' }) {
  return (
    <div className="flex h-full w-full items-center justify-center">
      <div className="relative flex flex-col items-center gap-4">
        <div className="absolute -inset-10 rounded-full bg-[radial-gradient(circle,rgba(245,158,11,0.18),transparent_70%)] blur-2xl" />
        <Globe2 className="relative h-16 w-16 text-amber-500/70 [animation:pulse-glow_2s_ease-in-out_infinite]" />
        {reason === 'loading' && (
          <span className="relative font-mono text-xs uppercase tracking-widest text-zinc-500">
            initializing datascape…
          </span>
        )}
      </div>
    </div>
  );
}

/**
 * WebGL/reduced-motion gate for the 3D globe. Renders a glowing static
 * placeholder when the canvas can't (or shouldn't) run — the HUD overlay stays
 * fully functional on top either way.
 */
export function ThreatGlobe({ sources }: { sources: ThreatSource[] }) {
  const reducedMotion = useReducedMotion();
  const enabled = supportsWebGL() && !reducedMotion;

  if (!enabled) return <GlobeFallback reason="unsupported" />;

  return (
    <Suspense fallback={<GlobeFallback reason="loading" />}>
      <ThreatGlobeScene sources={sources} />
    </Suspense>
  );
}
