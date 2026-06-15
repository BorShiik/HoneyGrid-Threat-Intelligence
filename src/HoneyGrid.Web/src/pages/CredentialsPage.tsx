import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { KeyRound, User, Lock, Hash, ShieldAlert, Eye, EyeOff, Copy, Check, TerminalSquare } from 'lucide-react';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { squarify } from '@/lib/treemap';
import { useStatsCredentials } from '@/api/queries';
import type { CredentialStat } from '@/types/api';

const TM_W = 1000;
const TM_H = 420;

/* ── Holographic Tile ── */
function Tile({
  icon: Icon,
  label,
  value,
  accent,
  glow,
  delay,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
  accent: string;
  glow: string;
  delay: number;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className={cn(
        "relative overflow-hidden rounded-2xl glass-strong p-5 group border transition-all duration-500",
        "hover:border-white/20 hover:bg-white/[0.04] border-white/5",
        glow
      )}
    >
      <div className={cn('absolute inset-x-0 top-0 h-[2px] transition-all duration-500', accent, 'group-hover:h-[3px]')} />
      <div className="flex items-center gap-2.5 text-[10px] font-bold uppercase tracking-widest text-zinc-400 group-hover:text-zinc-300 transition-colors">
        <Icon className={cn("h-4 w-4 drop-shadow-lg", accent.replace('bg-', 'text-'))} /> {label}
      </div>
      <div className="mt-3 truncate font-mono text-3xl font-bold tabular-nums text-white drop-shadow-sm">{value}</div>
      <div className={cn("absolute -bottom-6 -right-6 h-24 w-24 rounded-full blur-2xl opacity-10 group-hover:opacity-30 transition-opacity duration-500", accent)} />
    </motion.div>
  );
}

/* ── Glowing Rank Bars ── */
function RankBars({
  title,
  icon: Icon,
  rows,
  max,
  delay,
  accentClass,
}: {
  title: string;
  icon: React.ComponentType<{ className?: string }>;
  rows: { label: string; count: number }[];
  max: number;
  delay: number;
  accentClass: string;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay, type: 'spring', stiffness: 300, damping: 30 }}
      className="rounded-2xl glass-strong p-6 space-y-5 border border-white/5 relative overflow-hidden"
    >
      <div className="absolute top-0 right-0 p-32 bg-gradient-to-bl from-white/5 to-transparent blur-3xl opacity-50 rounded-full pointer-events-none" />
      
      <h3 className="flex items-center gap-2 text-sm font-bold text-zinc-100 tracking-tight relative z-10">
        <div className={cn("p-1.5 rounded-lg bg-black/40 border border-white/5 shadow-inner", accentClass.replace('bg-', 'text-'))}>
          <Icon className="h-4 w-4" />
        </div>
        {title}
      </h3>
      
      <div className="space-y-3 relative z-10">
        {rows.map((r, i) => {
          const width = Math.max(2, (r.count / max) * 100);
          return (
            <div key={r.label} className="group flex items-center gap-3">
              <div className="w-28 shrink-0 flex justify-end">
                <code className="font-mono text-xs text-zinc-300 bg-black/50 border border-zinc-800 px-2 py-0.5 rounded truncate max-w-full group-hover:border-zinc-600 transition-colors">
                  {r.label}
                </code>
              </div>
              <div className="flex-1 flex items-center gap-3">
                <div className="flex-1 h-1.5 rounded-full bg-zinc-900/80 shadow-inner relative overflow-hidden flex items-center">
                  <motion.div
                    initial={{ width: 0 }}
                    animate={{ width: `${width}%` }}
                    transition={{ delay: delay + 0.1 + i * 0.05, duration: 0.8, ease: 'easeOut' }}
                    className={cn("h-full rounded-full relative", accentClass)}
                  >
                    <div className="absolute right-0 top-1/2 -translate-y-1/2 w-4 h-4 bg-white rounded-full blur-[4px] opacity-60" />
                  </motion.div>
                </div>
                <span className="font-mono text-[10px] tabular-nums text-zinc-500 w-12 text-right group-hover:text-zinc-300 transition-colors">{formatInt(r.count)}</span>
              </div>
            </div>
          );
        })}
      </div>
    </motion.div>
  );
}

