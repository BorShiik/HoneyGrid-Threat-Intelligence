import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface SettingsState {
  muteToasts: boolean;
  setMuteToasts: (mute: boolean) => void;
}

export const useSettingsStore = create<SettingsState>()(
  persist(
    (set) => ({
      muteToasts: false,
      setMuteToasts: (muteToasts) => set({ muteToasts }),
    }),
    { name: 'hg-settings' }
  )
);
