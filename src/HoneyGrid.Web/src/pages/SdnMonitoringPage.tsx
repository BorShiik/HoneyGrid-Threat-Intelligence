import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { Network, Shield, Activity, ArrowRightLeft, AlertTriangle, WifiOff, RefreshCw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AreaChart, Area, ResponsiveContainer, Tooltip } from 'recharts';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';

import { useSdnTelemetry } from '@/lib/useSdnTelemetry';
import type { SdnNode } from '@/lib/useSdnTelemetry';

/* ── Styled Circular Progress ── */
function CircularGauge({ value, label, color, offline }: { value: number; label: string; color: string; offline?: boolean }) {
  const radius = 32;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (offline ? 0 : value / 100) * circumference;

  return (
    <div className="flex flex-col items-center gap-1 relative">
      <svg width="76" height="76" className="-rotate-90 filter drop-shadow-md">
        {/* Track */}
        <circle cx="38" cy="38" r={radius} strokeWidth="5" className={cn("fill-none", offline ? "stroke-zinc-800/30" : "stroke-zinc-800")} />
        {/* Gradient Def */}
        <defs>
          <linearGradient id={`grad-${label}`} x1="0%" y1="0%" x2="100%" y2="0%">
            <stop offset="0%" stopColor={color} stopOpacity={0.6} />
            <stop offset="100%" stopColor={color} stopOpacity={1} />
          </linearGradient>
        </defs>
        {/* Value Arc */}
        <motion.circle
          cx="38" cy="38" r={radius} strokeWidth="5" fill="none"
          stroke={`url(#grad-${label})`}
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={{ strokeDashoffset: circumference }}
          animate={{ strokeDashoffset: offset }}
          transition={{ duration: 1.5, ease: 'easeOut', delay: 0.2 }}
          className={cn(offline ? "opacity-0" : "opacity-100")}
          style={{ filter: `drop-shadow(0 0 6px ${color}80)` }}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center pt-0.5">
        <div className={cn("text-sm font-bold font-mono tabular-nums", offline ? "text-zinc-600" : "text-white")}>
          {offline ? '--' : value}<span className="text-[10px] text-zinc-500">%</span>
        </div>
      </div>
      <div className="text-[10px] text-zinc-500 uppercase tracking-widest font-semibold mt-1">{label}</div>
    </div>
  );
}

/* ── Toggle Switch ── */
function Toggle({ enabled, onChange, disabled }: { enabled: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <button
      onClick={() => !disabled && onChange(!enabled)}
      disabled={disabled}
      className={cn(
        'relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none',
        enabled ? 'bg-emerald-500/40 border border-emerald-500/50 shadow-[0_0_10px_rgba(16,185,129,0.3)]' : 'bg-zinc-800 border border-zinc-700',
        disabled && 'opacity-50 cursor-not-allowed'
      )}
    >
      <motion.span
        animate={{ x: enabled ? 17 : 2 }}
        transition={{ type: 'spring', stiffness: 500, damping: 30 }}
        className={cn('inline-block h-3 w-3 rounded-full shadow-sm', enabled ? 'bg-emerald-400 shadow-[0_0_5px_#34d399]' : 'bg-zinc-400')}
      />
    </button>
  );
}

