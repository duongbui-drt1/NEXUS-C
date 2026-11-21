import type { AuthTokenResponse } from '../../types/api';

/**
 * In-memory authentication session store.
 * Security rule: NEVER persist access tokens or refresh tokens in localStorage or sessionStorage.
 */
interface SessionTokens {
  accessToken: string;
  accessTokenExpiresAt: number;
  refreshToken: string;
  refreshTokenExpiresAt: number;
  userId: string;
  email: string;
  tenantId: string | null;
  scope: string;
  sessionId: string;
}

let currentSession: SessionTokens | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((fn) => fn());
}

export const tokenStore = {
  getSession(): SessionTokens | null {
    return currentSession;
  },

  getAccessToken(): string | null {
    return currentSession?.accessToken ?? null;
  },

  getRefreshToken(): string | null {
    return currentSession?.refreshToken ?? null;
  },

  isAccessTokenExpiringSoon(bufferMs = 30_000): boolean {
    if (!currentSession) return true;
    return Date.now() + bufferMs >= currentSession.accessTokenExpiresAt;
  },

  setFromAuthResponse(resp: AuthTokenResponse): void {
    currentSession = {
      accessToken: resp.accessToken,
      accessTokenExpiresAt: Date.parse(resp.accessTokenExpiresAt),
      refreshToken: resp.refreshToken,
      refreshTokenExpiresAt: Date.parse(resp.refreshTokenExpiresAt),
      userId: resp.userId,
      email: resp.email,
      tenantId: resp.tenantId,
      scope: resp.scope,
      sessionId: resp.sessionId,
    };
    notify();
  },

  clear(): void {
    currentSession = null;
    notify();
  },

  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};
