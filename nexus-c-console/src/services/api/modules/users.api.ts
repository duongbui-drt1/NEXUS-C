import type {
  ClerkSyncStatusDto,
  CreateUserRequest,
  PagedResult,
  PermissionDto,
  RoleDto,
  UpdateUserProfileRequest,
  UserDetailDto,
  UserDto,
  UserRoleDto,
} from '../../../types/api';
import { apiRequest } from '../client';

export const usersApi = {
  list: (query: { page?: number; pageSize?: number; search?: string }, signal?: AbortSignal) =>
    apiRequest<PagedResult<UserDto>>('/api/v1/platform/users', { query, signal }),

  getDetail: (userId: string, signal?: AbortSignal) =>
    apiRequest<UserDetailDto>(`/api/v1/platform/users/${encodeURIComponent(userId)}`, { signal }),

  updateProfile: (userId: string, body: UpdateUserProfileRequest) =>
    apiRequest<UserDetailDto>(`/api/v1/platform/users/${encodeURIComponent(userId)}/profile`, {
      method: 'PUT',
      body,
    }),

  getClerkSyncStatus: (signal?: AbortSignal) =>
    apiRequest<ClerkSyncStatusDto>('/api/v1/platform/users/clerk/sync-status', { signal }),

  linkClerkSubject: (userId: string, clerkSubject: string) =>
    apiRequest<void>('/api/v1/platform/users/clerk/link', {
      method: 'POST',
      body: { userId, clerkSubject },
    }),

  create: (body: CreateUserRequest) =>
    apiRequest<UserDto>('/api/v1/platform/users', { method: 'POST', body }),

  invite: (body: { email: string; fullName: string; roleName?: string }) =>
    apiRequest<UserDto>('/api/v1/platform/users/invite', { method: 'POST', body }),

  disable: (userId: string, reason: string) =>
    apiRequest<void>(`/api/v1/platform/users/${encodeURIComponent(userId)}/disable`, {
      method: 'POST',
      body: { reason },
    }),

  enable: (userId: string) =>
    apiRequest<void>(`/api/v1/platform/users/${encodeURIComponent(userId)}/enable`, {
      method: 'POST',
    }),

  revokeSession: (userId: string, sessionId: string) =>
    apiRequest<void>(
      `/api/v1/platform/users/${encodeURIComponent(userId)}/sessions/${encodeURIComponent(sessionId)}`,
      { method: 'DELETE' },
    ),
};

export const accessControlApi = {
  listPermissions: (signal?: AbortSignal) =>
    apiRequest<PermissionDto[]>('/api/v1/platform/roles/permissions', { signal }),

  listRoles: (signal?: AbortSignal) =>
    apiRequest<RoleDto[]>('/api/v1/platform/roles', { signal }),

  createRole: (body: { name: string; description: string; permissionCodes: string[] }) =>
    apiRequest<RoleDto>('/api/v1/platform/roles', { method: 'POST', body }),

  assignRole: (body: { userId: string; roleId: string; resourceScopeType?: string; resourceScopeId?: string }) =>
    apiRequest<UserRoleDto>('/api/v1/platform/roles/assign', { method: 'POST', body }),

  revokeRole: (userRoleId: string) =>
    apiRequest<void>(`/api/v1/platform/roles/assignments/${encodeURIComponent(userRoleId)}`, { method: 'DELETE' }),

  listUserRoles: (userId: string, signal?: AbortSignal) =>
    apiRequest<UserRoleDto[]>(`/api/v1/platform/roles/users/${encodeURIComponent(userId)}`, { signal }),
};
