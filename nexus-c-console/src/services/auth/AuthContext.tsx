import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { AuthTokenResponse, LoginRequest, TenantDto } from '../../types/api';
import { registerUnauthorizedHandler } from '../api/client';
import { authApi } from '../api/modules/auth.api';
import { tenantsApi } from '../api/modules/tenants.api';
import { tokenStore } from './tokenStore';

export type PermissionStatus = 'idle' | 'loading' | 'loaded' | 'failed';

export interface AuthState {
  isAuthenticated: boolean;
  isLoading: boolean;
  permissionStatus: PermissionStatus;
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
  switchTenant: (tenantId: string | null) => Promise<void>;
  reloadPermissions: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

async function fetchMetadataSnapshot(): Promise<{
  tenants: TenantDto[];
  roles: string[];
  permissions: string[];
  isPlatformUser: boolean;
  status: 'loaded' | 'failed';
}> {
  const [permRes, tenantsRes] = await Promise.allSettled([
    authApi.effectivePermissions(),
    tenantsApi.listAccessible(),
  ]);

  const tenants =
    tenantsRes.status === 'fulfilled' && Array.isArray(tenantsRes.value) ? tenantsRes.value : [];

  if (permRes.status === 'fulfilled') {
    return {
      tenants,
      roles: Array.isArray(permRes.value.roles) ? permRes.value.roles : [],
      permissions: Array.isArray(permRes.value.permissions) ? permRes.value.permissions : [],
      isPlatformUser: Boolean(permRes.value.isPlatformUser),
      status: 'loaded',
    };
  }

  // STRICT FAIL-CLOSED: failed permission fetch grants ZERO permissions
  return {
    tenants,
    roles: [],
    permissions: [],
    isPlatformUser: false,
    status: 'failed',
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState(() => tokenStore.getSession());
  const [isLoading, setIsLoading] = useState(false);
  const [permissionStatus, setPermissionStatus] = useState<PermissionStatus>(() =>
    tokenStore.getSession() ? 'loading' : 'idle',
  );
  const [permissions, setPermissions] = useState<string[]>([]);
  const [roles, setRoles] = useState<string[]>([]);
  const [serverIsPlatformUser, setServerIsPlatformUser] = useState(false);
  const [accessibleTenants, setAccessibleTenants] = useState<TenantDto[]>([]);

  const resetAuthContext = useCallback(() => {
    setSession(null);
    setPermissionStatus('idle');
    setPermissions([]);
    setRoles([]);
    setServerIsPlatformUser(false);
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

  const hydrateUserMetadata = useCallback(async () => {
    setPermissionStatus('loading');
    const snap = await fetchMetadataSnapshot();
    setAccessibleTenants(snap.tenants);
    setRoles(snap.roles);
    setPermissions(snap.permissions);
    setServerIsPlatformUser(snap.isPlatformUser);
    setPermissionStatus(snap.status);
  }, []);

  // Hydrate permissions asynchronously if an in-memory session already existed on mount
  useEffect(() => {
    if (!session || permissionStatus !== 'loading') return;
    let cancelled = false;
    void fetchMetadataSnapshot().then((snap) => {
      if (cancelled) return;
      setAccessibleTenants(snap.tenants);
      setRoles(snap.roles);
      setPermissions(snap.permissions);
      setServerIsPlatformUser(snap.isPlatformUser);
      setPermissionStatus(snap.status);
    });
    return () => {
      cancelled = true;
    };
  }, [session, permissionStatus]);

  const login = useCallback(
    async (req: LoginRequest): Promise<AuthTokenResponse> => {
      setIsLoading(true);
      try {
        const resp = await authApi.login(req);
        tokenStore.setFromAuthResponse(resp);
        setSession(tokenStore.getSession());
        await hydrateUserMetadata();
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
    async (tenantId: string | null) => {
      setIsLoading(true);
      try {
        const resp = await authApi.switchTenant(tenantId || null);
        tokenStore.setFromAuthResponse(resp);
        setSession(tokenStore.getSession());
        queryClient.clear();
        await hydrateUserMetadata();
      } finally {
        setIsLoading(false);
      }
    },
    [hydrateUserMetadata, queryClient],
  );

  /**
   * STRICT FAIL-CLOSED permission check (UX layer only; backend authorization remains authoritative):
   * - No session -> false
   * - Loading / Failed / Idle -> false
   * - Loaded with empty permissions -> false
   */
  const hasPermission = useCallback(
    (permission: string): boolean => {
      if (!session || permissionStatus !== 'loaded') {
        return false;
      }
      return permissions.includes(permission);
    },
    [permissions, permissionStatus, session],
  );

  const value = useMemo<AuthState>(
    () => ({
      isAuthenticated: session !== null,
      isLoading,
      permissionStatus,
      userId: session?.userId ?? null,
      email: session?.email ?? null,
      tenantId: session?.tenantId ?? null,
      scope: session?.scope ?? null,
      sessionId: session?.sessionId ?? null,
      isPlatformUser: serverIsPlatformUser || session?.scope === 'platform',
      permissions,
      roles,
      accessibleTenants,
      login,
      logout,
      switchTenant,
      reloadPermissions: hydrateUserMetadata,
      hasPermission,
    }),
    [session, isLoading, permissionStatus, serverIsPlatformUser, permissions, roles, accessibleTenants, login, logout, switchTenant, hydrateUserMetadata, hasPermission],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
