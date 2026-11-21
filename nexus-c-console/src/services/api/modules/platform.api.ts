import type {
  AuditEventDto,
  ConnectionDto,
  FeatureFlagDto,
  NotificationDto,
  OpenApiDocument,
  PagedResult,
  PlanDto,
  PlanFeatureInput,
  ProviderDto,
  SettingDto,
  SyncLogDto,
  TenantSubscriptionDto,
  WebhookDto,
} from '../../../types/api';
import { apiRequest, rawHealthCheck } from '../client';

export const auditApi = {
  query: (
    query: { page?: number; pageSize?: number; action?: string; resourceType?: string; tenantId?: string },
    signal?: AbortSignal,
  ) => apiRequest<PagedResult<AuditEventDto>>('/api/v1/platform/audit', { query, signal }),
};

export const subscriptionsApi = {
  listPlans: (signal?: AbortSignal) => apiRequest<PlanDto[]>('/api/v1/platform/subscriptions/plans', { signal }),

  createPlan: (body: {
    code: string;
    name: string;
    description: string;
    monthlyPrice: number;
    currency: string;
    features: PlanFeatureInput[];
  }) => apiRequest<PlanDto>('/api/v1/platform/subscriptions/plans', { method: 'POST', body }),

  assignPlan: (body: { tenantId: string; planId: string; startsAt: string; endsAt: string; seatCount: number }) =>
    apiRequest<TenantSubscriptionDto>('/api/v1/platform/subscriptions/assign', { method: 'POST', body }),

  getCurrent: (signal?: AbortSignal) =>
    apiRequest<TenantSubscriptionDto>('/api/v1/platform/subscriptions/current', { signal }),

  checkEntitlement: (featureCode: string, signal?: AbortSignal) =>
    apiRequest<boolean>(`/api/v1/platform/subscriptions/entitlements/${encodeURIComponent(featureCode)}`, { signal }),
};

export const configurationApi = {
  listGlobal: (signal?: AbortSignal) =>
    apiRequest<SettingDto[]>('/api/v1/platform/configuration/global', { signal }),

  upsertGlobal: (body: { key: string; value: string; category: string }) =>
    apiRequest<SettingDto>('/api/v1/platform/configuration/global', { method: 'PUT', body }),

  listTenant: (category?: string, signal?: AbortSignal) =>
    apiRequest<SettingDto[]>('/api/v1/platform/configuration/tenant', {
      query: category ? { category } : undefined,
      signal,
    }),

  upsertTenant: (body: { key: string; value: string; category: string }) =>
    apiRequest<SettingDto>('/api/v1/platform/configuration/tenant', { method: 'PUT', body }),

  listFlags: (signal?: AbortSignal) =>
    apiRequest<FeatureFlagDto[]>('/api/v1/platform/configuration/feature-flags', { signal }),

  upsertGlobalFlag: (body: { key: string; isEnabled: boolean; description?: string }) =>
    apiRequest<FeatureFlagDto>('/api/v1/platform/configuration/feature-flags/global', { method: 'PUT', body }),

  upsertTenantFlag: (body: { key: string; isEnabled: boolean; description?: string }) =>
    apiRequest<FeatureFlagDto>('/api/v1/platform/configuration/feature-flags', { method: 'PUT', body }),
};

export const integrationsApi = {
  listProviders: (signal?: AbortSignal) =>
    apiRequest<ProviderDto[]>('/api/v1/platform/integrations/providers', { signal }),

  registerProvider: (body: { code: string; name: string; category: string; maxRetries: number; timeoutSeconds: number }) =>
    apiRequest<ProviderDto>('/api/v1/platform/integrations/providers', { method: 'POST', body }),

  listConnections: (signal?: AbortSignal) =>
    apiRequest<ConnectionDto[]>('/api/v1/platform/integrations/connections', { signal }),

  createConnection: (body: { providerId: string; name: string; credentialReference: string; maxRetries: number; timeoutSeconds: number }) =>
    apiRequest<ConnectionDto>('/api/v1/platform/integrations/connections', { method: 'POST', body }),

  listSyncLogs: (connectionId: string, signal?: AbortSignal) =>
    apiRequest<SyncLogDto[]>(`/api/v1/platform/integrations/connections/${encodeURIComponent(connectionId)}/sync-logs`, { signal }),

  registerWebhook: (body: { eventType: string; targetUrl: string; signingSecretRef: string }) =>
    apiRequest<WebhookDto>('/api/v1/platform/integrations/webhooks', { method: 'POST', body }),
};

export const notificationsApi = {
  listMine: (query: { page?: number; pageSize?: number }, signal?: AbortSignal) =>
    apiRequest<PagedResult<NotificationDto>>('/api/v1/platform/notifications', { query, signal }),

  markRead: (id: string) =>
    apiRequest<void>(`/api/v1/platform/notifications/${encodeURIComponent(id)}/read`, { method: 'POST' }),
};

export const systemApi = {
  getHealth: async (signal?: AbortSignal) => {
    const [live, ready] = await Promise.all([
      rawHealthCheck('/health/live', signal).catch(() => ({ healthy: false, status: 0, body: 'Unreachable' })),
      rawHealthCheck('/health/ready', signal).catch(() => ({ healthy: false, status: 0, body: 'Unreachable' })),
    ]);
    return { live, ready, checkedAt: new Date().toISOString() };
  },

  getOpenApiSpec: (signal?: AbortSignal) =>
    apiRequest<OpenApiDocument>('/openapi/v1.json', { skipAuth: true, signal }),
};
