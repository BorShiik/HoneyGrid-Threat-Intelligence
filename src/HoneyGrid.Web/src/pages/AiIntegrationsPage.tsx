import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Brain, CheckCircle2, ChevronDown, Settings, Terminal, WifiOff } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';

/* ── Mock AI/MCP Server Data ── */
interface McpServer {
  id: string;
  name: string;
  provider: string;
  status: 'connected' | 'disconnected' | 'error';
  endpoint: string;
  tools: string[];
  lastPing: number;
  requestsToday: number;
}

interface AuditEntry {
  id: string;
  timestamp: string;
  server: string;
  tool: string;
  input: string;
  latencyMs: number;
  status: 'success' | 'error';
}

const MOCK_SERVERS: McpServer[] = [
  {
    id: 'mcp-01', name: 'ThreatIntel Analyzer', provider: 'Cloudflare Workers AI',
    status: 'connected', endpoint: 'https://ti-analyzer.honeygrid.workers.dev',
    tools: ['query_threat_logs', 'enrich_ip', 'classify_attack', 'generate_ioc'],
    lastPing: 12, requestsToday: 847,
  },
  {
    id: 'mcp-02', name: 'Sentinel Bridge', provider: 'Azure Functions',
    status: 'connected', endpoint: 'https://hg-sentinel-bridge.azurewebsites.net/mcp',
    tools: ['create_incident', 'update_watchlist', 'run_kql_query'],
    lastPing: 34, requestsToday: 312,
  },
  {
    id: 'mcp-03', name: 'Actor Profiler', provider: 'Azure OpenAI (gpt-4o-mini)',
    status: 'connected', endpoint: 'https://hg-ai-profiler.openai.azure.com',
    tools: ['build_actor_dossier', 'cluster_sessions', 'assess_sophistication'],
    lastPing: 89, requestsToday: 156,
  },
  {
    id: 'mcp-04', name: 'OSINT Enrichment', provider: 'Self-hosted',
    status: 'disconnected', endpoint: 'https://osint.internal.honeygrid.net/v1',
    tools: ['whois_lookup', 'dns_history', 'certificate_transparency'],
    lastPing: -1, requestsToday: 0,
  },
];

const MOCK_AUDIT: AuditEntry[] = [
  { id: 'a1', timestamp: '2026-06-14T18:12:04Z', server: 'ThreatIntel Analyzer', tool: 'classify_attack', input: '{"ip":"185.220.101.42","type":"brute-force"}', latencyMs: 142, status: 'success' },
  { id: 'a2', timestamp: '2026-06-14T18:11:58Z', server: 'Sentinel Bridge', tool: 'create_incident', input: '{"severity":"high","title":"Mass brute-force from AS12389"}', latencyMs: 234, status: 'success' },
  { id: 'a3', timestamp: '2026-06-14T18:11:41Z', server: 'Actor Profiler', tool: 'build_actor_dossier', input: '{"actorId":"actor-7f3a9c21"}', latencyMs: 1820, status: 'success' },
  { id: 'a4', timestamp: '2026-06-14T18:11:22Z', server: 'ThreatIntel Analyzer', tool: 'enrich_ip', input: '{"ip":"43.156.88.201"}', latencyMs: 89, status: 'success' },
  { id: 'a5', timestamp: '2026-06-14T18:10:55Z', server: 'OSINT Enrichment', tool: 'whois_lookup', input: '{"ip":"203.0.113.45"}', latencyMs: 0, status: 'error' },
  { id: 'a6', timestamp: '2026-06-14T18:10:30Z', server: 'ThreatIntel Analyzer', tool: 'generate_ioc', input: '{"hash":"sha256:e3b0c44...","type":"malware-dropper"}', latencyMs: 312, status: 'success' },
];

