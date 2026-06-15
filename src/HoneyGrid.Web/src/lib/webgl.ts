/**
 * Cheap, cached WebGL capability probe.
 *
 * The 3D "Datascape" layers (R3F canvases) only mount when a real WebGL context
 * is available. This keeps the app rendering a graceful CSS fallback under
 * jsdom (vitest), SSR, or browsers with WebGL disabled — instead of throwing
 * when `getContext('webgl')` returns null.
 */
let cached: boolean | null = null;

export function supportsWebGL(): boolean {
  if (cached !== null) return cached;
  if (typeof document === 'undefined') {
    cached = false;
    return cached;
  }
  try {
    const canvas = document.createElement('canvas');
    cached = !!(
      window.WebGLRenderingContext &&
      (canvas.getContext('webgl') || canvas.getContext('experimental-webgl'))
    );
  } catch {
    cached = false;
  }
  return cached;
}
