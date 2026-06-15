import { useEffect } from 'react';
import { useThree } from '@react-three/fiber';

/**
 * Caps a `frameloop="demand"` canvas to a fixed FPS by driving `invalidate()`
 * from a throttled rAF loop.
 *
 * Why: the Datascape canvas sits behind ~10 `backdrop-blur` glass panels, and
 * the browser must re-blur all of them on every rendered frame. Rendering at
 * 30 FPS instead of the display's 60–144 roughly halves (or better) that blur
 * cost for a backdrop where the motion is intentionally subtle.
 *
 * Bonus: `requestAnimationFrame` is automatically paused by the browser when
 * the tab is hidden, so the canvas idles at 0 FPS in the background for free.
 */
export function FrameLimiter({ fps = 30 }: { fps?: number }) {
  const invalidate = useThree((s) => s.invalidate);

  useEffect(() => {
    const interval = 1000 / fps;
    let raf = 0;
    let last = performance.now();

    const tick = (now: number) => {
      raf = requestAnimationFrame(tick);
      if (now - last >= interval) {
        last = now - ((now - last) % interval);
        invalidate();
      }
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [fps, invalidate]);

  return null;
}
