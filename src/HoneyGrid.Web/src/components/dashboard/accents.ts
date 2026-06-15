/**
 * Functional accent palette for the Datascape KPI cards. Colour is tied to
 * status, never decorative: amber = system/brand, rose = threat/attackers,
 * emerald = healthy/active, blue = network. Kept in its own module so the
 * card component stays a pure component export (react-refresh friendly).
 */
export type Accent = 'amber' | 'rose' | 'emerald' | 'blue';

export interface AccentTokens {
  hex: string;
  text: string;
  ring: string;
  glowFrom: string;
  bracket: string;
}

export const ACCENTS: Record<Accent, AccentTokens> = {
  amber: {
    hex: '#f59e0b',
    text: 'text-amber-400',
    ring: 'ring-amber-500/20',
    glowFrom: 'from-amber-500/20',
    bracket: 'border-amber-500/40',
  },
  rose: {
    hex: '#f43f5e',
    text: 'text-rose-400',
    ring: 'ring-rose-500/20',
    glowFrom: 'from-rose-500/20',
    bracket: 'border-rose-500/40',
  },
  emerald: {
    hex: '#10b981',
    text: 'text-emerald-400',
    ring: 'ring-emerald-500/20',
    glowFrom: 'from-emerald-500/20',
    bracket: 'border-emerald-500/40',
  },
  blue: {
    hex: '#3b82f6',
    text: 'text-blue-400',
    ring: 'ring-blue-500/20',
    glowFrom: 'from-blue-500/20',
    bracket: 'border-blue-500/40',
  },
};
