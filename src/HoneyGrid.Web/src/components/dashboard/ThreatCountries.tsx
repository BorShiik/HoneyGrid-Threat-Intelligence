import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Globe2 } from 'lucide-react';
import { formatInt } from '@/lib/format';
import { useReducedMotion } from '@/lib/useReducedMotion';

/**
 * Top source countries — glowing, animated threat lines next to a holographic
 * radar disc. The radar is an SVG placeholder sized + positioned so a real R3F
 * mini-globe can later be dropped into `.radar-slot` without touching layout.
 */

interface CountryRow {
  country: string;
  countryName: string;
  count: number;
}

function HoloRadar({ count }: { count: number }) {
  const reducedMotion = useReducedMotion();
  return (
    <div className="radar-slot relative aspect-square w-full max-w-[220px]">
      {/* 🔮 Advanced UI Radar */}
      <svg viewBox="0 0 240 240" className="h-full w-full drop-shadow-[0_0_12px_rgba(245,158,11,0.3)]">
        <defs>
          <radialGradient id="radar-fade" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.05" />
            <stop offset="80%" stopColor="#f59e0b" stopOpacity="0.1" />
            <stop offset="100%" stopColor="#f59e0b" stopOpacity="0.3" />
          </radialGradient>
          <linearGradient id="sweep" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.6" />
            <stop offset="50%" stopColor="#f59e0b" stopOpacity="0.15" />
            <stop offset="100%" stopColor="#f59e0b" stopOpacity="0" />
          </linearGradient>
          <filter id="glow">
            <feGaussianBlur stdDeviation="2" result="coloredBlur" />
            <feMerge>
              <feMergeNode in="coloredBlur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        {/* Base Glow & Inner Rings */}
        <circle cx="120" cy="120" r="110" fill="url(#radar-fade)" stroke="rgba(245,158,11,0.2)" strokeWidth="1" />
        {[40, 70, 100].map((r) => (
          <circle key={r} cx="120" cy="120" r={r} fill="none" stroke="rgba(245,158,11,0.2)" strokeWidth="1" />
        ))}

        {/* Outer dashed ring rotating backwards */}
        <motion.circle
          cx="120"
          cy="120"
          r="116"
          fill="none"
          stroke="rgba(245,158,11,0.4)"
          strokeWidth="1.5"
          strokeDasharray="4 8"
          animate={reducedMotion ? undefined : { rotate: -360 }}
          transition={{ duration: 20, repeat: Infinity, ease: 'linear' }}
          style={{ transformOrigin: '120px 120px', transformBox: 'view-box' }}
        />

        {/* Static Crosshairs */}
        <g stroke="rgba(245,158,11,0.25)" strokeWidth="1">
          <line x1="120" y1="0" x2="120" y2="240" />
          <line x1="0" y1="120" x2="240" y2="120" />
          <line x1="35" y1="35" x2="205" y2="205" strokeOpacity="0.4" strokeDasharray="2 4" />
          <line x1="205" y1="35" x2="35" y2="205" strokeOpacity="0.4" strokeDasharray="2 4" />
        </g>

        {/* Target Reticle Corners */}
        <path
          d="M25 45 L25 25 L45 25 M195 25 L215 25 L215 45 M215 195 L215 215 L195 215 M45 215 L25 215 L25 195"
          fill="none"
          stroke="#f59e0b"
          strokeWidth="2"
          opacity="0.5"
        />

        {/* Rotating sweep */}
        <motion.g
          animate={reducedMotion ? undefined : { rotate: 360 }}
          transition={{ duration: 3, repeat: Infinity, ease: 'linear' }}
          style={{ transformOrigin: '120px 120px', transformBox: 'view-box' }}
        >
          <path d="M120 120 L120 10 A110 110 0 0 1 215 55 Z" fill="url(#sweep)" />
          {/* Leading Edge */}
          <line x1="120" y1="120" x2="120" y2="10" stroke="#fbd38d" strokeWidth="2" filter="url(#glow)" />
        </motion.g>

        {/* Contact blips with ripple animation */}
        {[
          { cx: 160, cy: 80, c: '#f43f5e', s: 3, d: 0 },
          { cx: 70, cy: 150, c: '#3b82f6', s: 2.5, d: 1 },
          { cx: 140, cy: 160, c: '#f59e0b', s: 4, d: 2.5 },
          { cx: 80, cy: 80, c: '#10b981', s: 2, d: 1.5 },
        ].map((b, i) => (
          <motion.g
            key={i}
            animate={reducedMotion ? { opacity: 0.8 } : { opacity: [0, 1, 0.8, 0] }}
            transition={{ duration: 3, repeat: Infinity, delay: b.d, times: [0, 0.1, 0.5, 1] }}
          >
            <circle cx={b.cx} cy={b.cy} r={b.s} fill={b.c} filter="url(#glow)" />
            <motion.circle
              cx={b.cx}
              cy={b.cy}
              r={b.s}
              fill="none"
              stroke={b.c}
              strokeWidth="1"
              animate={{ scale: [1, 3.5], opacity: [1, 0] }}
              transition={{ duration: 1.5, repeat: Infinity, delay: b.d }}
              style={{ transformOrigin: `${b.cx}px ${b.cy}px` }}
            />
          </motion.g>
        ))}
      </svg>

      {/* Center UI Overlay */}
      <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
        <div className="flex h-[72px] w-[72px] flex-col items-center justify-center rounded-full border border-amber-500/20 bg-black/50 shadow-[0_0_20px_rgba(245,158,11,0.15)] backdrop-blur-md">
          <span className="font-mono text-2xl font-bold tabular-nums text-amber-400 drop-shadow-[0_0_8px_rgba(245,158,11,0.8)]">
            {count}
          </span>
          <span className="text-[9px] uppercase tracking-widest text-amber-500/80">srcs</span>
        </div>
      </div>
    </div>
  );
}

