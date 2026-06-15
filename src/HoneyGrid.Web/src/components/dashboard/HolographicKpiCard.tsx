import { motion } from 'framer-motion';
import { Area, AreaChart, ResponsiveContainer } from 'recharts';
import { cn } from '@/lib/utils';
import { ParticleSphere } from '@/components/three/ParticleSphere';
import { ACCENTS, type Accent } from './accents';

interface HolographicKpiCardProps {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
  hint?: string;
  accent: Accent;
  /** 24-point sparkline series. */
  sparkData?: { h: number; v: number }[];
  /** Render the live R3F particle sphere behind the value (hero card only). */
  withSphere?: boolean;
  delay?: number;
}

/** Decorative L-bracket corner — gives panels a HUD/targeting feel. */
function Corner({ className }: { className: string }) {
  return <span className={cn('pointer-events-none absolute h-3 w-3 border-current', className)} />;
}

export function HolographicKpiCard({
  icon: Icon,
  label,
  value,
  hint,
  accent,
  sparkData,
  withSphere = false,
  delay = 0,
}: HolographicKpiCardProps) {
  const a = ACCENTS[accent];
  const gradId = `spark-${accent}`;

  return (
    <motion.div
      initial={{ opacity: 0, y: 24 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 260, damping: 26 }}
      whileHover={{ y: -4, transition: { type: 'spring', stiffness: 400, damping: 22 } }}
      className="group relative h-full overflow-hidden rounded-2xl glass-strong p-4 cursor-default scanline"
    >
      {/* Accent corner brackets */}
      <span className={a.bracket}>
        <Corner className="left-2 top-2 border-l-2 border-t-2" />
        <Corner className="right-2 top-2 border-r-2 border-t-2" />
        <Corner className="bottom-2 left-2 border-b-2 border-l-2" />
        <Corner className="bottom-2 right-2 border-b-2 border-r-2" />
      </span>

      {/* Hover wash in the accent colour */}
      <div
        className={cn(
          'pointer-events-none absolute inset-0 bg-gradient-to-br to-transparent opacity-0 transition-opacity duration-300 group-hover:opacity-100',
          a.glowFrom,
        )}
      />

      {/* 3D particle core (hero) */}
      {withSphere && (
        <div className="pointer-events-none absolute -right-6 -top-6 h-36 w-36 opacity-70">
          <ParticleSphere color={a.hex} />
        </div>
      )}

      <div className="relative z-10 flex h-full flex-col justify-between gap-3">
        <div className="flex items-center justify-between">
          <div
            className={cn(
              'flex h-9 w-9 items-center justify-center rounded-lg bg-white/5 ring-1 ring-inset',
              a.ring,
            )}
          >
            <Icon className={cn('h-4.5 w-4.5', a.text)} />
          </div>
          <span className={cn('font-mono text-[10px] uppercase tracking-widest', a.text)}>● live</span>
        </div>

        <div>
          <div
            className="font-mono text-3xl font-bold leading-none tracking-tight tabular-nums text-white"
            style={{ textShadow: `0 0 24px ${a.hex}55` }}
          >
            {value}
          </div>
          <div className="mt-1.5 text-xs font-medium text-zinc-300">{label}</div>
          {hint && <div className="text-[11px] text-zinc-500">{hint}</div>}
        </div>

        {/* Glowing sparkline */}
        {sparkData && (
          <div className="-mx-4 -mb-4 h-12 opacity-80 transition-opacity group-hover:opacity-100">
            <ResponsiveContainer width="100%" height="100%" minWidth={0} minHeight={0}>
              <AreaChart data={sparkData} margin={{ top: 4, right: 0, bottom: 0, left: 0 }}>
                <defs>
                  <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={a.hex} stopOpacity={0.45} />
                    <stop offset="100%" stopColor={a.hex} stopOpacity={0} />
                  </linearGradient>
                </defs>
                <Area
                  type="monotone"
                  dataKey="v"
                  stroke={a.hex}
                  strokeWidth={1.75}
                  fill={`url(#${gradId})`}
                  dot={false}
                  isAnimationActive={false}
                  style={{ filter: `drop-shadow(0 0 4px ${a.hex}aa)` }}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
    </motion.div>
  );
}
