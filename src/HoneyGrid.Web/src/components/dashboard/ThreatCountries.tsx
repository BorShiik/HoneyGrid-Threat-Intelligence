import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Globe2 } from 'lucide-react';
import { formatInt } from '@/lib/format';

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

/** Lightweight SVG radar — rotating sweep, range rings, contact blips. */
function HoloRadar({ count }: { count: number }) {
  return (
    <div className="radar-slot relative aspect-square w-full max-w-[180px]">
      {/* ↳ Replace this SVG with a <GlobeScene/>-style R3F canvas when ready. */}
      <svg viewBox="0 0 200 200" className="h-full w-full">
        <defs>
          <radialGradient id="radar-fade" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.18" />
            <stop offset="100%" stopColor="#f59e0b" stopOpacity="0" />
          </radialGradient>
          <linearGradient id="sweep" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.5" />
            <stop offset="100%" stopColor="#f59e0b" stopOpacity="0" />
          </linearGradient>
        </defs>

        <circle cx="100" cy="100" r="96" fill="url(#radar-fade)" />
        {[36, 60, 84].map((r) => (
          <circle key={r} cx="100" cy="100" r={r} fill="none" stroke="rgba(245,158,11,0.18)" strokeWidth="1" />
        ))}
        <line x1="100" y1="4" x2="100" y2="196" stroke="rgba(245,158,11,0.12)" strokeWidth="1" />
        <line x1="4" y1="100" x2="196" y2="100" stroke="rgba(245,158,11,0.12)" strokeWidth="1" />

        {/* Rotating sweep */}
        <g style={{ transformOrigin: '100px 100px' }}>
          <motion.g
            animate={{ rotate: 360 }}
            transition={{ duration: 4, repeat: Infinity, ease: 'linear' }}
            style={{ transformOrigin: '100px 100px' }}
          >
            <path d="M100 100 L100 6 A94 94 0 0 1 180 55 Z" fill="url(#sweep)" />
            <line x1="100" y1="100" x2="100" y2="6" stroke="#f59e0b" strokeWidth="1.5" opacity="0.7" />
          </motion.g>
        </g>

        {/* Contact blips */}
        {[
          { cx: 132, cy: 64, c: '#f43f5e' },
          { cx: 70, cy: 120, c: '#3b82f6' },
          { cx: 120, cy: 134, c: '#f59e0b' },
        ].map((b, i) => (
          <motion.circle
            key={i}
            cx={b.cx}
            cy={b.cy}
            r="2.5"
            fill={b.c}
            animate={{ opacity: [0.3, 1, 0.3] }}
            transition={{ duration: 2, repeat: Infinity, delay: i * 0.6 }}
          />
        ))}
      </svg>

      <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
        <span className="font-mono text-2xl font-bold tabular-nums text-white">{count}</span>
        <span className="text-[10px] uppercase tracking-widest text-zinc-500">sources</span>
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
