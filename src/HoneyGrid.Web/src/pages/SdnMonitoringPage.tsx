import { useState } from 'react';
import { motion } from 'framer-motion';
import { Network, Shield, Activity, ArrowRightLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';

/* ── Mock SDN Nodes ── */
interface SdnNode {
  id: string;
  name: string;
  location: string;
  status: 'active' | 'degraded' | 'offline';
  cpu: number;
  ram: number;
  filteredTraffic: number;
  dynamicMigration: boolean;
  connections: number;
}

const MOCK_NODES: SdnNode[] = [
  { id: 'sdn-01', name: 'Edge-WEU-01', location: 'Франкфурт', status: 'active', cpu: 42, ram: 67, filteredTraffic: 12840, dynamicMigration: true, connections: 847 },
  { id: 'sdn-02', name: 'Edge-WEU-02', location: 'Амстердам', status: 'active', cpu: 28, ram: 45, filteredTraffic: 8320, dynamicMigration: false, connections: 623 },
  { id: 'sdn-03', name: 'Core-NEU-01', location: 'Дублин', status: 'active', cpu: 71, ram: 83, filteredTraffic: 23100, dynamicMigration: true, connections: 1204 },
  { id: 'sdn-04', name: 'Edge-EUS-01', location: 'Вирджиния', status: 'degraded', cpu: 89, ram: 91, filteredTraffic: 31200, dynamicMigration: true, connections: 1890 },
  { id: 'sdn-05', name: 'Edge-SEA-01', location: 'Сингапур', status: 'active', cpu: 35, ram: 52, filteredTraffic: 6750, dynamicMigration: false, connections: 412 },
  { id: 'sdn-06', name: 'Core-WUS-01', location: 'Сиэтл', status: 'offline', cpu: 0, ram: 12, filteredTraffic: 0, dynamicMigration: false, connections: 0 },
];

/* ── Circular Progress ── */
function CircularGauge({ value, label, color }: { value: number; label: string; color: string }) {
  const radius = 28;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (value / 100) * circumference;

  return (
    <div className="flex flex-col items-center gap-1">
      <svg width="68" height="68" className="-rotate-90">
        <circle cx="34" cy="34" r={radius} strokeWidth="4" className="stroke-zinc-800/60" fill="none" />
        <motion.circle
          cx="34" cy="34" r={radius} strokeWidth="4" fill="none"
          stroke={color}
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={{ strokeDashoffset: circumference }}
          animate={{ strokeDashoffset: offset }}
          transition={{ duration: 1.2, ease: 'easeOut' }}
        />
      </svg>
      <div className="text-center -mt-[52px] mb-2">
        <div className="text-sm font-bold font-mono tabular-nums text-white">{value}%</div>
      </div>
      <div className="text-[10px] text-zinc-500 mt-1">{label}</div>
    </div>
  );
}

/* ── Toggle Switch ── */
function Toggle({ enabled, onChange }: { enabled: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      onClick={() => onChange(!enabled)}
      className={cn(
        'relative inline-flex h-5 w-9 items-center rounded-full transition-colors',
        enabled ? 'bg-emerald-500/30' : 'bg-zinc-700',
      )}
      style={enabled ? { boxShadow: '0 0 8px rgba(16,185,129,0.3)' } : undefined}
    >
      <motion.span
        animate={{ x: enabled ? 17 : 2 }}
        transition={{ type: 'spring', stiffness: 500, damping: 30 }}
        className={cn('inline-block h-3.5 w-3.5 rounded-full', enabled ? 'bg-emerald-400' : 'bg-zinc-400')}
      />
    </button>
  );
}

/* ── Node Card ── */
function NodeCard({ node, onToggle }: { node: SdnNode; onToggle: (id: string) => void }) {
  const { t } = useTranslation();
  const cpuColor = node.cpu > 80 ? '#e11d48' : node.cpu > 60 ? '#f97316' : '#10b981';
  const ramColor = node.ram > 80 ? '#e11d48' : node.ram > 60 ? '#f97316' : '#3b82f6';

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.95 }}
      animate={{ opacity: 1, scale: 1 }}
      whileHover={{ y: -2 }}
      className={cn(
        'rounded-xl glass-strong p-4 space-y-4 transition-shadow',
        node.dynamicMigration && 'shadow-[0_0_20px_rgba(16,185,129,0.08)]',
        node.status === 'offline' && 'opacity-50',
      )}
    >
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <div className="flex items-center gap-2">
            <span className={cn(
              'h-2 w-2 rounded-full',
              node.status === 'active' && 'bg-emerald-500',
              node.status === 'degraded' && 'bg-amber-500 animate-pulse',
              node.status === 'offline' && 'bg-zinc-600',
            )} />
            <span className="font-mono text-sm font-semibold text-zinc-200">{node.name}</span>
          </div>
          <span className="text-xs text-zinc-500 ml-4">{node.location}</span>
        </div>
        <span className={cn(
          'rounded-md px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wider',
          node.status === 'active' && 'bg-emerald-500/10 text-emerald-400',
          node.status === 'degraded' && 'bg-amber-500/10 text-amber-400',
          node.status === 'offline' && 'bg-zinc-800 text-zinc-500',
        )}>
          {node.status === 'offline' ? t('topbar.offline') : node.status === 'active' ? t('topbar.online') : node.status}
        </span>
      </div>

      {/* Gauges */}
      <div className="flex justify-center gap-6">
        <CircularGauge value={node.cpu} label="CPU" color={cpuColor} />
        <CircularGauge value={node.ram} label="RAM" color={ramColor} />
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 gap-2 text-xs">
        <div className="rounded-lg bg-black/30 p-2">
          <div className="text-zinc-500 flex items-center gap-1"><Shield className="h-3 w-3" /> {t('sdn.filtered')}</div>
          <div className="font-mono font-medium text-zinc-200">{node.filteredTraffic.toLocaleString()}</div>
        </div>
        <div className="rounded-lg bg-black/30 p-2">
          <div className="text-zinc-500 flex items-center gap-1"><Activity className="h-3 w-3" /> {t('sdn.connections')}</div>
          <div className="font-mono font-medium text-zinc-200">{node.connections.toLocaleString()}</div>
        </div>
      </div>

      {/* Dynamic Migration Toggle */}
      <div className="flex items-center justify-between pt-1 border-t border-white/5">
        <div className="flex items-center gap-1.5 text-xs text-zinc-400">
          <ArrowRightLeft className="h-3 w-3" />
          {t('sdn.dynamicMigration')}
        </div>
        <Toggle enabled={node.dynamicMigration} onChange={() => onToggle(node.id)} />
      </div>
    </motion.div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   SDN MONITORING PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function SdnMonitoringPage() {
  const { t } = useTranslation();
  const [nodes, setNodes] = useState(MOCK_NODES);

  const toggleMigration = (id: string) => {
    setNodes((prev) =>
      prev.map((n) => (n.id === id ? { ...n, dynamicMigration: !n.dynamicMigration } : n)),
    );
  };

  const stats = {
    active: nodes.filter((n) => n.status === 'active').length,
    totalFiltered: nodes.reduce((s, n) => s + n.filteredTraffic, 0),
    avgCpu: Math.round(nodes.filter((n) => n.status !== 'offline').reduce((s, n) => s + n.cpu, 0) / Math.max(1, nodes.filter((n) => n.status !== 'offline').length)),
  };

  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-5">
      <div>
        <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
          <Network className="h-5 w-5 text-amber-500" />
          {t('sdn.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500">
          {t('sdn.subtitle')}
        </p>
      </div>

      {/* KPI Row */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="rounded-xl glass-strong p-4">
          <div className="text-xs text-zinc-500">{t('topbar.online')}</div>
          <div className="text-2xl font-bold text-emerald-400 font-mono">{stats.active}/{nodes.length}</div>
        </div>
        <div className="rounded-xl glass-strong p-4">
          <div className="text-xs text-zinc-500">{t('sdn.filtered')}</div>
          <div className="text-2xl font-bold text-amber-400 font-mono">{stats.totalFiltered.toLocaleString()}</div>
        </div>
        <div className="rounded-xl glass-strong p-4">
          <div className="text-xs text-zinc-500">CPU (Avg)</div>
          <div className={cn('text-2xl font-bold font-mono', stats.avgCpu > 70 ? 'text-rose-400' : 'text-zinc-200')}>{stats.avgCpu}%</div>
        </div>
      </div>

      {/* Node Grid */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {nodes.map((node) => (
          <NodeCard key={node.id} node={node} onToggle={toggleMigration} />
        ))}
      </div>
    </motion.section>
  );
}
