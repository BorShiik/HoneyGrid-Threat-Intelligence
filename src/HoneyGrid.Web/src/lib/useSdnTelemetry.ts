import { useEffect, useState } from 'react';
import { apiGet, apiPost } from '@/api/client';
import { getAttackHubConnection } from '@/api/signalr';

export interface SdnNodeState {
  id: string;
  name: string;
  location: string;
  status: 'active' | 'degraded' | 'offline';
  dynamicMigration: boolean;
}

export interface SdnTelemetryEvent {
  id: string;
  cpu: number;
  ram: number;
  filteredTraffic: number;
  connections: number;
}

export interface SdnNode extends SdnNodeState, SdnTelemetryEvent {
  trafficHistory: { time: string; value: number }[];
}

export function useSdnTelemetry() {
  const [nodes, setNodes] = useState<SdnNode[]>([]);
  const [loading, setLoading] = useState(true);

  // Initialize nodes from API
  useEffect(() => {
    apiGet<SdnNodeState[]>('/api/sdn/nodes')
      .then(data => {
        setNodes(data.map(n => ({
          ...n,
          cpu: 0,
          ram: 0,
          filteredTraffic: 0,
          connections: 0,
          trafficHistory: Array(20).fill(0).map((_, i) => ({ time: String(i), value: 0 }))
        })));
        setLoading(false);
      })
      .catch(console.error);
  }, []);

  // Connect to SignalR or run local simulator for MSW dev mode
  useEffect(() => {
    if (nodes.length === 0) return;
    let timer: any;
    let disposed = false;

    const applyTelemetry = (events: SdnTelemetryEvent[]) => {
      if (disposed) return;
      setNodes(prev => prev.map(n => {
        const ev = events.find(e => e.id === n.id);
        if (!ev) return n;
        const newHistory = [...n.trafficHistory.slice(1), { time: Date.now().toString(), value: ev.filteredTraffic }];
        return { ...n, ...ev, trafficHistory: newHistory };
      }));
    };

    if (import.meta.env.PROD) {
      const hub = getAttackHubConnection();
      hub.on('sdnTelemetry', applyTelemetry);
    } else {
      // Dev mode MSW fallback simulator (mirrors the Azure Function logic)
      timer = setInterval(() => {
        const mockEvents = nodes.map(node => {
          if (node.status === 'offline') {
            return { id: node.id, cpu: 0, ram: 12, filteredTraffic: 0, connections: 0 };
          }
          const baseTraffic = node.id === 'sdn-03' ? 23000 : node.id === 'sdn-04' ? 31000 : node.id === 'sdn-01' ? 12000 : 6000;
          const baseCpu = node.status === 'degraded' ? 85 : 40;
          return {
            id: node.id,
            cpu: Math.min(100, Math.max(5, baseCpu + Math.floor(Math.random() * 25 - 10))),
            ram: Math.min(95, Math.max(20, baseCpu + Math.floor(Math.random() * 20 + 5))),
            filteredTraffic: Math.max(0, baseTraffic + Math.floor(Math.random() * 4000 - 2000)),
            connections: Math.max(0, Math.floor(baseTraffic / 15) + Math.floor(Math.random() * 200 - 100)),
          };
        });
        applyTelemetry(mockEvents);
      }, 5000);
    }

    return () => {
      disposed = true;
      if (timer) clearInterval(timer);
      if (import.meta.env.PROD) getAttackHubConnection().off('sdnTelemetry', applyTelemetry);
    };
  }, [nodes.length > 0]);

  const toggleMigration = async (id: string) => {
    // Optimistic UI update
    setNodes(prev => prev.map(n => n.id === id ? { ...n, dynamicMigration: !n.dynamicMigration } : n));
    try {
      await apiPost(`/api/sdn/nodes/${id}/migration`);
    } catch (e) {
      console.error('Failed to toggle migration', e);
      // Revert on failure
      setNodes(prev => prev.map(n => n.id === id ? { ...n, dynamicMigration: !n.dynamicMigration } : n));
    }
  };

  return { nodes, loading, toggleMigration };
}
