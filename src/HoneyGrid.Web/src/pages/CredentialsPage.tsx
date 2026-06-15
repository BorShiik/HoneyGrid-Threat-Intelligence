import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { KeyRound, User, Lock, Hash, ShieldAlert } from 'lucide-react';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { squarify } from '@/lib/treemap';
import { useStatsCredentials } from '@/api/queries';
import type { CredentialStat } from '@/types/api';

const TM_W = 1000;
const TM_H = 420;

function Tile({
  icon: Icon,
  label,
  value,
  accent,
  delay,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
  accent: string;
  delay: number;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className="relative overflow-hidden rounded-xl glass-strong p-4"
    >
      <div className={cn('absolute top-0 left-0 right-0 h-[2px]', accent)} />
      <div className="flex items-center gap-2 text-[11px] text-zinc-500">
        <Icon className="h-3.5 w-3.5" /> {label}
      </div>
      <div className="mt-1.5 truncate font-mono text-xl font-bold tabular-nums text-white">{value}</div>
    </motion.div>
  );
}

function RankBars({
  title,
  icon: Icon,
  rows,
  max,
  delay,
}: {
  title: string;
  icon: React.ComponentType<{ className?: string }>;
  rows: { label: string; count: number }[];
  max: number;
  delay: number;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-xl glass-strong p-4 space-y-3"
    >
      <h3 className="flex items-center gap-2 text-sm font-semibold text-zinc-200">
        <Icon className="h-4 w-4 text-amber-500" /> {title}
      </h3>
      <div className="space-y-2.5">
        {rows.map((r, i) => (
          <div key={r.label} className="space-y-1">
            <div className="flex items-center justify-between text-sm">
              <code className="font-mono text-zinc-300">{r.label}</code>
              <span className="font-mono text-xs tabular-nums text-zinc-500">{formatInt(r.count)}</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-zinc-800/60">
              <motion.div
                initial={{ width: 0 }}
                animate={{ width: `${(r.count / max) * 100}%` }}
                transition={{ delay: delay + 0.1 + i * 0.04, duration: 0.7, ease: 'easeOut' }}
                className="h-full rounded-full bg-gradient-to-r from-amber-500 to-orange-500/50"
              />
            </div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

export function CredentialsPage() {
  const { t } = useTranslation();
  const { data, isPending, isError } = useStatsCredentials();

  const pairRects = useMemo(() => {
    if (!data) return [];
    return squarify(
      data.topPairs.map((p) => ({ ...p, value: p.count })),
      TM_W,
      TM_H,
    );
  }, [data]);

  const maxPairCount = Math.max(1, ...(data?.topPairs ?? []).map((p) => p.count));
  const maxUser = Math.max(1, ...(data?.topUsernames ?? []).map((u) => u.count));
  const maxPass = Math.max(1, ...(data?.topPasswords ?? []).map((p) => p.count));

  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-5">
      <div>
        <h2 className="flex items-center gap-2 text-xl font-bold tracking-tight text-white">
          <KeyRound className="h-5 w-5 text-amber-500" />
          {t('credentials.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500">{t('credentials.subtitle')}</p>
      </div>

      {isError && (
        <div className="rounded-xl border border-rose-500/20 bg-rose-500/5 p-4 text-sm text-rose-400">
          {t('credentials.loadError')}
        </div>
      )}

      {isPending ? (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="h-20 rounded-xl shimmer" />
            ))}
          </div>
          <div className="h-80 rounded-xl shimmer" />
        </div>
      ) : data ? (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Tile icon={Hash} label={t('credentials.totalAttempts')} value={formatInt(data.totalAttempts)} accent="bg-amber-500" delay={0} />
            <Tile icon={User} label={t('credentials.topLogins')} value={formatInt(data.topUsernames.length)} accent="bg-rose-500" delay={0.05} />
            <Tile icon={Lock} label={t('credentials.topPasswords')} value={formatInt(data.topPasswords.length)} accent="bg-orange-500" delay={0.1} />
            <Tile
              icon={ShieldAlert}
              label={t('credentials.commonPair')}
              value={data.topPairs[0] ? `${data.topPairs[0].username}:${data.topPairs[0].password}` : '—'}
              accent="bg-blue-500"
              delay={0.15}
            />
          </div>

          {/* Treemap */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-xl glass-strong p-4"
          >
            <h3 className="mb-3 text-sm font-semibold text-zinc-200">{t('credentials.treemapTitle')}</h3>
            <div className="relative w-full" style={{ aspectRatio: `${TM_W} / ${TM_H}` }}>
              {pairRects.map((rect, i) => {
                const pair = rect.item as CredentialStat & { value: number };
                const intensity = 0.18 + Math.max(0, 0.6 - i * 0.08);
                const showLabel = rect.width > 110 && rect.height > 44;
                return (
                  <motion.div
                    key={`${pair.username}:${pair.password}`}
                    initial={{ opacity: 0, scale: 0.9 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ delay: 0.25 + i * 0.03, type: 'spring', stiffness: 300, damping: 28 }}
                    className="absolute rounded-md border border-amber-400/10 p-2 overflow-hidden"
                    style={{
                      left: `${(rect.x / TM_W) * 100}%`,
                      top: `${(rect.y / TM_H) * 100}%`,
                      width: `${(rect.width / TM_W) * 100}%`,
                      height: `${(rect.height / TM_H) * 100}%`,
                      backgroundColor: `rgba(245,158,11,${intensity})`,
                    }}
                  >
                    {showLabel && (
                      <>
                        <div className="font-mono text-[13px] font-semibold text-amber-50 truncate">
                          {pair.username}:{pair.password}
                        </div>
                        <div className="font-mono text-[11px] text-amber-100/70">
                          {formatInt(pair.count)} {t('credentials.attempts')}
                        </div>
                      </>
                    )}
                  </motion.div>
                );
              })}
            </div>
          </motion.div>

          {/* Rank bars */}
          <div className="grid gap-4 lg:grid-cols-2">
            <RankBars
              title={t('credentials.frequentLogins')}
              icon={User}
              max={maxUser}
              delay={0.3}
              rows={data.topUsernames.map((u) => ({ label: u.username, count: u.count }))}
            />
            <RankBars
              title={t('credentials.frequentPasswords')}
              icon={Lock}
              max={maxPass}
              delay={0.35}
              rows={data.topPasswords.map((p) => ({ label: p.password, count: p.count }))}
            />
          </div>

          {/* Pairs table */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.4, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-xl glass-strong overflow-hidden"
          >
            <div className="border-b border-white/5 px-4 py-2.5 text-sm font-semibold text-zinc-200">
              {t('credentials.pairsTitle')}
            </div>
            <div className="overflow-x-auto">
              <div className="min-w-[400px]">
                <div className="grid grid-cols-[1fr_1fr_auto_5rem] gap-2 border-b border-white/5 px-4 py-2 text-[11px] uppercase tracking-wider text-zinc-600">
                  <span>{t('credentials.colLogin')}</span>
                  <span>{t('credentials.colPassword')}</span>
                  <span className="text-right">{t('credentials.colAttempts')}</span>
                  <span />
                </div>
                {data.topPairs.map((p) => (
                  <div
                    key={`${p.username}:${p.password}`}
                    className="grid grid-cols-[1fr_1fr_auto_5rem] items-center gap-2 border-b border-white/[0.03] px-4 py-2 text-sm last:border-b-0 hover:bg-white/[0.03] transition-colors"
                  >
                    <code className="font-mono text-zinc-300 truncate">{p.username}</code>
                    <code className="font-mono text-zinc-400 truncate">{p.password}</code>
                    <span className="text-right font-mono text-xs tabular-nums text-zinc-300">{formatInt(p.count)}</span>
                    <div className="h-1.5 rounded-full bg-zinc-800/60 overflow-hidden">
                      <div className="h-full rounded-full bg-amber-500/60" style={{ width: `${(p.count / maxPairCount) * 100}%` }} />
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </motion.div>
        </>
      ) : null}
    </motion.section>
  );
}
