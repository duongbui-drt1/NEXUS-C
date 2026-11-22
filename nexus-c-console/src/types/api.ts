export interface ApiMeta {
  correlationId: string;
}

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  meta: ApiMeta;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

// === Auth ===
export interface LoginRequest {
  email: string;
  password: string;
  tenantId?: string | null;
}

export interface AuthTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  userId: string;
  email: string;
  tenantId: string | null;
  scope: 'platform' | 'tenant' | string;
  sessionId: string;
}

export interface CurrentUserMe {
  userId: string;
  email: string;
  tenantId: string | null;
  sessionId: string;
  scope: string;
  isPlatformUser: boolean;
}

export interface EffectivePermissionsResponse {
  userId: string;
  tenantId: string | null;
  scope: 'platform' | 'tenant' | string;
  isPlatformUser: boolean;
  roles: string[];
  permissions: string[];
}

export interface SessionDto {
  id: string;
  userId: string;
  tenantId: string | null;
  ipAddress: string | null;
  userAgent: string | null;
  createdAt: string;
  lastSeenAt: string;
  expiresAt: string;
  isRevoked: boolean;
}

// === Tenancy ===
export interface TenantDto {
  id: string;
  code: string;
  name: string;
  slug: string;
  status: 'Active' | 'Suspended' | 'Archived' | string;
  timeZone: string;
  defaultCurrency: string;
  countryCode: string;
  suspendedReason: string | null;
  suspendedAt: string | null;
  createdAt: string;
  updatedAt: string;
  rowVersion: number;
}

export interface CreateTenantRequest {
  code: string;
  name: string;
  slug: string;
  timeZone: string;
  defaultCurrency: string;
  countryCode: string;
}

export interface UpdateTenantRequest {
  name: string;
  timeZone: string;
  defaultCurrency: string;
  countryCode: string;
  expectedRowVersion?: number;
}

// === Organization ===
export interface LegalEntityDto {
  id: string;
  tenantId: string;
  code: string;
  legalName: string;
  taxIdentificationNumber: string | null;
  address: string | null;
  status: string;
  createdAt: string;
}

export interface BranchDto {
  id: string;
  tenantId: string;
  legalEntityId: string;
  code: string;
  name: string;
  address: string | null;
  status: string;
  createdAt: string;
}

export interface OrganizationUnitDto {
  id: string;
  tenantId: string;
  parentId: string | null;
  code: string;
  name: string;
  unitType: string;
  createdAt: string;
}

// === Users & Access Control ===
export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
  isPlatformUser: boolean;
  emailConfirmed: boolean;
  isLockedOut: boolean;
  createdAt: string;
}

export interface CreateUserRequest {
  email: string;
  password: string;
  fullName: string;
  tenantId?: string | null;
  roleName?: string | null;
  isPlatformUser?: boolean;
}

export interface PermissionDto {
  id: string;
  code: string;
  name: string;
  scope: string;
  category: string;
}

export interface RoleDto {
  id: string;
  tenantId: string | null;
  name: string;
  description: string;
  scope: string;
  isSystemRole: boolean;
  permissions: string[];
}

export interface UserRoleDto {
  id: string;
  userId: string;
  roleId: string;
  roleName: string;
  tenantId: string | null;
  scope: string;
  resourceScopeType: string | null;
  resourceScopeId: string | null;
}

// === Audit ===
export interface AuditEventDto {
  id: string;
  actorUserId: string | null;
  actorEmail: string | null;
  tenantId: string | null;
  action: string;
  resourceType: string;
  resourceId: string | null;
  occurredAt: string;
  ipAddress: string | null;
  correlationId: string;
  result: string;
  reason: string | null;
  beforeMetadataJson: string | null;
  afterMetadataJson: string | null;
  isCrossTenantPlatformAccess: boolean;
}

// === Subscriptions ===
export interface PlanFeatureInput {
  featureCode: string;
  isEnabled: boolean;
  quotaLimit?: number | null;
}

