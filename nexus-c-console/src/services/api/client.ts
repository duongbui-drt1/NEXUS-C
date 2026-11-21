import type { ApiResponse, AuthTokenResponse, ProblemDetails } from '../../types/api';
import { tokenStore } from '../auth/tokenStore';
import { ApiClientError } from './errors';

const DEFAULT_TIMEOUT_MS = 20_000;

export function getApiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL;
  return configured ? configured.replace(/\/+$/, '') : '';
}

function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `corr-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
}

// Single-flight refresh lock so concurrent 401s NEVER trigger backend refresh token reuse detection
let inflightRefresh: Promise<boolean> | null = null;
let onUnauthorizedCallback: (() => void) | null = null;

export function registerUnauthorizedHandler(handler: () => void): void {
  onUnauthorizedCallback = handler;
}

async function tryRefreshSession(): Promise<boolean> {
  const refreshToken = tokenStore.getRefreshToken();
  if (!refreshToken) return false;

  if (!inflightRefresh) {
    inflightRefresh = (async () => {
      try {
        const res = await fetch(`${getApiBaseUrl()}/api/v1/auth/refresh`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-Correlation-Id': createCorrelationId(),
          },
          body: JSON.stringify({ refreshToken }),
        });

        if (!res.ok) {
          tokenStore.clear();
          onUnauthorizedCallback?.();
          return false;
        }

        const payload = (await res.json()) as ApiResponse<AuthTokenResponse>;
        tokenStore.setFromAuthResponse(payload.data);
        return true;
      } catch {
        tokenStore.clear();
        onUnauthorizedCallback?.();
        return false;
      } finally {
        inflightRefresh = null;
      }
    })();
  }

  return inflightRefresh;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
  timeoutMs?: number;
  skipAuth?: boolean;
  idempotencyKey?: string;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const base = getApiBaseUrl();
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  const url = `${base}${normalizedPath}`;

  if (!query) return url;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') {
      params.set(k, String(v));
    }
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

async function executeFetch(path: string, options: RequestOptions, isRetry: boolean): Promise<Response> {
  const headers: Record<string, string> = {
    'X-Correlation-Id': createCorrelationId(),
  };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (options.idempotencyKey) {
    headers['Idempotency-Key'] = options.idempotencyKey;
  }

  if (!options.skipAuth) {
    if (tokenStore.getSession() && tokenStore.isAccessTokenExpiringSoon() && !isRetry) {
      await tryRefreshSession();
    }
    const token = tokenStore.getAccessToken();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs ?? DEFAULT_TIMEOUT_MS);

  const onExternalAbort = () => controller.abort();
  options.signal?.addEventListener('abort', onExternalAbort, { once: true });

  try {
    const response = await fetch(buildUrl(path, options.query), {
      method: options.method ?? 'GET',
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: controller.signal,
    });

    if (response.status === 401 && !options.skipAuth && !isRetry && tokenStore.getRefreshToken()) {
      const refreshed = await tryRefreshSession();
      if (refreshed) {
        return executeFetch(path, options, true);
      }
    }

    return response;
  } finally {
    clearTimeout(timeout);
    options.signal?.removeEventListener('abort', onExternalAbort);
  }
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await executeFetch(path, options, false);

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  const isJson = contentType.includes('json');

  if (!response.ok) {
    if (response.status === 401 && !options.skipAuth) {
      tokenStore.clear();
      onUnauthorizedCallback?.();
    }

    const problem = isJson ? ((await response.json().catch(() => null)) as ProblemDetails | null) : null;
    throw ApiClientError.fromProblemDetails(response.status, problem);
  }

  if (!isJson) {
    return (await response.text()) as unknown as T;
  }

  const json = (await response.json()) as ApiResponse<T> | T;
  if (typeof json === 'object' && json !== null && 'success' in json && 'data' in json) {
    return (json as ApiResponse<T>).data;
  }
  return json as T;
}

export async function rawHealthCheck(path: '/health/live' | '/health/ready', signal?: AbortSignal): Promise<{ healthy: boolean; status: number; body: string }> {
  const response = await fetch(buildUrl(path), { signal });
  const body = await response.text();
  return { healthy: response.ok, status: response.status, body };
}