/* ── Mini Sparkline for KPIs ── */
function MiniSparkline({ data, color }: { data: number[]; color: string }) {
  const chartData = data.map((d, i) => ({ value: d, index: i }));
  return (
    <div className="h-8 w-24">
      <ResponsiveContainer width="100%" height="100%" minWidth={1} minHeight={1}>
        <AreaChart data={chartData}>
          <defs>
            <linearGradient id={`color-${color}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={color} stopOpacity={0.3}/>
              <stop offset="95%" stopColor={color} stopOpacity={0}/>
            </linearGradient>
          </defs>
          <Area type="monotone" dataKey="value" stroke={color} strokeWidth={2} fill={`url(#color-${color})`} isAnimationActive={false} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

/* ── Node Card ── */
function NodeCard({ node, onToggle }: { node: SdnNode; onToggle: (id: string) => void }) {
  const { t } = useTranslation();
  
  const isOffline = node.status === 'offline';
  const isDegraded = node.status === 'degraded';
  const isActive = node.status === 'active';

  const cpuColor = node.cpu > 80 ? '#e11d48' : node.cpu > 60 ? '#f59e0b' : '#10b981';
  const ramColor = node.ram > 80 ? '#e11d48' : node.ram > 60 ? '#f59e0b' : '#3b82f6';

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.95, y: 20 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      whileHover={{ y: -4 }}
      className={cn(
        'relative rounded-2xl glass-strong p-5 space-y-5 transition-all duration-300 overflow-hidden border',
        isActive ? 'border-emerald-500/20 shadow-[0_0_15px_rgba(16,185,129,0.05)] hover:shadow-[0_0_30px_rgba(16,185,129,0.1)]' : '',
        isDegraded ? 'border-amber-500/40 shadow-[0_0_20px_rgba(245,158,11,0.15)] bg-gradient-to-br from-amber-500/5 to-transparent' : '',
        isOffline ? 'border-rose-500/30 opacity-70 grayscale-[50%]' : 'border-white/5'
      )}
    >
      {/* Warning Stripes Background for Degraded */}
      {isDegraded && (
        <div className="absolute inset-0 pointer-events-none opacity-[0.03]" style={{ backgroundImage: 'repeating-linear-gradient(45deg, transparent, transparent 10px, #f59e0b 10px, #f59e0b 20px)' }} />
      )}
      
      {/* CRT Scanline Overlay for Offline */}
      {isOffline && (
        <div className="absolute inset-0 pointer-events-none opacity-20 bg-[linear-gradient(rgba(18,16,16,0)_50%,rgba(0,0,0,0.25)_50%),linear-gradient(90deg,rgba(255,0,0,0.06),rgba(0,255,0,0.02),rgba(0,0,255,0.06))] bg-[length:100%_4px,3px_100%] z-10" />
      )}

      {/* Header */}
      <div className="relative z-20 flex items-start justify-between">
        <div>
          <div className="flex items-center gap-2">
            <span className={cn(
              'h-2.5 w-2.5 rounded-full',
              isActive && 'bg-emerald-500 shadow-[0_0_8px_#10b981]',
              isDegraded && 'bg-amber-500 animate-pulse shadow-[0_0_8px_#f59e0b]',
              isOffline && 'bg-rose-500 shadow-[0_0_8px_#e11d48]',
            )} />
            <span className={cn("font-mono text-[15px] font-bold tracking-tight", isOffline ? "text-zinc-500" : "text-zinc-100")}>{node.name}</span>
          </div>
          <span className="text-xs text-zinc-500 font-medium ml-4.5 block mt-0.5">{t(`sdn.location.${node.location}`, node.location)}</span>
        </div>
        <div className={cn(
          'rounded-md px-2 py-1 text-[10px] font-bold uppercase tracking-wider border flex items-center gap-1.5 shadow-inner',
          isActive && 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
          isDegraded && 'bg-amber-500/10 text-amber-400 border-amber-500/30',
          isOffline && 'bg-rose-500/10 text-rose-500 border-rose-500/20',
        )}>
          {isOffline && <WifiOff className="h-3 w-3" />}
          {isDegraded && <AlertTriangle className="h-3 w-3" />}
          {isOffline ? t('topbar.offline', 'OFFLINE') : isActive ? t('topbar.online', 'ONLINE') : 'DEGRADED'}
        </div>
      </div>

      {/* Gauges */}
      <div className="relative z-20 flex justify-around px-4">
        <CircularGauge value={node.cpu} label="CPU" color={cpuColor} offline={isOffline} />
        <CircularGauge value={node.ram} label="RAM" color={ramColor} offline={isOffline} />
      </div>

      {/* Traffic Mini-Chart */}
      <div className="relative z-20 h-16 -mx-2 -mb-2 border-t border-white/[0.03] pt-2">
        <ResponsiveContainer width="100%" height="100%" minWidth={1} minHeight={1}>
          <AreaChart data={node.trafficHistory}>
            <defs>
              <linearGradient id={`grad-${node.id}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor={isOffline ? '#52525b' : '#3b82f6'} stopOpacity={0.3}/>
                <stop offset="95%" stopColor={isOffline ? '#52525b' : '#3b82f6'} stopOpacity={0}/>
              </linearGradient>
            </defs>
            <Tooltip contentStyle={{ display: 'none' }} cursor={false} />
            <Area 
              type="monotone" 
              dataKey="value" 
              stroke={isOffline ? '#52525b' : '#3b82f6'} 
              strokeWidth={1.5} 
              fill={`url(#grad-${node.id})`} 
              isAnimationActive={false} 
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* Stats */}
      <div className="relative z-20 grid grid-cols-2 gap-3 text-xs bg-black/40 rounded-xl p-3 border border-white/5">
        <div>
          <div className="text-zinc-500 flex items-center gap-1.5 uppercase tracking-wider text-[10px] font-semibold mb-1"><Shield className="h-3 w-3" /> {t('sdn.filtered', 'Filtered')}</div>
          <div className={cn("font-mono font-bold text-sm", isOffline ? "text-zinc-600" : "text-zinc-200")}>{isOffline ? '--' : formatInt(node.filteredTraffic)}</div>
        </div>
        <div>
          <div className="text-zinc-500 flex items-center gap-1.5 uppercase tracking-wider text-[10px] font-semibold mb-1"><Activity className="h-3 w-3" /> {t('sdn.connections', 'Connections')}</div>
          <div className={cn("font-mono font-bold text-sm", isOffline ? "text-zinc-600" : "text-zinc-200")}>{isOffline ? '--' : formatInt(node.connections)}</div>
        </div>
      </div>

      {/* Dynamic Migration Toggle */}
      <div className="relative z-20 flex items-center justify-between pt-2">
        <div className={cn("flex items-center gap-2 text-xs font-medium", isOffline ? "text-zinc-600" : "text-zinc-400")}>
          <ArrowRightLeft className="h-3.5 w-3.5" />
          {t('sdn.dynamicMigration', 'Dynamic Migration')}
        </div>
        <Toggle enabled={node.dynamicMigration && !isOffline} onChange={() => onToggle(node.id)} disabled={isOffline} />
      </div>
    </motion.div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   SDN MONITORING PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function SdnMonitoringPage() {
  const { t } = useTranslation();
  const { nodes, loading, toggleMigration } = useSdnTelemetry();
  
  // Simulated Global Traffic Data
  const [globalTraffic, setGlobalTraffic] = useState<{ time: string; value: number }[]>([]);

  useEffect(() => {
    if (nodes.length === 0) return;
    const totalCurrentTraffic = nodes.reduce((s, n) => s + n.filteredTraffic, 0);
    setGlobalTraffic(prev => {
      const next = Math.max(20000, totalCurrentTraffic);
      const newArray = prev.length < 40 ? [...prev, { time: Date.now().toString(), value: next }] : [...prev.slice(1), { time: Date.now().toString(), value: next }];
      return newArray;
    });
  }, [nodes]);

  const stats = {
    active: nodes.filter((n) => n.status === 'active').length,
    totalFiltered: nodes.reduce((s, n) => s + n.filteredTraffic, 0),
    avgCpu: Math.round(nodes.filter((n) => n.status !== 'offline').reduce((s, n) => s + n.cpu, 0) / Math.max(1, nodes.filter((n) => n.status !== 'offline').length)),
  };

  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-6 pb-10">
      
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-white flex items-center gap-3">
            <Network className="h-6 w-6 text-emerald-400" />
            {t('sdn.title', 'SDN Мониторинг')}
          </h2>
          <p className="mt-1 text-sm text-zinc-400">
            {t('sdn.subtitle', 'Распределённые узлы фильтрации трафика. Управление балансировкой.')}
          </p>
        </div>
        <div className="hidden sm:flex items-center gap-2 text-xs text-zinc-500 font-mono bg-white/5 px-3 py-1.5 rounded-lg border border-white/10 shadow-inner">
           <RefreshCw className="h-3.5 w-3.5 text-emerald-500 animate-spin" style={{ animationDuration: '3s' }} />
           LIVE SYNC
        </div>
      </div>

      {/* Hero: Global Traffic Chart */}
      <div className="w-full h-48 rounded-2xl glass-strong border border-emerald-500/20 p-5 relative overflow-hidden shadow-[0_0_30px_rgba(16,185,129,0.05)]">
         <div className="absolute top-4 left-5 z-10 flex items-center gap-3">
            <div className="flex flex-col">
              <span className="text-[10px] font-bold uppercase tracking-widest text-emerald-500/80">Global Throughput</span>
              <span className="text-2xl font-mono font-bold text-white shadow-sm flex items-baseline gap-1">
                {globalTraffic.length > 0 ? formatInt(globalTraffic[globalTraffic.length - 1].value) : '--'} <span className="text-xs text-zinc-500 font-sans font-medium">pps</span>
              </span>
            </div>
         </div>
         <ResponsiveContainer width="100%" height="100%" minWidth={1} minHeight={1} className="absolute inset-0 pt-8">
          <AreaChart data={globalTraffic} margin={{ top: 20, right: 0, left: 0, bottom: 0 }}>
            <defs>
              <linearGradient id="globalTrafficGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="#10b981" stopOpacity={0.4}/>
                <stop offset="100%" stopColor="#10b981" stopOpacity={0}/>
              </linearGradient>
            </defs>
            <Area 
              type="monotone" 
              dataKey="value" 
              stroke="#10b981" 
              strokeWidth={2} 
              fill="url(#globalTrafficGrad)" 
              isAnimationActive={true} 
              animationDuration={500}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* KPI Row (Holographic Widgets) */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
        <div className="rounded-2xl bg-black/40 border border-white/5 shadow-inner p-5 flex items-center justify-between overflow-hidden relative group hover:border-white/10 transition-colors">
          <div className="relative z-10">
            <div className="text-[10px] uppercase tracking-widest font-bold text-zinc-500 mb-1">{t('topbar.online', 'Система онлайн')}</div>
            <div className="text-3xl font-bold text-emerald-400 font-mono drop-shadow-[0_0_8px_rgba(16,185,129,0.5)]">
              {stats.active}<span className="text-xl text-zinc-600">/{nodes.length}</span>
            </div>
          </div>
          <div className="relative z-10 opacity-50 group-hover:opacity-100 transition-opacity">
             <MiniSparkline data={[2, 4, 3, 5, 4, 4]} color="#10b981" />
          </div>
        </div>

        <div className="rounded-2xl bg-black/40 border border-white/5 shadow-inner p-5 flex items-center justify-between overflow-hidden relative group hover:border-white/10 transition-colors">
          <div className="relative z-10">
            <div className="text-[10px] uppercase tracking-widest font-bold text-zinc-500 mb-1">{t('sdn.filtered', 'Отфильтровано')}</div>
            <div className="text-3xl font-bold text-amber-400 font-mono drop-shadow-[0_0_8px_rgba(251,191,36,0.5)]">
              {formatInt(stats.totalFiltered)}
            </div>
          </div>
           <div className="relative z-10 opacity-50 group-hover:opacity-100 transition-opacity">
             <MiniSparkline data={[12, 15, 20, 18, 25, 30]} color="#fbbf24" />
          </div>
        </div>

        <div className="rounded-2xl bg-black/40 border border-white/5 shadow-inner p-5 flex items-center justify-between overflow-hidden relative group hover:border-white/10 transition-colors">
          <div className="relative z-10">
            <div className="text-[10px] uppercase tracking-widest font-bold text-zinc-500 mb-1">CPU (Avg)</div>
            <div className={cn('text-3xl font-bold font-mono', stats.avgCpu > 70 ? 'text-rose-400 drop-shadow-[0_0_8px_rgba(225,29,72,0.5)]' : 'text-blue-400 drop-shadow-[0_0_8px_rgba(59,130,246,0.5)]')}>
              {stats.avgCpu}<span className="text-xl text-zinc-600">%</span>
            </div>
          </div>
           <div className="relative z-10 opacity-50 group-hover:opacity-100 transition-opacity">
             <MiniSparkline data={[40, 45, 55, 50, 60, stats.avgCpu]} color={stats.avgCpu > 70 ? "#e11d48" : "#3b82f6"} />
          </div>
        </div>
      </div>

      {/* Node Grid */}
      <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
        {nodes.map((node) => (
          <NodeCard key={node.id} node={node} onToggle={toggleMigration} />
        ))}
      </div>
    </motion.section>
  );
}