export interface PlanDto {
  id: string;
  code: string;
  name: string;
  description: string;
  monthlyPrice: number;
  currency: string;
  isActive: boolean;
  features: PlanFeatureInput[];
  version?: number;
  archivedAt?: string | null;
  archiveReason?: string | null;
}

export interface PlanDeletionResultDto {
  planId: string;
  planCode: string;
  outcome: 'HardDeleted' | 'Archived' | string;
  referencedSubscriptionCount: number;
  archivedAt: string | null;
  message: string;
}

export interface EntitlementDto {
  featureCode: string;
  isEnabled: boolean;
  quotaLimit: number | null;
  expiresAt: string;
}

export interface TenantSubscriptionDto {
  id: string;
  tenantId: string;
  planId: string;
  status: string;
  startsAt: string;
  endsAt: string;
  entitlements: EntitlementDto[];
}

// === Configuration ===
export interface SettingDto {
  key: string;
  value: string;
  category: string;
  version: number;
  updatedAt: string;
}

export interface FeatureFlagDto {
  key: string;
  isEnabled: boolean;
  description: string | null;
  version: number;
  isTenantOverride: boolean;
}

// === Integrations ===
export interface ProviderDto {
  id: string;
  code: string;
  name: string;
  category: string;
  isActive: boolean;
  defaultMaxRetries: number;
  defaultTimeoutSeconds: number;
  version?: string;
  allowedHosts?: string | null;
  requiresSecretRef?: boolean;
}

export interface ConnectionDto {
  id: string;
  tenantId: string;
  providerId: string;
  name: string;
  credentialReference: string;
  status: string;
  maxRetries: number;
  timeoutSeconds: number;
  lastSyncedAt: string | null;
  applicationCode?: string | null;
  healthStatus?: string;
  lastHealthCheckAt?: string | null;
  disabledAt?: string | null;
  nonSecretConfigJson?: string | null;
}

export interface ConnectionTestResultDto {
  connectionId: string;
  healthStatus: string;
  passed: boolean;
  summary: string;
  checkedAt: string;
}

export interface SyncLogDto {
  id: string;
  connectionId: string;
  direction: string;
  status: string;
  recordsProcessed: number;
  summary: string | null;
  startedAt: string;
  completedAt: string | null;
}

export interface WebhookDto {
  id: string;
  tenantId: string;
  eventType: string;
  targetUrl: string;
  isActive: boolean;
  description?: string | null;
  secretVersion?: number;
  secretPrefix?: string | null;
  secretRotatedAt?: string | null;
}

export interface WebhookSecretRotationDto {
  webhook: WebhookDto;
  signingSecret: string;
}

export interface WebhookDeliveryDto {
  id: string;
  tenantId: string;
  webhookId: string;
  eventId: string;
  eventType: string;
  status: string;
  attemptCount: number;
  httpStatusCode: number | null;
  timestampUnix: number;
  signatureDigestPrefix: string | null;
  failureReason: string | null;
  attemptedAt: string;
  completedAt: string | null;
}

// === API Clients & Scoped API Keys ===
export interface ServiceClientDto {
  id: string;
  clientId: string;
  name: string;
  applicationCode: string | null;
  tenantId: string | null;
  secretPrefix: string;
  allowedScopes: string[];
  rateLimitPerMinute: number;
  isActive: boolean;
  createdAt: string;
  lastUsedAt: string | null;
  secretRotatedAt: string | null;
  revokedAt: string | null;
}

export interface ServiceClientWithSecretDto {
  client: ServiceClientDto;
  clientSecret: string;
}

