import type {
  ApiKeyDto,
  ApiKeyWithSecretDto,
  AuditEventDto,
  BackgroundJobStatusDto,
  ConnectionDto,
  DeadLetterMessageDto,
  EntitlementEvaluationDto,
  FeatureFlagDto,
  InboxMessageDto,
  NotificationDto,
  OpenApiDocument,
  OutboxMessageDto,
  PagedResult,
  PlanDto,
  PlanFeatureInput,
  ProviderDto,
  RegisteredApplicationDto,
  ServiceClientDto,
  ServiceClientWithSecretDto,
  SettingDto,
  SyncLogDto,
  TenantApplicationDto,
  TenantSubscriptionDto,
  WebhookDeliveryDto,
  WebhookDto,
  WebhookSecretRotationDto,
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

export const applicationsApi = {
  listRegistry: (signal?: AbortSignal) =>
    apiRequest<RegisteredApplicationDto[]>('/api/v1/platform/applications', { signal }),

  listTenantApplications: (signal?: AbortSignal) =>
    apiRequest<TenantApplicationDto[]>('/api/v1/platform/applications/tenant', { signal }),

  activateForTenant: (code: string, tenantId?: string) =>
    apiRequest<TenantApplicationDto>(`/api/v1/platform/applications/${encodeURIComponent(code)}/activate`, {
      method: 'POST',
      body: tenantId ? { tenantId } : {},
    }),

  deactivateForTenant: (code: string, tenantId?: string) =>
    apiRequest<TenantApplicationDto>(`/api/v1/platform/applications/${encodeURIComponent(code)}/deactivate`, {
      method: 'POST',
      body: tenantId ? { tenantId } : {},
    }),

  listEntitlements: (signal?: AbortSignal) =>
    apiRequest<EntitlementEvaluationDto[]>('/api/v1/platform/entitlements', { signal }),

  evaluateEntitlement: (featureCode: string, signal?: AbortSignal) =>
    apiRequest<EntitlementEvaluationDto>(`/api/v1/platform/entitlements/${encodeURIComponent(featureCode)}`, { signal }),
};

export const apiManagementApi = {
  listClients: (signal?: AbortSignal) =>
    apiRequest<ServiceClientDto[]>('/api/v1/platform/api-clients', { signal }),

  createClient: (body: {
    clientId: string;
    name: string;
    applicationCode?: string | null;
    tenantId?: string | null;
    allowedScopes: string[];
    rateLimitPerMinute?: number;
  }) => apiRequest<ServiceClientWithSecretDto>('/api/v1/platform/api-clients', { method: 'POST', body }),

  rotateClientSecret: (id: string) =>
    apiRequest<ServiceClientWithSecretDto>(`/api/v1/platform/api-clients/${encodeURIComponent(id)}/rotate-secret`, {
      method: 'POST',
    }),

  revokeClient: (id: string) =>
    apiRequest<void>(`/api/v1/platform/api-clients/${encodeURIComponent(id)}/revoke`, { method: 'POST' }),

  listKeys: (signal?: AbortSignal) =>
    apiRequest<ApiKeyDto[]>('/api/v1/platform/api-keys', { signal }),

  createKey: (body: { name: string; allowedScopes: string[]; expiresAt?: string | null; tenantId?: string | null }) =>
    apiRequest<ApiKeyWithSecretDto>('/api/v1/platform/api-keys', { method: 'POST', body }),

  rotateKey: (id: string) =>
    apiRequest<ApiKeyWithSecretDto>(`/api/v1/platform/api-keys/${encodeURIComponent(id)}/rotate`, {
      method: 'POST',
      body: {},
    }),

  revokeKey: (id: string) =>
    apiRequest<void>(`/api/v1/platform/api-keys/${encodeURIComponent(id)}`, { method: 'DELETE' }),
};

export const operationsApi = {
  listOutbox: (
    query: { page?: number; pageSize?: number; status?: string; eventType?: string; tenantId?: string },
    signal?: AbortSignal,
  ) => apiRequest<PagedResult<OutboxMessageDto>>('/api/v1/platform/operations/outbox', { query, signal }),

  listInbox: (
    query: { page?: number; pageSize?: number; consumerName?: string; eventType?: string; tenantId?: string },
    signal?: AbortSignal,
  ) => apiRequest<PagedResult<InboxMessageDto>>('/api/v1/platform/operations/inbox', { query, signal }),

  listDeadLetters: (
    query: { page?: number; pageSize?: number; resolved?: boolean; eventType?: string; tenantId?: string },
    signal?: AbortSignal,
  ) => apiRequest<PagedResult<DeadLetterMessageDto>>('/api/v1/platform/operations/dead-letters', { query, signal }),

  retryDeadLetter: (id: string, note?: string) =>
    apiRequest<DeadLetterMessageDto>(`/api/v1/platform/operations/dead-letters/${encodeURIComponent(id)}/retry`, {
      method: 'POST',
      body: { note },
    }),

  listJobs: (signal?: AbortSignal) =>
    apiRequest<BackgroundJobStatusDto[]>('/api/v1/platform/operations/jobs', { signal }),
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

  listWebhooks: (signal?: AbortSignal) =>
    apiRequest<WebhookDto[]>('/api/v1/platform/integrations/webhooks', { signal }),

  registerWebhook: (body: { eventType: string; targetUrl: string; signingSecretRef: string; description?: string }) =>
    apiRequest<WebhookDto>('/api/v1/platform/integrations/webhooks', { method: 'POST', body }),

  disableWebhook: (id: string) =>
    apiRequest<WebhookDto>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(id)}/disable`, { method: 'POST' }),

  enableWebhook: (id: string) =>
    apiRequest<WebhookDto>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(id)}/enable`, { method: 'POST' }),

  deleteWebhook: (id: string) =>
    apiRequest<void>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  rotateWebhookSecret: (id: string) =>
    apiRequest<WebhookSecretRotationDto>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(id)}/rotate-secret`, {
      method: 'POST',
    }),

  listWebhookDeliveries: (webhookId: string, signal?: AbortSignal) =>
    apiRequest<WebhookDeliveryDto[]>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(webhookId)}/deliveries`, { signal }),

  retryWebhookDelivery: (deliveryId: string) =>
    apiRequest<WebhookDeliveryDto>(`/api/v1/platform/integrations/webhooks/deliveries/${encodeURIComponent(deliveryId)}/retry`, {
      method: 'POST',
    }),
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
