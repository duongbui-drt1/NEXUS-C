import type {
  AiCapabilityPolicyDto,
  ApiKeyDto,
  ApiKeyWithSecretDto,
  ApiRequestLogPage,
  AuditEventDto,
  BackgroundJobStatusDto,
  ConnectionDto,
  ConnectionTestResultDto,
  CurrencyDto,
  DeadLetterMessageDto,
  EmailDeliveryDto,
  EmailProviderStatusDto,
  EmailSuppressionDto,
  EntitlementEvaluationDto,
  FeatureFlagDto,
  FxRateDto,
  FxRefreshResultDto,
  InboxMessageDto,
  NotificationDto,
  OpenApiDocument,
  OutboxMessageDto,
  PagedResult,
  PlanDeletionResultDto,
  PlanDto,
  PlanFeatureInput,
  ProviderDto,
  RateLimitPolicyDto,
  ReferenceDataItemDto,
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

  deleteOrArchivePlan: (planId: string, reason?: string) =>
    apiRequest<PlanDeletionResultDto>(`/api/v1/platform/subscriptions/plans/${encodeURIComponent(planId)}`, {
      method: 'DELETE',
      query: reason ? { reason } : undefined,
    }),

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

  listRateLimits: (signal?: AbortSignal) =>
    apiRequest<RateLimitPolicyDto[]>('/api/v1/platform/rate-limits', { signal }),

  createRateLimit: (body: {
    name: string;
    routePattern: string;
    scopeType: string;
    permitLimit: number;
    windowSeconds: number;
    burstLimit?: number;
    isEnabled?: boolean;
    priority?: number;
    description?: string;
  }) => apiRequest<RateLimitPolicyDto>('/api/v1/platform/rate-limits', { method: 'POST', body }),

  updateRateLimit: (
    id: string,
    body: {
      routePattern: string;
      scopeType: string;
      permitLimit: number;
      windowSeconds: number;
      burstLimit: number;
      isEnabled: boolean;
      priority: number;
      description?: string | null;
    },
  ) => apiRequest<RateLimitPolicyDto>(`/api/v1/platform/rate-limits/${encodeURIComponent(id)}`, { method: 'PUT', body }),

  deleteRateLimit: (id: string) =>
    apiRequest<void>(`/api/v1/platform/rate-limits/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  queryApiLogs: (
    query: {
      route?: string;
      statusCode?: number;
      correlationId?: string;
      rateLimitDecision?: string;
      page?: number;
      pageSize?: number;
    },
    signal?: AbortSignal,
  ) => apiRequest<ApiRequestLogPage>('/api/v1/platform/api-logs', { query, signal }),
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

  createConnection: (body: { providerId: string; name: string; credentialReference: string; maxRetries: number; timeoutSeconds: number; applicationCode?: string }) =>
    apiRequest<ConnectionDto>('/api/v1/platform/integrations/connections', { method: 'POST', body }),

  testConnection: (id: string) =>
    apiRequest<ConnectionTestResultDto>(`/api/v1/platform/integrations/connections/${encodeURIComponent(id)}/test`, { method: 'POST' }),

  disableConnection: (id: string) =>
    apiRequest<ConnectionDto>(`/api/v1/platform/integrations/connections/${encodeURIComponent(id)}/disable`, { method: 'POST' }),

  reconnectConnection: (id: string) =>
    apiRequest<ConnectionDto>(`/api/v1/platform/integrations/connections/${encodeURIComponent(id)}/reconnect`, { method: 'POST' }),

  deleteConnection: (id: string) =>
    apiRequest<void>(`/api/v1/platform/integrations/connections/${encodeURIComponent(id)}`, { method: 'DELETE' }),

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

  dispatchWebhook: (webhookId: string, body: { payloadJson: string; maxAttempts?: number }) =>
    apiRequest<WebhookDeliveryDto>(`/api/v1/platform/integrations/webhooks/${encodeURIComponent(webhookId)}/dispatch`, {
      method: 'POST',
      body,
    }),

  listAiPolicies: (signal?: AbortSignal) =>
    apiRequest<AiCapabilityPolicyDto[]>('/api/v1/platform/integrations/ai/policies', { signal }),

  upsertAiPolicy: (body: {
    capabilityCode: string;
    isEnabled: boolean;
    killSwitchActive: boolean;
    requireHumanApproval: boolean;
    monthlyTokenBudget: number;
    allowedTools?: string | null;
    promptTemplateVersion?: string;
    dataClassification?: string;
  }) => apiRequest<AiCapabilityPolicyDto>('/api/v1/platform/integrations/ai/policies', { method: 'PUT', body }),
};

export const emailApi = {
  getStatus: (signal?: AbortSignal) =>
    apiRequest<EmailProviderStatusDto>('/api/v1/platform/email/status', { signal }),

  listDeliveries: (query: { page?: number; pageSize?: number; status?: string }, signal?: AbortSignal) =>
    apiRequest<PagedResult<EmailDeliveryDto>>('/api/v1/platform/email/deliveries', { query, signal }),

  listSuppressions: (signal?: AbortSignal) =>
    apiRequest<EmailSuppressionDto[]>('/api/v1/platform/email/suppressions', { signal }),

  send: (body: { to: string; subject: string; body: string; isHtml?: boolean; idempotencyKey?: string }) =>
    apiRequest<EmailDeliveryDto>('/api/v1/platform/email/send', { method: 'POST', body }),
};

export const referenceDataApi = {
  listByCategory: (category: string, search?: string, signal?: AbortSignal) =>
    apiRequest<ReferenceDataItemDto[]>(`/api/v1/platform/reference-data/${encodeURIComponent(category)}`, {
      query: search ? { search } : undefined,
      signal,
    }),

  listCurrencies: (search?: string, signal?: AbortSignal) =>
    apiRequest<CurrencyDto[]>('/api/v1/platform/currencies', {
      query: search ? { search } : undefined,
      signal,
    }),

  getFxRates: (baseCurrency = 'USD', signal?: AbortSignal) =>
    apiRequest<FxRateDto[]>('/api/v1/platform/fx/rates', {
      query: { baseCurrency },
      signal,
    }),

  refreshFxRates: (baseCurrency = 'USD') =>
    apiRequest<FxRefreshResultDto>('/api/v1/platform/fx/refresh', {
      method: 'POST',
      query: { baseCurrency },
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
