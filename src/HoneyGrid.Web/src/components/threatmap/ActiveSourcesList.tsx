import { motion } from 'framer-motion';
import { Crosshair } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { CountryFlag } from '@/components/ui/CountryFlag';
import { formatInt } from '@/lib/format';
import { useThreatMapStore } from '@/stores/threatMapStore';
import type { ThreatSource } from './types';

/**
 * Right HUD panel — top attacking countries.
 *
 * Hovering a row writes `hoveredCountry` to the shared store; the 3D camera rig
 * orbits to that country and the arcs dim the others. This is the 2D→3D bridge.
 */
export function ActiveSourcesList({ sources }: { sources: ThreatSource[] }) {
  const { t } = useTranslation();
  const hovered = useThreatMapStore((s) => s.hoveredCountry);
  const setHovered = useThreatMapStore((s) => s.setHoveredCountry);

  const top = sources.slice(0, 8);
  const max = Math.max(1, top[0]?.count ?? 1);

  return (
    <motion.div
      initial={{ opacity: 0, x: 24, filter: 'blur(6px)' }}
      animate={{ opacity: 1, x: 0, filter: 'blur(0px)' }}
      transition={{ type: 'spring', stiffness: 260, damping: 26 }}
      className="w-60 rounded-2xl border border-white/10 bg-zinc-900/20 p-3 shadow-2xl backdrop-blur-2xl"
      onMouseLeave={() => setHovered(null)}
    >
      <div className="mb-2 flex items-center gap-2 px-1">
        <Crosshair className="h-3.5 w-3.5 text-amber-500" />
        <span className="text-xs font-semibold uppercase tracking-wider text-zinc-300">
          {t('threatMap.activeSources')}
        </span>
      </div>

      <div className="space-y-0.5">
        {top.map((c, i) => {
          const active = hovered === c.country;
          const dimmed = hovered != null && !active;
          return (
            <button
              key={c.country}
              type="button"
              onMouseEnter={() => setHovered(c.country)}
              onFocus={() => setHovered(c.country)}
              className={cn(
                'group relative flex w-full items-center gap-2.5 rounded-lg px-2 py-1.5 text-left transition-all duration-200',
                active ? 'bg-amber-500/10 ring-1 ring-amber-500/30' : 'hover:bg-white/5',
                dimmed && 'opacity-40',
              )}
            >
              {active && (
                <motion.span
                  layoutId="source-focus"
                  className="absolute left-0 top-1/2 h-5 w-0.5 -translate-y-1/2 rounded-r-full bg-amber-500"
                  transition={{ type: 'spring', stiffness: 350, damping: 30 }}
                />
              )}
              <span className="w-4 text-right font-mono text-[10px] text-zinc-600">{i + 1}</span>
              <CountryFlag code={c.country} className="text-sm" />
              <span
                className={cn(
                  'w-7 font-mono text-[11px] font-semibold uppercase',
                  active ? 'text-amber-300' : 'text-zinc-300',
                )}
              >
                {c.country}
              </span>
              <div className="h-1 flex-1 overflow-hidden rounded-full bg-white/5">
                <div
                  className={cn(
                    'h-full rounded-full transition-all duration-500',
                    active ? 'bg-amber-400' : 'bg-gradient-to-r from-amber-500/70 to-orange-500/40',
                  )}
                  style={{
                    width: `${(c.count / max) * 100}%`,
                    boxShadow: active ? '0 0 8px rgba(245,158,11,0.7)' : 'none',
                  }}
                />
              </div>
              <span className="w-10 text-right font-mono text-[10px] tabular-nums text-zinc-400">
                {formatInt(c.count)}
              </span>
            </button>
          );
        })}
        {top.length === 0 && (
          <div className="px-2 py-6 text-center font-mono text-xs text-zinc-600">
            {t('common.waiting')}
          </div>
        )}
      </div>
    </motion.div>
  );
}
