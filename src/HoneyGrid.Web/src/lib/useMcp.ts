import { useEffect, useState } from 'react';
import { apiGet } from '@/api/client';
import { getAttackHubConnection } from '@/api/signalr';

export interface McpServerState {
  id: string;
  name: string;
  provider: string;
  status: 'connected' | 'disconnected' | 'error';
  endpoint: string;
  tools: string[];
  lastPing: number;
  requestsToday: number;
}

export interface AiAuditEntry {
  id: string;
  timestamp: string;
  server: string;
  tool: string;
  input: string;
  latencyMs: number;
  status: 'success' | 'error';
}

export function useMcp() {
  const [servers, setServers] = useState<McpServerState[]>([]);
  const [auditLog, setAuditLog] = useState<AiAuditEntry[]>([]);
  const [loading, setLoading] = useState(true);

  // Initialize servers from API
  useEffect(() => {
    apiGet<McpServerState[]>('/api/mcp/servers')
      .then(data => {
        setServers(data);
        setLoading(false);
      })
      .catch(console.error);
  }, []);

  // Connect to SignalR or run local simulator for MSW dev mode
  useEffect(() => {
    let timer: any;
    let disposed = false;
    let seq = 0;

    const applyAudit = (events: AiAuditEntry[]) => {
      if (disposed) return;
      setAuditLog(prev => {
        const next = [...events, ...prev];
        return next.slice(0, 100); // Keep last 100 logs
      });
    };

    if (import.meta.env.PROD) {
      const hub = getAttackHubConnection();
      hub.on('aiAuditLog', applyAudit);
    } else {
      // Dev mode MSW fallback simulator
      const Tools01 = ["query_threat_logs", "enrich_ip", "classify_attack", "generate_ioc"];
      const Tools02 = ["create_incident", "update_watchlist", "run_kql_query"];
      const Tools03 = ["build_actor_dossier", "cluster_sessions", "assess_sophistication"];

      timer = setInterval(() => {
        const burst = Math.floor(Math.random() * 2) + 1;
        const events: AiAuditEntry[] = [];
        
        for (let i = 0; i < burst; i++) {
          seq++;
          const serverChoice = Math.floor(Math.random() * 3);
          let serverName = "";
          let tool = "";
          let input = "";

          if (serverChoice === 0) {
            serverName = "ThreatIntel Analyzer";
            tool = Tools01[Math.floor(Math.random() * Tools01.length)];
            if (tool === "classify_attack") input = `{"ip":"185.${Math.floor(Math.random()*100)+100}.101.42","type":"brute-force"}`;
            else if (tool === "enrich_ip") input = `{"ip":"43.156.88.201"}`;
            else input = `{"hash":"sha256:e3b0c44..."}`;
          } else if (serverChoice === 1) {
            serverName = "Sentinel Bridge";
            tool = Tools02[Math.floor(Math.random() * Tools02.length)];
            input = `{"severity":"high","title":"Mass brute-force"}`;
          } else {
            serverName = "Actor Profiler";
            tool = Tools03[Math.floor(Math.random() * Tools03.length)];
            input = `{"actorId":"actor-7f3a9c21"}`;
          }

          const isError = Math.random() > 0.9;
          
          events.push({
            id: `dev-audit-${seq}`,
            timestamp: new Date().toISOString(),
            server: serverName,
            tool,
            input,
            latencyMs: isError ? 0 : Math.floor(Math.random() * 1000 + 50),
            status: isError ? 'error' : 'success'
          });
        }
        applyAudit(events);
      }, 3000);
    }

    return () => {
      disposed = true;
      if (timer) clearInterval(timer);
      if (import.meta.env.PROD) getAttackHubConnection().off('aiAuditLog', applyAudit);
    };
  }, []);

  return { servers, auditLog, loading };
}