/* ── Server Card ── */
function ServerCard({ server }: { server: McpServer }) {
  const { t } = useTranslation();
  const [configOpen, setConfigOpen] = useState(false);

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className="rounded-xl glass-strong overflow-hidden"
    >
      <div className="p-4 space-y-3">
        {/* Header */}
        <div className="flex items-start justify-between">
          <div className="flex items-center gap-2.5">
            <div className={cn(
              'flex h-8 w-8 items-center justify-center rounded-lg',
              server.status === 'connected' ? 'bg-emerald-500/10' : 'bg-zinc-800',
            )}>
              <Brain className={cn('h-4 w-4', server.status === 'connected' ? 'text-emerald-400' : 'text-zinc-600')} />
            </div>
            <div>
              <div className="text-sm font-semibold text-zinc-200">{server.name}</div>
              <div className="text-[10px] text-zinc-500">{server.provider}</div>
            </div>
          </div>

          {/* WebSocket indicator */}
          <div className="flex items-center gap-1.5">
            {server.status === 'connected' ? (
              <>
                <span className="relative flex h-2 w-2">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-50" />
                  <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
                </span>
                <span className="text-[10px] text-emerald-400">WS {t('ai.connected')}</span>
              </>
            ) : (
              <>
                <WifiOff className="h-3 w-3 text-zinc-600" />
                <span className="text-[10px] text-zinc-500">{t('ai.disconnected')}</span>
              </>
            )}
          </div>
        </div>

        {/* Tools */}
        <div className="flex flex-wrap gap-1">
          {server.tools.map((tool) => (
            <span key={tool} className="rounded-md bg-white/5 px-1.5 py-0.5 font-mono text-[10px] text-zinc-400">
              {tool}
            </span>
          ))}
        </div>

        {/* Stats */}
        <div className="flex items-center gap-4 text-xs text-zinc-500">
          <span>{t('ai.ping')}: <span className={cn('font-mono', server.lastPing > 0 ? 'text-zinc-300' : 'text-zinc-600')}>{server.lastPing > 0 ? `${server.lastPing}ms` : '—'}</span></span>
          <span>{t('ai.requests')}: <span className="font-mono text-zinc-300">{server.requestsToday}</span></span>
        </div>

        {/* Config toggle */}
        <button
          onClick={() => setConfigOpen((p) => !p)}
          className="flex items-center gap-1.5 text-[11px] text-zinc-500 hover:text-zinc-300 transition-colors"
        >
          <Settings className="h-3 w-3" />
          {t('ai.configuration')}
          <ChevronDown className={cn('h-3 w-3 transition-transform', configOpen && 'rotate-180')} />
        </button>

        {/* Hidden config fields */}
        <AnimatePresence>
          {configOpen && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ type: 'spring', stiffness: 300, damping: 30 }}
              className="overflow-hidden space-y-2"
            >
              <div className="rounded-lg bg-black/30 p-2.5 space-y-2 text-xs">
                <div>
                  <label className="text-zinc-500 text-[10px] uppercase tracking-wider">{t('ai.endpoint')}</label>
                  <div className="font-mono text-zinc-300 text-[11px] mt-0.5 break-all">{server.endpoint}</div>
                </div>
                <div>
                  <label className="text-zinc-500 text-[10px] uppercase tracking-wider">Auth</label>
                  <div className="font-mono text-zinc-500 text-[11px] mt-0.5">OAuth 2.0 / Managed Identity</div>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </motion.div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   AI INTEGRATIONS PAGE
   ══════════════════════════════════════════════════════════════════════ */
export function AiIntegrationsPage() {
  const { t } = useTranslation();
  return (
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-5">
      <div>
        <h2 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
          <Brain className="h-5 w-5 text-amber-500" />
          {t('ai.title')}
        </h2>
        <p className="mt-1 text-sm text-zinc-500">
          {t('ai.subtitle')}
        </p>
      </div>

      {/* Server Cards Grid */}
      <div className="grid gap-4 sm:grid-cols-2">
        {MOCK_SERVERS.map((server) => (
          <ServerCard key={server.id} server={server} />
        ))}
      </div>

      {/* Audit Log */}
      <div className="rounded-xl glass-strong overflow-hidden">
        <div className="flex items-center gap-2 px-4 py-3 border-b border-white/5">
          <Terminal className="h-4 w-4 text-zinc-500" />
          <h3 className="text-sm font-semibold text-zinc-200">{t('ai.auditLog')}</h3>
        </div>
        <div className="overflow-x-auto">
        <div className="min-w-[640px] max-h-80 overflow-y-auto">
          {MOCK_AUDIT.map((entry) => (
            <div
              key={entry.id}
              className="flex items-center gap-3 px-4 py-2 border-b border-white/[0.03] text-xs hover:bg-white/[0.02] transition-colors"
            >
              {entry.status === 'success' ? (
                <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500 shrink-0" />
              ) : (
                <span className="h-3.5 w-3.5 rounded-full bg-rose-500/20 flex items-center justify-center shrink-0">
                  <span className="h-1.5 w-1.5 rounded-full bg-rose-500" />
                </span>
              )}
              <span className="font-mono text-zinc-500 w-16 shrink-0">
                {new Date(entry.timestamp).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
              </span>
              <span className="text-zinc-400 w-36 shrink-0 truncate">{entry.server}</span>
              <span className="font-mono text-amber-400/80 w-40 shrink-0">{entry.tool}</span>
              <span className="flex-1 font-mono text-zinc-600 truncate">{entry.input}</span>
              <span className={cn(
                'font-mono tabular-nums w-14 text-right shrink-0',
                entry.latencyMs > 1000 ? 'text-amber-400' : entry.latencyMs > 0 ? 'text-zinc-400' : 'text-rose-400',
              )}>
                {entry.latencyMs > 0 ? `${entry.latencyMs}ms` : 'FAIL'}
              </span>
            </div>
          ))}
        </div>
        </div>
      </div>
    </motion.section>
  );
}
