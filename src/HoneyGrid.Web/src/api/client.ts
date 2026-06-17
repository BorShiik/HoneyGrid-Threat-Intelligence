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

/**
 * Base URL for the Functions app. Serverless SignalR negotiate AND the MCP/SDN
 * HTTP endpoints live on the Functions host, NOT on the Container API. Derived
 * from VITE_SIGNALR_URL's origin so no extra env var is needed. In dev/MSW
 * (no VITE_SIGNALR_URL) it stays the dev origin, so the mock worker keeps
 * intercepting /api/mcp/* and /api/sdn/*.
 */
const FUNC_BASE = (() => {
  const signalr = import.meta.env.VITE_SIGNALR_URL;
  if (!signalr) return window.location.origin;
  try {
    return new URL(signalr).origin;
  } catch {
    return window.location.origin;
  }
})();

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

/** Like apiGet, but targets the Functions app (MCP/SDN endpoints). */
export async function funcGet<T>(
  path: string,
  params?: Record<string, string | number | undefined>,
): Promise<T> {
  const url = new URL(path, FUNC_BASE);
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

/** Like apiPost, but targets the Functions app (SDN migration toggle). */
export async function funcPost<T>(path: string, body?: any): Promise<T> {
  const url = new URL(path, FUNC_BASE);
  const response = await fetch(url.toString(), {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    throw new ApiError(
      response.status,
      `Żądanie API nie powiodło się: ${response.status} ${response.statusText}`,
    );
  }
  return (await response.json()) as T;
}

export async function apiPost<T>(
  path: string,
  body?: any,
): Promise<T> {
  const url = new URL(path, window.location.origin);
  const response = await fetch(url.toString(), {
    method: 'POST',
    headers: {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    throw new ApiError(
      response.status,
      `Żądanie API nie powiodło się: ${response.status} ${response.statusText}`,
    );
  }
  return (await response.json()) as T;
}
