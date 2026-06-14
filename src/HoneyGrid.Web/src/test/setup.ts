import '@testing-library/jest-dom/vitest';
import '@/i18n'; // initialise i18next (pl) so useTranslation works in tests
import { afterAll, afterEach, beforeAll } from 'vitest';
import { setupServer } from 'msw/node';
import { handlers } from '@/mocks/handlers';

/** Node-side MSW server reused by all tests. */
export const server = setupServer(...handlers);

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