export function ThreatCountries({ countries, delay = 0 }: { countries: CountryRow[]; delay?: number }) {
  const { t } = useTranslation();
  const top = countries.slice(0, 5);
  const max = Math.max(1, top[0]?.count ?? 1);

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 280, damping: 28 }}
      className="rounded-2xl glass-strong p-5"
    >
      <h3 className="mb-4 flex items-center gap-2 text-sm font-semibold text-zinc-200">
        <Globe2 className="h-4 w-4 text-amber-500" />
        {t('dashboard.topCountries')}
      </h3>

      <div className="flex items-center gap-5">
        <div className="min-w-0 flex-1 space-y-3.5">
          {top.map((c, i) => (
            <div key={c.country} className="space-y-1.5">
              <div className="flex items-center justify-between text-xs">
                <span className="flex items-center gap-2">
                  <span className="font-mono text-zinc-600">{String(i + 1).padStart(2, '0')}</span>
                  <span className="font-mono font-semibold uppercase text-zinc-200">{c.country}</span>
                  <span className="truncate text-zinc-500">{c.countryName}</span>
                </span>
                <span className="font-mono tabular-nums text-amber-400">{formatInt(c.count)}</span>
              </div>
              {/* Glowing animated threat line */}
              <div className="relative h-[3px] overflow-hidden rounded-full bg-white/5">
                <motion.div
                  initial={{ width: 0 }}
                  animate={{ width: `${(c.count / max) * 100}%` }}
                  transition={{ delay: delay + 0.2 + i * 0.06, duration: 0.7, ease: 'easeOut' }}
                  className="line-flow h-full rounded-full"
                  style={{
                    background:
                      'linear-gradient(90deg, rgba(245,158,11,0.3), #f59e0b, #fb923c, rgba(245,158,11,0.3))',
                    boxShadow: '0 0 8px rgba(245,158,11,0.6)',
                  }}
                />
              </div>
            </div>
          ))}
        </div>

        <div className="hidden shrink-0 sm:block">
          <HoloRadar count={countries.length} />
        </div>
      </div>
    </motion.div>
  );
}
