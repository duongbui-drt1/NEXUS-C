import { describe, expect, it, vi, beforeEach } from 'vitest';
import { apiRequest, registerUnauthorizedHandler } from '../src/services/api/client';
import { ApiClientError } from '../src/services/api/errors';
import { tokenStore } from '../src/services/auth/tokenStore';

function mockJsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('Token Store & API Client Security', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    tokenStore.clear();
    window.localStorage.clear();
  });

  it('stores tokens strictly in memory and never persists them in localStorage', () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'jwt-access-secret',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'opaque-refresh-secret',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-1',
      email: 'admin@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 's-1',
    });

    expect(tokenStore.getAccessToken()).toBe('jwt-access-secret');
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it('parses RFC 9457 ProblemDetails into ApiClientError with errorCode and correlationId', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockJsonResponse(
        {
          title: 'Forbidden',
          status: 403,
          detail: 'Tenant is suspended',
          errorCode: 'TENANT_SUSPENDED',
          correlationId: 'corr-123',
        },
        403,
      ),
    );

    await expect(apiRequest('/api/v1/platform/tenants', { skipAuth: true })).rejects.toMatchObject({
      name: 'ApiClientError',
      status: 403,
      errorCode: 'TENANT_SUSPENDED',
      correlationId: 'corr-123',
      message: 'Tenant is suspended',
    });
  });

  it('deduplicates concurrent 401 refresh calls via single-flight lock (prevents reuse detection)', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'expired-token',
      accessTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(),
      refreshToken: 'refresh-v1',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-1',
      email: 'admin@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 's-1',
    });

    let refreshCallCount = 0;

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/refresh')) {
        refreshCallCount++;
        await new Promise((r) => setTimeout(r, 15));
        return mockJsonResponse({
          success: true,
          data: {
            accessToken: 'fresh-token-v2',
            accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
            refreshToken: 'refresh-v2',
            refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
            userId: 'u-1',
            email: 'admin@nexus.local',
            tenantId: null,
            scope: 'platform',
            sessionId: 's-1',
          },
          meta: { correlationId: 'c-ref' },
        });
      }

      const authHeader = (init?.headers as Record<string, string>)?.Authorization;
      if (authHeader === 'Bearer expired-token') {
        return mockJsonResponse({ status: 401, errorCode: 'AUTH_EXPIRED', detail: 'Expired' }, 401);
      }

      return mockJsonResponse({ success: true, data: { ok: true }, meta: { correlationId: 'c-ok' } });
    });

    const [r1, r2] = await Promise.all([
      apiRequest<{ ok: boolean }>('/api/v1/auth/me'),
      apiRequest<{ ok: boolean }>('/api/v1/platform/roles'),
    ]);

    expect(r1.ok).toBe(true);
    expect(r2.ok).toBe(true);
    expect(refreshCallCount).toBe(1);
    expect(tokenStore.getRefreshToken()).toBe('refresh-v2');
  });

  it('clears session and invokes unauthorized handler when refresh token fails', async () => {
    const onUnauth = vi.fn();
    registerUnauthorizedHandler(onUnauth);

    tokenStore.setFromAuthResponse({
      accessToken: 'old-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(),
      refreshToken: 'revoked-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-1',
      email: 'admin@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 's-1',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/refresh')) {
        return mockJsonResponse({ status: 401, errorCode: 'AUTH_REFRESH_TOKEN_REUSE', detail: 'Revoked' }, 401);
      }
      return mockJsonResponse({ status: 401, errorCode: 'AUTH_EXPIRED', detail: 'Expired' }, 401);
    });

    await expect(apiRequest('/api/v1/auth/me')).rejects.toBeInstanceOf(ApiClientError);
    expect(tokenStore.getSession()).toBeNull();
    expect(onUnauth).toHaveBeenCalled();
  });
});
