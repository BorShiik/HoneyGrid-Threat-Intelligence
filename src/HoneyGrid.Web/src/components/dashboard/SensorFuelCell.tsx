import { useState } from 'react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { SENSOR_LABELS, formatInt } from '@/lib/format';
import type { SensorType } from '@/types/api';

/**
 * "3D Fuel Cell" sensor breakdown — replaces flat progress bars.
 *
 * Each sensor is a segmented energy cell. Hovering a row lifts it toward the
 * viewer on the Z-axis (perspective parent) and ignites its accent colour, so
 * the panel reads like a stack of physical capacity gauges rather than a chart.
 */

const SENSOR_TOKENS: Record<SensorType, { hex: string; text: string; bg: string }> = {
  ssh: { hex: '#ec4899', text: 'text-pink-400', bg: 'bg-pink-500' }, // Pink — SSH
  web: { hex: '#f59e0b', text: 'text-amber-400', bg: 'bg-amber-500' }, // Amber — WEB
  rdp: { hex: '#3b82f6', text: 'text-blue-400', bg: 'bg-blue-500' }, // Blue — RDP
};

const SEGMENTS = 24;

function FuelGauge({ pct, hex, active }: { pct: number; hex: string; active: boolean }) {
  const lit = Math.round((pct / 100) * SEGMENTS);
  return (
    <div className="flex h-10 items-end gap-[3px]">
      {Array.from({ length: SEGMENTS }, (_, i) => {
        const on = i < lit;
        // Segments rise in height toward the leading edge for a "charge" feel.
        const h = 40 + (i / SEGMENTS) * 60;
        return (
          <motion.span
            key={i}
            initial={{ scaleY: 0 }}
            animate={{ scaleY: on ? 1 : 0.18 }}
            transition={{ delay: i * 0.012, type: 'spring', stiffness: 320, damping: 24 }}
            className="w-full origin-bottom rounded-[1px]"
            style={{
              height: `${h}%`,
              background: on ? hex : 'rgba(113,113,122,0.18)',
              boxShadow: on && active ? `0 0 8px ${hex}` : 'none',
              opacity: on ? (active ? 1 : 0.8) : 1,
            }}
          />
        );
      })}
    </div>
  );
}

export function SensorFuelCell({ data, delay = 0 }: { data: Record<SensorType, number>; delay?: number }) {
  const { t } = useTranslation();
  const [hovered, setHovered] = useState<SensorType | null>(null);
  const total = Math.max(1, Object.values(data).reduce((s, v) => s + v, 0));
  const sensors = (Object.keys(data) as SensorType[]).sort((a, b) => data[b] - data[a]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 280, damping: 28 }}
      className="rounded-2xl glass-strong p-5"
    >
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-zinc-200">{t('dashboard.eventsBySensor')}</h3>
        <span className="font-mono text-xs text-zinc-500">
          {formatInt(total)} {t('common.total')}
        </span>
      </div>

      <div className="space-y-3" style={{ perspective: 1200 }}>
        {sensors.map((s) => {
          const pct = (data[s] / total) * 100;
          const tok = SENSOR_TOKENS[s];
          const active = hovered === s;
          return (
            <motion.div
              key={s}
              onHoverStart={() => setHovered(s)}
              onHoverEnd={() => setHovered(null)}
              animate={{ z: active ? 60 : 0, scale: active ? 1.015 : 1 }}
              transition={{ type: 'spring', stiffness: 400, damping: 26 }}
              style={{ transformStyle: 'preserve-3d' }}
              className={cn(
                'relative flex items-center gap-4 rounded-xl border px-4 py-3 transition-colors',
                active
                  ? 'border-white/15 bg-white/[0.04]'
                  : 'border-white/5 bg-white/[0.015]',
              )}
            >
              {/* Accent edge light */}
              <span
                className="absolute left-0 top-1/2 h-8 w-[3px] -translate-y-1/2 rounded-r"
                style={{ background: tok.hex, boxShadow: active ? `0 0 12px ${tok.hex}` : 'none' }}
              />

              <div className="w-14 shrink-0">
                <div className={cn('font-mono text-sm font-bold', tok.text)}>{SENSOR_LABELS[s]}</div>
                <div className="font-mono text-[10px] text-zinc-500 tabular-nums">{formatInt(data[s])}</div>
              </div>

              <div className="flex-1">
                <FuelGauge pct={pct} hex={tok.hex} active={active} />
              </div>

              <div
                className="w-16 text-right font-mono text-lg font-bold tabular-nums"
                style={{ color: tok.hex, textShadow: active ? `0 0 12px ${tok.hex}88` : 'none' }}
              >
                {pct.toFixed(1)}
                <span className="text-xs text-zinc-500">%</span>
              </div>
            </motion.div>
          );
        })}
      </div>
    </motion.div>
  );
}
