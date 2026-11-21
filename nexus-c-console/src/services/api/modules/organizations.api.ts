import type { BranchDto, LegalEntityDto, OrganizationUnitDto, PagedResult } from '../../../types/api';
import { apiRequest } from '../client';

export const organizationsApi = {
  listLegalEntities: (query: { page?: number; pageSize?: number; search?: string }, signal?: AbortSignal) =>
    apiRequest<PagedResult<LegalEntityDto>>('/api/v1/platform/organizations/legal-entities', {
      query,
      signal,
    }),

  createLegalEntity: (body: { code: string; legalName: string; taxIdentificationNumber?: string; address?: string }) =>
    apiRequest<LegalEntityDto>('/api/v1/platform/organizations/legal-entities', { method: 'POST', body }),

  deleteLegalEntity: (id: string) =>
    apiRequest<void>(`/api/v1/platform/organizations/legal-entities/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  listBranches: (query: { page?: number; pageSize?: number; legalEntityId?: string }, signal?: AbortSignal) =>
    apiRequest<PagedResult<BranchDto>>('/api/v1/platform/organizations/branches', { query, signal }),

  createBranch: (body: { legalEntityId: string; code: string; name: string; address?: string }) =>
    apiRequest<BranchDto>('/api/v1/platform/organizations/branches', { method: 'POST', body }),

  deleteBranch: (id: string) =>
    apiRequest<void>(`/api/v1/platform/organizations/branches/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  listUnits: (signal?: AbortSignal) =>
    apiRequest<OrganizationUnitDto[]>('/api/v1/platform/organizations/units', { signal }),

  createUnit: (body: { parentId?: string | null; code: string; name: string; unitType: number }) =>
    apiRequest<OrganizationUnitDto>('/api/v1/platform/organizations/units', { method: 'POST', body }),
};
