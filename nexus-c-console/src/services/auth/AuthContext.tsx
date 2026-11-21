import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { AuthTokenResponse, LoginRequest, TenantDto } from '../../types/api';
import { registerUnauthorizedHandler } from '../api/client';
import { authApi } from '../api/modules/auth.api';
import { tenantsApi } from '../api/modules/tenants.api';
import { accessControlApi } from '../api/modules/users.api';
import { tokenStore } from './tokenStore';

export interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  userId: string | null;
  email: string | null;
  tenantId: string | null;
  scope: string | null;
  sessionId: string | null;
  isPlatformUser: boolean;
  permissions: string[];
  roles: string[];
  accessibleTenants: TenantDto[];
  login: (req: LoginRequest) => Promise<AuthTokenResponse>;
  logout: () => Promise<void>;
  switchTenant: (tenantId: string) => Promise<void>;
  hasPermission: (permission: string) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState(() => tokenStore.getSession());
  const [isLoading, setIsLoading] = useState(false);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [roles, setRoles] = useState<string[]>([]);
  const [accessibleTenants, setAccessibleTenants] = useState<TenantDto[]>([]);

  const resetAuthContext = useCallback(() => {
    setSession(null);
    setPermissions([]);
    setRoles([]);
    setAccessibleTenants([]);
    queryClient.clear();
  }, [queryClient]);

  useEffect(() => {
    registerUnauthorizedHandler(() => {
      resetAuthContext();
    });
    return tokenStore.subscribe(() => {
      setSession(tokenStore.getSession());
    });
  }, [resetAuthContext]);

  const hydrateUserMetadata = useCallback(async (userId: string) => {
    // Load accessible tenants & user roles for UX navigation gating (backend remains sole authority!)
    const [tenantsRes, userRolesRes, allRolesRes] = await Promise.allSettled([
      tenantsApi.listAccessible(),
      accessControlApi.listUserRoles(userId),
      accessControlApi.listRoles(),
    ]);

    if (tenantsRes.status === 'fulfilled') {
      setAccessibleTenants(tenantsRes.value);
    } else {
      setAccessibleTenants([]);
    }

    if (userRolesRes.status === 'fulfilled') {
      const assignedRoleIds = new Set(userRolesRes.value.map((r) => r.roleId));
      setRoles(userRolesRes.value.map((r) => r.roleName));

      if (allRolesRes.status === 'fulfilled') {
        const permSet = new Set<string>();
        for (const role of allRolesRes.value) {
          if (assignedRoleIds.has(role.id)) {
            for (const p of role.permissions) permSet.add(p);
          }
        }
        setPermissions(Array.from(permSet));
      } else {
        setPermissions([]);
      }
    } else {
      setRoles([]);
      setPermissions([]);
    }
  }, []);

  const login = useCallback(
    async (req: LoginRequest): Promise<AuthTokenResponse> => {
      setIsLoading(true);
      try {
        const resp = await authApi.login(req);
        tokenStore.setFromAuthResponse(resp);
        setSession(tokenStore.getSession());
        await hydrateUserMetadata(resp.userId);
        return resp;
      } finally {
        setIsLoading(false);
      }
    },
    [hydrateUserMetadata],
  );

  const logout = useCallback(async () => {
    try {
      if (tokenStore.getAccessToken()) {
        await authApi.logout();
      }
    } catch {
      // Clear local memory session regardless of network failure
    } finally {
      tokenStore.clear();
      resetAuthContext();
    }
  }, [resetAuthContext]);

  const switchTenant = useCallback(
    async (tenantId: string) => {
      setIsLoading(true);
      try {
        const resp = await authApi.switchTenant(tenantId);
        tokenStore.setFromAuthResponse(resp);
        setSession(tokenStore.getSession());
        queryClient.clear();
        await hydrateUserMetadata(resp.userId);
      } finally {
        setIsLoading(false);
      }
    },
    [hydrateUserMetadata, queryClient],
  );

  const hasPermission = useCallback(
    (permission: string): boolean => {
      if (!session) return false;
      // If permissions were resolved from backend role catalog, enforce them strictly for UX gating
      if (permissions.length > 0) {
        return permissions.includes(permission);
      }
      // Fallback when user doesn't hold platform.roles.read: gate platform-only routes by scope; backend enforces actual 403
      if (permission.startsWith('platform.tenants.') || permission === 'platform.subscriptions.manage') {
        return session.scope === 'platform';
      }
      return true;
    },
    [permissions, session],
  );

  const value = useMemo<AuthState>(
    () => ({
      isAuthenticated: session !== null,
      isLoading,
      userId: session?.userId ?? null,
      email: session?.email ?? null,
      tenantId: session?.tenantId ?? null,
      scope: session?.scope ?? null,
      sessionId: session?.sessionId ?? null,
      isPlatformUser: session?.scope === 'platform',
      permissions,
      roles,
      accessibleTenants,
      login,
      logout,
      switchTenant,
      hasPermission,
    }),
    [session, isLoading, permissions, roles, accessibleTenants, login, logout, switchTenant, hasPermission],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
