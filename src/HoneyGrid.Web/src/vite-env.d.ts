/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** REST API base URL (HoneyGrid.Api Container App). Empty in dev/MSW. */
  readonly VITE_API_BASE?: string;
  /** SignalR Serverless negotiate base (Functions app). Empty in dev/MSW. */
  readonly VITE_SIGNALR_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
