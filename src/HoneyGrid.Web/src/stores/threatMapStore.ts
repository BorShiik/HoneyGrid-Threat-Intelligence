import { create } from 'zustand';

/**
 * Bridges the 2D HUD and the 3D canvas on the Threat Map.
 *
 * The Active Sources list (DOM) writes `hoveredCountry`; the R3F scene reads it
 * — the camera rig reads it per-frame via `getState()` (no React re-render),
 * while the arcs subscribe so they can dim siblings. Decoupling through a store
 * keeps the heavy canvas from re-rendering the whole page on every hover.
 */
interface ThreatMapState {
  /** ISO alpha-2 code of the country currently focused from the HUD, or null. */
  hoveredCountry: string | null;
  setHoveredCountry: (country: string | null) => void;
}

export const useThreatMapStore = create<ThreatMapState>((set) => ({
  hoveredCountry: null,
  setHoveredCountry: (hoveredCountry) => set({ hoveredCountry }),
}));
