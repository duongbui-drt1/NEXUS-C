import type { CreateTenantRequest, PagedResult, TenantDto, UpdateTenantRequest } from '../../../types/api';
import { apiRequest } from '../client';

export interface TenantListQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  status?: string;
}

export const tenantsApi = {
  list: (query: TenantListQuery, signal?: AbortSignal) =>
    apiRequest<PagedResult<TenantDto>>('/api/v1/platform/tenants', {
      query: { ...query },
      signal,
    }),

  getById: (tenantId: string, signal?: AbortSignal) =>
    apiRequest<TenantDto>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId)}`, { signal }),

  listAccessible: (signal?: AbortSignal) =>
    apiRequest<TenantDto[]>('/api/v1/platform/tenants/accessible', { signal }),

  create: (body: CreateTenantRequest) =>
    apiRequest<TenantDto>('/api/v1/platform/tenants', { method: 'POST', body }),

  update: (tenantId: string, body: UpdateTenantRequest) =>
    apiRequest<TenantDto>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId)}`, {
      method: 'PUT',
      body,
    }),

  suspend: (tenantId: string, reason: string) =>
    apiRequest<TenantDto>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId)}/suspend`, {
      method: 'POST',
      body: { reason },
    }),

  activate: (tenantId: string) =>
    apiRequest<TenantDto>(`/api/v1/platform/tenants/${encodeURIComponent(tenantId)}/activate`, {
      method: 'POST',
    }),

  addMembership: (tenantId: string, userId: string) =>
    apiRequest<unknown>(
      `/api/v1/platform/tenants/${encodeURIComponent(tenantId)}/memberships/${encodeURIComponent(userId)}`,
      { method: 'POST' },
    ),

  revokeMembership: (tenantId: string, userId: string) =>
    apiRequest<void>(
      `/api/v1/platform/tenants/${encodeURIComponent(tenantId)}/memberships/${encodeURIComponent(userId)}`,
      { method: 'DELETE' },
    ),
};
