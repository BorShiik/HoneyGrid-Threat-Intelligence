/** Thin fetch wrapper for the HoneyGrid REST API. */

/**
 * Base URL for the REST API (HoneyGrid.Api Container App).
 *
 * In dev/MSW this stays empty, so requests are resolved against the dev origin
 * and intercepted by the mock service worker. In production set VITE_API_BASE
 * at build time to the API's public FQDN, e.g.
 * https://hg-prod-api.<region>.azurecontainerapps.io — the API serves GET with
 * CORS AllowAnyOrigin, so an absolute cross-origin base works as-is.
 */
const API_BASE = import.meta.env.VITE_API_BASE ?? window.location.origin;

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export async function apiGet<T>(
  path: string,
  params?: Record<string, string | number | undefined>,
): Promise<T> {
  const url = new URL(path, API_BASE);
  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined) url.searchParams.set(key, String(value));
    }
  }
  const response = await fetch(url.toString(), {
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new ApiError(
      response.status,
      `Żądanie API nie powiodło się: ${response.status} ${response.statusText}`,
    );
  }
  return (await response.json()) as T;
}
