import { create } from 'zustand';

export type ConnectionStatus = 'connected' | 'disconnected' | 'connecting';

interface ConnectionState {
  /** SignalR connection state for the /hubs/attacks hub. */
  status: ConnectionStatus;
  setStatus: (status: ConnectionStatus) => void;
}

/**
 * Zustand store holding the live SignalR connection state.
 * Week 0: the value is a placeholder ('disconnected') — the real hub
 * connection (src/api/signalr.ts) will drive it from Week 1 onwards.
 */
export const useConnectionStore = create<ConnectionState>((set) => ({
  status: 'disconnected',
  setStatus: (status) => set({ status }),
}));
