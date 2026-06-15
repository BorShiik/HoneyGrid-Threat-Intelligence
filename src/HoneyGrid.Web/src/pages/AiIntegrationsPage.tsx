import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Brain, CheckCircle2, Settings, Terminal, WifiOff, Activity, AlertCircle } from 'lucide-react';
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

/* ── JSON Syntax Highlighter ── */
function JsonHighlighter({ jsonString }: { jsonString: string }) {
  // A simple tokenizer for flat JSON logs
  const renderToken = (str: string) => {
    // String matching
    return str.split(/(".*?"|[{}:,])/).map((part, i) => {
      if (part === '{' || part === '}') return <span key={i} className="text-zinc-500">{part}</span>;
      if (part === ':' || part === ',') return <span key={i} className="text-zinc-500 mx-0.5">{part}</span>;
      if (part.startsWith('"') && part.endsWith('"')) {
        // Distinguish between key and value heuristically if needed, 
        // but simple highlighting works best:
        // Assume anything right before a colon is a key
        const isKey = str.includes(part + '":') || str.includes(part + '": ');
        if (isKey) {
          return <span key={i} className="text-blue-300">{part}</span>;
        }
        return <span key={i} className="text-emerald-400">{part}</span>;
      }
      if (!isNaN(Number(part)) && part.trim() !== '') {
        return <span key={i} className="text-amber-400">{part}</span>;
      }
      return <span key={i} className="text-zinc-400">{part}</span>;
    });
  };

  return <span className="font-mono text-[11px] bg-black/40 px-2 py-1 rounded-md border border-white/5 shadow-inner">{renderToken(jsonString)}</span>;
}


