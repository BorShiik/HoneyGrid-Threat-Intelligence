import { setupWorker } from 'msw/browser';
import { handlers } from './handlers';

/** MSW worker for dev mode (started conditionally in main.tsx). */
export const worker = setupWorker(...handlers);
