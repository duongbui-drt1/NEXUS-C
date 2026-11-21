import type { AuthTokenResponse, CurrentUserMe, LoginRequest, SessionDto } from '../../../types/api';
import { apiRequest } from '../client';

export const authApi = {
  login: (body: LoginRequest) =>
    apiRequest<AuthTokenResponse>('/api/v1/auth/login', {
      method: 'POST',
      body,
      skipAuth: true,
    }),

  logout: () => apiRequest<void>('/api/v1/auth/logout', { method: 'POST' }),

  me: (signal?: AbortSignal) => apiRequest<CurrentUserMe>('/api/v1/auth/me', { signal }),

  switchTenant: (tenantId: string) =>
    apiRequest<AuthTokenResponse>('/api/v1/auth/switch-tenant', {
      method: 'POST',
      body: { tenantId },
    }),

  changePassword: (currentPassword: string, newPassword: string) =>
    apiRequest<void>('/api/v1/auth/change-password', {
      method: 'POST',
      body: { currentPassword, newPassword },
    }),

  listSessions: (signal?: AbortSignal) => apiRequest<SessionDto[]>('/api/v1/auth/sessions', { signal }),

  revokeSession: (sessionId: string) =>
    apiRequest<void>(`/api/v1/auth/sessions/${encodeURIComponent(sessionId)}`, { method: 'DELETE' }),
};