/* ── Interactive Secrets Table Row ── */
function SecretRow({ pair, maxCount }: { pair: CredentialStat & { value?: number }; maxCount: number }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(`${pair.username}:${pair.password}`);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="grid grid-cols-[1fr_1fr_auto_6rem] items-center gap-4 border-b border-white/[0.02] px-5 py-2.5 text-sm last:border-b-0 hover:bg-white/[0.04] transition-colors group">
      {/* Login */}
      <div className="flex items-center gap-2 min-w-0">
        <TerminalSquare className="h-3 w-3 text-zinc-600 shrink-0" />
        <code className="font-mono text-[13px] text-zinc-300 truncate">{pair.username}</code>
      </div>
      
      {/* Password */}
      <div className="flex items-center min-w-0 pr-4">
        <code className="font-mono text-[13px] text-amber-400 truncate">
          {pair.password}
        </code>
      </div>

      {/* Actions */}
      <div className="flex items-center justify-end">
        <button 
          onClick={handleCopy}
          className="p-1.5 rounded-md text-zinc-500 hover:text-white hover:bg-white/10 transition-all opacity-0 group-hover:opacity-100 outline-none"
          title="Copy pair"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      </div>

      {/* Attempts Bar */}
      <div className="flex flex-col gap-1 justify-center relative pr-2">
        <div className="text-right font-mono text-[11px] font-bold tabular-nums text-zinc-400 group-hover:text-amber-400 transition-colors">{formatInt(pair.count)}</div>
        <div className="h-1 w-full rounded-full bg-zinc-800/60 overflow-hidden ml-auto max-w-[60px]">
          <div className="h-full rounded-full bg-amber-500/80 shadow-[0_0_8px_#f59e0b]" style={{ width: `${(pair.count / maxCount) * 100}%` }} />
        </div>
      </div>
    </div>
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
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-6 pb-10">
      {/* Header */}
      <div>
        <h2 className="flex items-center gap-3 text-2xl font-bold tracking-tight text-white">
          <KeyRound className="h-6 w-6 text-amber-500 drop-shadow-[0_0_8px_rgba(245,158,11,0.5)]" />
          {t('credentials.title', 'Анализ учётных данных')}
        </h2>
        <p className="mt-1 text-sm text-zinc-400">{t('credentials.subtitle', 'Наиболее перебираемые логины, пароли и пары из brute-force атак. Размер плитки — число попыток.')}</p>
      </div>

      {isError && (
        <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-400 shadow-inner">
          {t('credentials.loadError', 'Ошибка загрузки данных')}
        </div>
      )}

      {isPending ? (
        <div className="space-y-6">
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="h-28 rounded-2xl shimmer" />
            ))}
          </div>
          <div className="h-[450px] rounded-2xl shimmer" />
        </div>
      ) : data ? (
        <>
          {/* Holographic KPIs */}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Tile 
              icon={Hash} 
              label={t('credentials.totalAttempts', 'Попыток всего')} 
              value={formatInt(data.totalAttempts)} 
              accent="bg-amber-500" 
              glow="shadow-[0_0_20px_-5px_rgba(245,158,11,0.15)]"
              delay={0} 
            />
            <Tile 
              icon={User} 
              label={t('credentials.topLogins', 'Топ логинов')} 
              value={formatInt(data.topUsernames.length)} 
              accent="bg-rose-500" 
              glow="shadow-[0_0_20px_-5px_rgba(225,29,72,0.15)]"
              delay={0.05} 
            />
            <Tile 
              icon={Lock} 
              label={t('credentials.topPasswords', 'Топ паролей')} 
              value={formatInt(data.topPasswords.length)} 
              accent="bg-orange-500" 
              glow="shadow-[0_0_20px_-5px_rgba(249,115,22,0.15)]"
              delay={0.1} 
            />
            <Tile
              icon={ShieldAlert}
              label={t('credentials.commonPair', 'Частая пара')}
              value={data.topPairs[0] ? `${data.topPairs[0].username}:${data.topPairs[0].password}` : '—'}
              accent="bg-blue-500"
              glow="shadow-[0_0_20px_-5px_rgba(59,130,246,0.15)]"
              delay={0.15}
            />
          </div>

          {/* Cyber-Map Treemap */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-2xl glass-strong p-6 border border-white/5 relative overflow-hidden"
          >
            {/* Background cyber grid */}
            <div className="absolute inset-0 bg-[url('data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMjAiIGhlaWdodD0iMjAiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PGNpcmNsZSBjeD0iMSIgY3k9IjEiIHI9IjEiIGZpbGw9InJnYmEoMjU1LDI1NSwyNTUsMC4wNSkiLz48L3N2Zz4=')] opacity-30 pointer-events-none" />
            
            <div className="flex items-center justify-between mb-4 relative z-10">
               <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                 <TerminalSquare className="h-4 w-4 text-amber-500" />
                 {t('credentials.treemapTitle', 'Карта пар логин / пароль')}
               </h3>
               <span className="text-[10px] uppercase tracking-widest text-zinc-500 font-semibold bg-black/40 px-2 py-1 rounded-md border border-white/5">Visual Matrix</span>
            </div>

            <div className="relative w-full rounded-xl overflow-hidden shadow-2xl bg-black/50 border border-white/10" style={{ aspectRatio: `${TM_W} / ${TM_H}` }}>
              {pairRects.map((rect, i) => {
                const pair = rect.item as CredentialStat & { value: number };
                
                // Opacity-based intensity using a pure amber color to avoid muddy browns
                const intensity = 1 - (i / pairRects.length);
                const bgOpacity = 0.15 + (intensity * 0.35); // 0.15 to 0.50
                const borderOpacity = 0.3 + (intensity * 0.4); // 0.3 to 0.7
                
                const showFullLabel = rect.width > 120 && rect.height > 75;
                const showMiniLabel = rect.width > 80 && rect.height > 35 && !showFullLabel;
                
                // Add padding/gap between tiles
                const PADDING = 4;
                const left = (rect.x / TM_W) * 100 + '%';
                const top = (rect.y / TM_H) * 100 + '%';
                const width = `calc(${(rect.width / TM_W) * 100}% - ${PADDING * 2}px)`;
                const height = `calc(${(rect.height / TM_H) * 100}% - ${PADDING * 2}px)`;
                
                return (
                  <motion.div
                    key={`${pair.username}:${pair.password}`}
                    initial={{ opacity: 0, scale: 0.9 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ delay: 0.25 + i * 0.02, type: 'spring', stiffness: 300, damping: 28 }}
                    className="absolute rounded-xl overflow-hidden group hover:z-10 transition-all duration-300 backdrop-blur-md shadow-inner hover:scale-[1.02] hover:shadow-[0_10px_30px_-5px_rgba(245,158,11,0.6)] hover:!border-amber-400"
                    style={{
                      left: `calc(${left} + ${PADDING}px)`,
                      top: `calc(${top} + ${PADDING}px)`,
                      width,
                      height,
                      backgroundColor: `rgba(245, 158, 11, ${bgOpacity})`,
                      borderColor: `rgba(245, 158, 11, ${borderOpacity})`,
                      borderWidth: '1px',
                    }}
                  >
                    {/* Hover bright background overlay */}
                    <div className="absolute inset-0 bg-amber-400/20 opacity-0 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none" />
                    
                    {(showFullLabel || showMiniLabel) && (
                      <div className="relative z-10 h-full p-3 flex flex-col justify-start overflow-hidden pointer-events-none">
                        <div className="font-mono text-sm md:text-base font-bold text-amber-50 truncate drop-shadow-md">
                          <span className="text-amber-400 group-hover:text-amber-300 transition-colors">{pair.username}</span>
                          <span className="text-zinc-500 mx-1">:</span>
                          <span className="text-zinc-400 group-hover:text-white transition-colors">{pair.password}</span>
                        </div>
                        {showFullLabel && (
                          <div className="mt-1.5 inline-flex items-center gap-1.5 px-2 py-0.5 rounded bg-black/40 border border-white/5 w-fit shrink-0">
                            <span className="font-mono text-[10px] font-bold text-amber-500">{formatInt(pair.count)}</span>
                            <span className="text-[9px] uppercase tracking-widest text-zinc-500 font-semibold">{t('credentials.attempts', 'попыток')}</span>
                          </div>
                        )}
                      </div>
                    )}
                  </motion.div>
                );
              })}
            </div>
          </motion.div>

          {/* Neon Rank Bars */}
          <div className="grid gap-6 lg:grid-cols-2">
            <RankBars
              title={t('credentials.frequentLogins', 'Частые логины')}
              icon={User}
              max={maxUser}
              delay={0.3}
              accentClass="bg-gradient-to-r from-orange-600/80 to-amber-400 shadow-[0_0_10px_#f59e0b]"
              rows={data.topUsernames.map((u) => ({ label: u.username, count: u.count }))}
            />
            <RankBars
              title={t('credentials.frequentPasswords', 'Частые пароли')}
              icon={Lock}
              max={maxPass}
              delay={0.35}
              accentClass="bg-gradient-to-r from-rose-600/80 to-rose-400 shadow-[0_0_10px_#fb7185]"
              rows={data.topPasswords.map((p) => ({ label: p.password, count: p.count }))}
            />
          </div>

          {/* Interactive Secrets Table */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.4, type: 'spring', stiffness: 300, damping: 30 }}
            className="rounded-2xl glass-strong overflow-hidden border border-white/5 shadow-2xl"
          >
            <div className="bg-black/40 border-b border-white/5 px-6 py-4 flex items-center justify-between">
              <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                 <KeyRound className="h-4 w-4 text-amber-500" />
                 {t('credentials.pairsTitle', 'Частые пары логин / пароль')}
              </h3>
              <span className="text-[10px] uppercase tracking-widest font-bold text-rose-500 animate-pulse bg-rose-500/10 px-2 py-0.5 rounded border border-rose-500/20">Brute Force Matrix</span>
            </div>
            
            <div className="overflow-x-auto">
              <div className="min-w-[600px]">
                <div className="grid grid-cols-[1fr_1fr_auto_6rem] gap-4 border-b border-white/[0.05] px-5 py-3 text-[10px] font-bold uppercase tracking-widest text-zinc-500 bg-black/20">
                  <span>{t('credentials.colLogin', 'ЛОГИН')}</span>
                  <span>{t('credentials.colPassword', 'ПАРОЛЬ')}</span>
                  <span className="text-right">ACTIONS</span>
                  <span className="text-right">{t('credentials.colAttempts', 'ПОПЫТКИ')}</span>
                </div>
                <div className="max-h-[400px] overflow-y-auto custom-scrollbar">
                   {data.topPairs.map((p) => (
                     <SecretRow key={`${p.username}:${p.password}`} pair={p} maxCount={maxPairCount} />
                   ))}
                </div>
              </div>
            </div>
          </motion.div>
        </>
      ) : null}
    </motion.section>
  );
}
