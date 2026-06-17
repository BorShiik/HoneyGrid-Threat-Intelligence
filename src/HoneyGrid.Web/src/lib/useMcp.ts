import { useEffect, useState } from 'react';
import { funcGet } from '@/api/client';
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
    funcGet<McpServerState[]>('/api/mcp/servers')
      .then(data => {
        setServers(data);
        setLoading(false);
      })
      .catch(console.error);
  }, []);

  // Subscribe to the real AI audit log broadcast over SignalR (emitted by ClassifyEvents).
  useEffect(() => {
    let disposed = false;

    const applyAudit = (events: AiAuditEntry[]) => {
      if (disposed) return;
      setAuditLog(prev => {
        const next = [...events, ...prev];
        return next.slice(0, 100); // Keep last 100 logs
      });
    };

    const hub = getAttackHubConnection();
    hub.on('aiAuditLog', applyAudit);

    return () => {
      disposed = true;
      hub.off('aiAuditLog', applyAudit);
    };
  }, []);

  return { servers, auditLog, loading };
}
