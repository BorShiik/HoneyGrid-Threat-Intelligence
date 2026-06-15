import { useEffect, useState } from 'react';

/**
 * Tracks the user's `prefers-reduced-motion` setting (live).
 *
 * The Datascape leans on continuous WebGL + spring animation; when the user
 * asks for reduced motion we drop the heavy effects (3D canvases, radar sweep,
 * equalizer jitter) in favour of static renders — both an accessibility and a
 * performance win.
 */
export function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return false;
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  });

  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const mq = window.matchMedia('(prefers-reduced-motion: reduce)');
    const onChange = () => setReduced(mq.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  return reduced;
}