export interface ApiKeyDto {
  id: string;
  name: string;
  tenantId: string | null;
  keyPrefix: string;
  allowedScopes: string[];
  expiresAt: string | null;
  isActive: boolean;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

export interface ApiKeyWithSecretDto {
  apiKey: ApiKeyDto;
  rawApiKey: string;
}

// === Operations (Outbox, Inbox, Dead Letters, Background Jobs) ===
export interface OutboxMessageDto {
  id: string;
  eventId: string;
  eventType: string;
  producer: string;
  tenantId: string | null;
  correlationId: string;
  causationId: string | null;
  schemaVersion: number;
  status: string;
  retryCount: number;
  occurredAt: string;
  createdAt: string;
  processedAt: string | null;
  nextAttemptAt: string;
  lastError: string | null;
}

export interface InboxMessageDto {
  id: string;
  eventId: string;
  consumerName: string;
  eventType: string;
  tenantId: string | null;
  processedAt: string;
}

export interface DeadLetterMessageDto {
  id: string;
  eventId: string;
  eventType: string;
  producer: string;
  tenantId: string | null;
  correlationId: string;
  causationId: string | null;
  reason: string;
  retryCount: number;
  manualRetryCount: number;
  maxManualRetries: number;
  lastRetriedByUserId: string | null;
  lastRetriedAt: string | null;
  retryHistoryJson: string | null;
  isResolved: boolean;
  failedAt: string;
}

export interface BackgroundJobStatusDto {
  jobName: string;
  status: string;
  lastRunStartedAt: string | null;
  lastRunCompletedAt: string | null;
  totalRuns: number;
  totalItemsProcessed: number;
  totalFailures: number;
  lastError: string | null;
}

// === Ecosystem Application Registry & Entitlements ===
export interface RegisteredApplicationDto {
  id: string;
  code: string;
  name: string;
  description: string;
  currentVersion: string;
  releaseChannel: string;
  status: string;
  minPlatformVersion: string;
  requiredCapabilities: string[];
  moduleDependencies: string[];
  compatibilityMetadataJson: string | null;
}

export interface TenantApplicationDto {
  applicationCode: string;
  applicationName: string;
  currentVersion: string;
  registryStatus: string;
  isEntitled: boolean;
  isActivated: boolean;
  entitlementReason: string | null;
  activatedAt: string | null;
  deactivatedAt: string | null;
}

export interface EntitlementEvaluationDto {
  tenantId: string;
  featureCode: string;
  isEntitled: boolean;
  quotaLimit: number | null;
  currentUsage: number;
  subscriptionStatus: string;
  featureFlagOverride: boolean | null;
  expiresAt: string | null;
  reason: string;
}

// === Notifications ===
export interface NotificationDto {
  id: string;
  tenantId: string;
  recipientUserId: string | null;
  recipientEmail: string | null;
  channel: string;
  subject: string;
  body: string;
  status: string;
  retryCount: number;
  isRead: boolean;
  createdAt: string;
  deliveredAt: string | null;
}

// === OpenAPI 3.x Spec (Minimal Typed Subset) ===
export interface OpenApiOperation {
  summary?: string;
  description?: string;
  tags?: string[];
  parameters?: Array<{
    name: string;
    in: string;
    required?: boolean;
    schema?: Record<string, unknown>;
  }>;
  requestBody?: {
    required?: boolean;
    content?: Record<string, { schema?: Record<string, unknown> }>;
  };
  responses?: Record<string, { description?: string }>;
  security?: Array<Record<string, string[]>>;
}

export interface OpenApiDocument {
  openapi?: string;
  info?: {
    title?: string;
    version?: string;
  };
  paths?: Record<string, Record<string, OpenApiOperation>>;
  components?: {
    schemas?: Record<string, unknown>;
  };
}

// === War-Room Final Foundation Types ===

export interface UserDetailDto {
  id: string;
  email: string;
  fullName: string;
  firstName: string | null;
  lastName: string | null;
  phoneNumber: string | null;
  jobTitle: string | null;
  countryCode: string | null;
  region: string | null;
  timeZone: string | null;
  locale: string | null;
  avatarUrl: string | null;
  adminNotes: string | null;
  isActive: boolean;
  isPlatformUser: boolean;
  emailConfirmed: boolean;
  isLockedOut: boolean;
  createdAt: string;
  lastSignInAt: string | null;
  clerkSubject: string | null;
  clerkSyncStatus: string | null;
  tenantMemberships: string[];
  activeSessionCount: number;
}

export interface UpdateUserProfileRequest {
  firstName?: string | null;
  lastName?: string | null;
  fullName?: string | null;
  phoneNumber?: string | null;
  jobTitle?: string | null;
  countryCode?: string | null;
  region?: string | null;
  timeZone?: string | null;
  locale?: string | null;
  avatarUrl?: string | null;
  adminNotes?: string | null;
}

export interface ClerkSyncStatusDto {
  appId: string;
  isEnabled: boolean;
  hasSecretKey: boolean;
  hasWebhookSecret: boolean;
  linkedIdentitiesCount: number;
  failedSyncCount: number;
  lastWebhookProcessedAt: string | null;
}

export interface RateLimitPolicyDto {
  id: string;
  name: string;
  routePattern: string;
  scopeType: 'AnonymousIp' | 'HumanSession' | 'ServiceClient' | 'ApiKey' | 'Expensive' | string;
  permitLimit: number;
  windowSeconds: number;
  burstLimit: number;
  isEnabled: boolean;
  priority: number;
  description: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface ApiRequestLogDto {
  id: string;
  timestamp: string;
  method: string;
  normalizedRoute: string;
  statusCode: number;
  durationMs: number;
  correlationId: string;
  applicationCode: string | null;
  serviceClientId: string | null;
  tenantId: string | null;
  userId: string | null;
  actorType: string;
  rateLimitDecision: string;
  rateLimitPolicy: string | null;
  errorCode: string | null;
}

export interface ApiRequestLogPage {
  items: ApiRequestLogDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface EmailProviderStatusDto {
  providerName: string;
  isApiKeyConfigured: boolean;
  isWebhookSecretConfigured: boolean;
  defaultFromAddress: string;
  defaultFromName: string;
  totalDeliveries: number;
  sentOrDeliveredCount: number;
  failedOrBouncedCount: number;
  activeSuppressionCount: number;
}

export interface EmailDeliveryDto {
  id: string;
  tenantId: string | null;
  provider: string;
  providerMessageId: string | null;
  templateKey: string | null;
  templateVersion: number;
  recipientMasked: string;
  subject: string;
  status: string;
  attemptCount: number;
  isTransientError: boolean;
  lastError: string | null;
  correlationId: string | null;
  createdAt: string;
  sentAt: string | null;
  deliveredAt: string | null;
}

export interface EmailSuppressionDto {
  id: string;
  recipientMasked: string;
  reason: string;
  provider: string;
  isActive: boolean;
  suppressedAt: string;
}

export interface AiCapabilityPolicyDto {
  id: string;
  tenantId: string;
  capabilityCode: string;
  isEnabled: boolean;
  killSwitchActive: boolean;
  requireHumanApproval: boolean;
  monthlyTokenBudget: number;
  allowedTools: string | null;
  promptTemplateVersion: string;
  dataClassification: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface ReferenceDataItemDto {
  id: string;
  category: string;
  code: string;
  label: string;
  isActive: boolean;
  sortOrder: number;
  localizedLabelsJson: string | null;
  metadataJson: string | null;
}

export interface CurrencyDto {
  id: string;
  code: string;
  name: string;
  symbol: string;
  minorUnitPrecision: number;
  isActive: boolean;
}

export interface FxRateDto {
  id: string;
  baseCurrency: string;
  quoteCurrency: string;
  rate: number;
  provider: string;
  observedAt: string;
  fetchedAt: string;
  staleAfter: string;
  isStale: boolean;
  sourceReference: string | null;
  disclaimer: string;
}

export interface FxRefreshResultDto {
  baseCurrency: string;
  provider: string;
  ratesUpdated: number;
  observedAt: string;
  fetchedAt: string;
  staleAfter: string;
  disclaimer: string;
}

export interface PlatformRealtimeEventDto {
  eventId: string;
  eventType: string;
  occurredAt: string;
  tenantId: string | null;
  requiredPermission: string;
  applicationCode: string | null;
  summary: string;
  metadata?: unknown;
}