/* ── Server Card (Cyber Node) ── */
function ServerCard({ server, index }: { server: McpServer; index: number }) {
  const { t } = useTranslation();
  const [configOpen, setConfigOpen] = useState(false);
  const isConnected = server.status === 'connected';

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.1, type: 'spring', stiffness: 300, damping: 30 }}
      className="relative rounded-2xl glass-strong overflow-hidden group border border-white/5 hover:border-white/10 transition-colors bg-gradient-to-b from-white/[0.02] to-transparent"
    >
      {/* Status Glow Line */}
      <div className={cn(
        "absolute top-0 left-0 right-0 h-[2px] transition-all duration-500",
        isConnected ? "bg-emerald-500 shadow-[0_0_15px_rgba(16,185,129,0.8)]" : "bg-zinc-700"
      )} />

      <div className="p-5 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between">
          <div className="flex items-center gap-3">
            <div className={cn(
              'flex h-10 w-10 items-center justify-center rounded-xl shadow-inner border',
              isConnected ? 'bg-emerald-500/10 border-emerald-500/20' : 'bg-black/40 border-white/5'
            )}>
              <Brain className={cn('h-5 w-5', isConnected ? 'text-emerald-400 drop-shadow-[0_0_8px_rgba(16,185,129,0.5)]' : 'text-zinc-600')} />
            </div>
            <div>
              <div className="text-base font-bold text-white tracking-tight">{server.name}</div>
              <div className="text-[11px] uppercase tracking-widest font-semibold text-zinc-500 mt-0.5">{server.provider}</div>
            </div>
          </div>

          {/* WebSocket indicator */}
          <div className="flex flex-col items-end gap-1">
            <div className="flex items-center gap-1.5 bg-black/40 px-2 py-1 rounded-md border border-white/5">
              {isConnected ? (
                <>
                  <span className="relative flex h-2 w-2">
                    <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                    <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500 shadow-[0_0_8px_#10b981]" />
                  </span>
                  <span className="text-[10px] font-bold tracking-wider text-emerald-400 uppercase">WS {t('ai.connected', 'Połączono')}</span>
                </>
              ) : (
                <>
                  <WifiOff className="h-3 w-3 text-zinc-600" />
                  <span className="text-[10px] font-bold tracking-wider text-zinc-500 uppercase">{t('ai.disconnected', 'Rozłączono')}</span>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Tools as Terminal Commands */}
        <div className="space-y-1.5 pt-2">
          <div className="text-[10px] uppercase tracking-widest text-zinc-600 font-semibold">{t('ai.availableTools', 'Zarejestrowane narzędzia')}</div>
          <div className="flex flex-wrap gap-2">
            {server.tools.map((tool) => (
              <span key={tool} className="rounded-md bg-black/50 border border-white/5 px-2 py-1 font-mono text-[10px] text-zinc-300 shadow-inner group-hover:border-zinc-700 transition-colors">
                <span className="text-amber-500/50 mr-1">{'>_'}</span>{tool}
              </span>
            ))}
          </div>
        </div>

        {/* Stats */}
        <div className="flex items-center gap-6 pt-2 border-t border-white/5">
          <div className="flex items-center gap-2">
            <Activity className={cn("h-3.5 w-3.5", server.lastPing > 0 && server.lastPing < 50 ? "text-emerald-500" : server.lastPing >= 50 ? "text-amber-500" : "text-zinc-600")} />
            <span className="text-[11px] text-zinc-500 uppercase tracking-widest font-semibold">{t('ai.ping', 'Ping')}: <span className={cn('font-mono font-bold tracking-normal', server.lastPing > 0 ? 'text-zinc-200' : 'text-zinc-600')}>{server.lastPing > 0 ? `${server.lastPing}ms` : '—'}</span></span>
          </div>
          <div className="flex items-center gap-2">
            <Terminal className="h-3.5 w-3.5 text-zinc-600" />
            <span className="text-[11px] text-zinc-500 uppercase tracking-widest font-semibold">{t('ai.requests', 'Zapytania')}: <span className="font-mono font-bold tracking-normal text-zinc-200">{server.requestsToday}</span></span>
          </div>
        </div>

        {/* Config toggle */}
        <button
          onClick={() => setConfigOpen((p) => !p)}
          className="flex items-center gap-1.5 text-[11px] font-bold tracking-widest uppercase text-zinc-500 hover:text-white transition-colors pt-2 outline-none w-fit"
        >
          <Settings className={cn("h-3.5 w-3.5 transition-transform duration-500", configOpen && "rotate-90")} />
          {t('ai.configuration', 'Konfiguracja')}
        </button>

        {/* Hidden config terminal */}
        <AnimatePresence>
          {configOpen && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ type: 'spring', stiffness: 300, damping: 30 }}
              className="overflow-hidden"
            >
              <div className="mt-2 rounded-lg bg-black border border-white/10 p-4 space-y-3 shadow-inner relative">
                {/* Terminal dots */}
                <div className="absolute top-2 left-2 flex gap-1.5">
                  <div className="w-2 h-2 rounded-full bg-rose-500/50" />
                  <div className="w-2 h-2 rounded-full bg-amber-500/50" />
                  <div className="w-2 h-2 rounded-full bg-emerald-500/50" />
                </div>
                
                <div className="pt-2">
                  <label className="text-zinc-600 text-[9px] uppercase tracking-widest font-bold">endpoint_url</label>
                  <div className="font-mono text-emerald-400 text-[11px] mt-0.5 break-all">{server.endpoint}</div>
                </div>
                <div>
                  <label className="text-zinc-600 text-[9px] uppercase tracking-widest font-bold">auth_method</label>
                  <div className="font-mono text-amber-400 text-[11px] mt-0.5">OAuth 2.0 / Managed Identity</div>
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
    <motion.section initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-6 pb-10">
      <div>
        <h2 className="text-2xl font-bold tracking-tight text-white flex items-center gap-3">
          <Brain className="h-6 w-6 text-amber-500 drop-shadow-[0_0_8px_rgba(245,158,11,0.5)]" />
          {t('ai.title', 'Integracje AI (MCP)')}
        </h2>
        <p className="mt-1 text-sm text-zinc-400">
          {t('ai.subtitle', 'Serwery MCP i dziennik audytu narzędzi AI')}
        </p>
      </div>

      {/* Server Cards Grid */}
      <div className="grid gap-5 xl:grid-cols-2">
        {MOCK_SERVERS.map((server, idx) => (
          <ServerCard key={server.id} server={server} index={idx} />
        ))}
      </div>

      {/* Live Audit Terminal */}
      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.4, type: 'spring', stiffness: 300, damping: 30 }}
        className="rounded-2xl bg-[#0a0a0a] border border-white/10 shadow-2xl overflow-hidden flex flex-col relative"
      >
        {/* Terminal Header */}
        <div className="flex items-center justify-between px-4 py-2.5 bg-[#141414] border-b border-white/5">
           <div className="flex items-center gap-2">
             <div className="flex gap-1.5 mr-2">
               <div className="w-3 h-3 rounded-full bg-rose-500/80 border border-rose-500 shadow-inner" />
               <div className="w-3 h-3 rounded-full bg-amber-500/80 border border-amber-500 shadow-inner" />
               <div className="w-3 h-3 rounded-full bg-emerald-500/80 border border-emerald-500 shadow-inner" />
             </div>
             <Terminal className="h-4 w-4 text-zinc-400" />
             <h3 className="text-[11px] font-mono font-bold uppercase tracking-widest text-zinc-300">{t('ai.auditLog', 'Dziennik audytu AI')}</h3>
           </div>
           
           <div className="flex items-center gap-2">
             <span className="relative flex h-2 w-2">
               <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-rose-400 opacity-50" />
               <span className="relative inline-flex h-2 w-2 rounded-full bg-rose-500" />
             </span>
             <span className="text-[9px] font-mono font-bold text-rose-500 uppercase tracking-widest">Live Stream</span>
           </div>
        </div>

        {/* Terminal Body */}
        <div className="p-4 overflow-x-auto">
          <div className="min-w-[800px] max-h-[400px] overflow-y-auto custom-scrollbar pr-2 space-y-1">
            {MOCK_AUDIT.map((entry, i) => (
              <motion.div
                initial={{ opacity: 0, x: -8 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: 0.5 + Math.min(i, 20) * 0.05 }}
                key={entry.id}
                className="group flex items-center gap-4 px-3 py-2 rounded-lg hover:bg-white/[0.03] transition-colors border border-transparent hover:border-white/5"
              >
                {/* Status Indicator */}
                {entry.status === 'success' ? (
                  <CheckCircle2 className="h-4 w-4 text-emerald-500 drop-shadow-[0_0_5px_rgba(16,185,129,0.5)] shrink-0" />
                ) : (
                  <AlertCircle className="h-4 w-4 text-rose-500 drop-shadow-[0_0_5px_rgba(225,29,72,0.5)] shrink-0" />
                )}

                {/* Timestamp */}
                <span className="font-mono text-zinc-500 text-[11px] w-16 shrink-0 group-hover:text-zinc-400 transition-colors">
                  {new Date(entry.timestamp).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                </span>

                {/* Server */}
                <span className="text-[11px] font-bold text-zinc-300 w-40 shrink-0 truncate">{entry.server}</span>

                {/* Tool (Shell command) */}
                <span className="font-mono text-[11px] text-amber-400 font-bold w-48 shrink-0 flex items-center gap-2">
                   <span className="text-zinc-600">~$</span> {entry.tool}
                </span>

                {/* JSON Payload */}
                <span className="flex-1 min-w-0">
                  <JsonHighlighter jsonString={entry.input} />
                </span>

                {/* Latency */}
                <div className={cn(
                  'font-mono tabular-nums text-[11px] font-bold w-20 text-right shrink-0 px-2 py-1 rounded-md border shadow-inner transition-colors',
                  entry.status === 'error' 
                    ? 'bg-rose-500/20 text-rose-400 border-rose-500/30' 
                    : entry.latencyMs > 1000 
                      ? 'bg-amber-500/20 text-amber-400 border-amber-500/30 animate-pulse' 
                      : 'bg-black/40 text-zinc-400 border-white/5 group-hover:border-white/10 group-hover:text-zinc-200'
                )}>
                  {entry.status === 'error' ? 'FAIL' : `${entry.latencyMs}ms`}
                </div>
              </motion.div>
            ))}
            
            {/* Blinking Cursor */}
            <div className="flex items-center gap-2 px-3 py-2 mt-2">
              <span className="w-2 h-4 bg-zinc-500 animate-pulse" />
            </div>
          </div>
        </div>
      </motion.div>
    </motion.section>
  );
}
