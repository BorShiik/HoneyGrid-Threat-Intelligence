import { create } from 'zustand';

export type ConnectionStatus = 'connected' | 'disconnected' | 'connecting';

interface ConnectionState {
  /** SignalR connection state for the /hubs/attacks hub. */
  status: ConnectionStatus;
  setStatus: (status: ConnectionStatus) => void;
  /**
   * True when the live stream is driven by the client-side simulator rather
   * than a real SignalR backend (the synthetic data fallback). Surfaced in the
   * header so an "online" dot doesn't imply the data is real.
   */
  simulated: boolean;
  setSimulated: (simulated: boolean) => void;
}

/**
 * Zustand store holding the live SignalR connection state.
 * Week 0: the value is a placeholder ('disconnected') — the real hub
 * connection (src/api/signalr.ts) will drive it from Week 1 onwards.
 */
export const useConnectionStore = create<ConnectionState>((set) => ({
  status: 'disconnected',
  setStatus: (status) => set({ status }),
  simulated: false,
  setSimulated: (simulated) => set({ simulated }),
}));
