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
