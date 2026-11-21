import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { AppProviders } from '../src/app/providers';
import { AppRouter } from '../src/app/router';
import { tokenStore } from '../src/services/auth/tokenStore';

function jsonRes(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderAt(initialRoute: string) {
  return render(
    <MemoryRouter initialEntries={[initialRoute]}>
      <AppProviders>
        <AppRouter />
      </AppProviders>
    </MemoryRouter>,
  );
}

describe('NEXUS C Console — Auth, Fail-Closed RBAC, Tenants, API Management & Audit UI', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    tokenStore.clear();
  });

  it('redirects unauthenticated users to /login and displays backend login errors', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      jsonRes({ status: 401, errorCode: 'AUTH_INVALID_CREDENTIALS', detail: 'Invalid email or password.' }, 401),
    );

    const user = userEvent.setup();
    renderAt('/overview');

    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument();

    await user.type(screen.getByLabelText('Email quản trị'), 'wrong@nexus.local');
    await user.type(screen.getByLabelText('Mật khẩu'), 'BadPass123!');
    await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
  });

  it('enforces STRICT FAIL-CLOSED when GET /api/v1/auth/permissions fails (never grants implicit access)', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'admin-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'admin-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-admin',
      email: 'admin@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 'sess-1',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({ status: 500, errorCode: 'INTERNAL_SERVER_ERROR', detail: 'DB unavailable' }, 500);
      }
      return jsonRes({ success: true, data: [], meta: { correlationId: 'c1' } });
    });

    renderAt('/core/tenants');

    expect(await screen.findByText(/Không thể tải danh sách quyền từ máy chủ \(Fail-Closed\)/i)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Tenants' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'API Management' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Events & Jobs' })).not.toBeInTheDocument();
  });

  it('denies /api-management and /operations when user lacks platform.api.read / platform.operations.read', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'emp-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'emp-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-emp',
      email: 'employee@acme.vn',
      tenantId: 't-acme',
      scope: 'tenant',
      sessionId: 'sess-e',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({
          success: true,
          data: {
            userId: 'u-emp',
            tenantId: 't-acme',
            scope: 'tenant',
            isPlatformUser: false,
            roles: ['Employee'],
            permissions: ['platform.organizations.read', 'platform.notifications.read'],
          },
          meta: { correlationId: 'c-perm' },
        });
      }
      return jsonRes({ success: true, data: [], meta: { correlationId: 'c2' } });
    });

    renderAt('/api-management');
    expect(await screen.findByText(/Bạn không có quyền/i)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'API Management' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Events & Jobs' })).not.toBeInTheDocument();
  });

  it('logs in Platform SuperAdmin, renders Tenants list with search & suspend confirmation, and logs out', async () => {
    const suspendSpy = vi.fn();

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.endsWith('/api/v1/auth/login')) {
        return jsonRes({
          success: true,
          data: {
            accessToken: 'admin-jwt',
            accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
            refreshToken: 'admin-refresh',
            refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
            userId: 'u-admin',
            email: 'superadmin@nexus.local',
            tenantId: null,
            scope: 'platform',
            sessionId: 'sess-1',
          },
          meta: { correlationId: 'c1' },
        });
      }
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({
          success: true,
          data: {
            userId: 'u-admin',
            tenantId: null,
            scope: 'platform',
            isPlatformUser: true,
            roles: ['PlatformSuperAdmin'],
            permissions: [
              'platform.tenants.read',
              'platform.tenants.create',
              'platform.tenants.update',
              'platform.tenants.suspend',
              'platform.memberships.manage',
              'platform.users.read',
              'platform.audit.read',
              'platform.configuration.read',
              'platform.api.read',
              'platform.operations.read',
            ],
          },
          meta: { correlationId: 'c-perm' },
        });
      }
      if (url.endsWith('/api/v1/auth/logout')) return new Response(null, { status: 204 });
      if (url.endsWith('/health/live') || url.endsWith('/health/ready')) return new Response('Healthy', { status: 200 });
      if (url.includes('/api/v1/platform/tenants/accessible')) return jsonRes({ success: true, data: [], meta: { correlationId: 'c2' } });
      if (url.includes('/suspend') && method === 'POST') {
        suspendSpy(init?.body);
        return jsonRes({
          success: true,
          data: { id: 't-1', code: 'ACME', name: 'Công ty ACME', status: 'Suspended' },
          meta: { correlationId: 'c5' },
        });
      }
      if (url.includes('/api/v1/platform/tenants')) {
        return jsonRes({
          success: true,
          data: {
            items: [
              {
                id: 't-1',
                code: 'ACME',
                name: 'Công ty Cổ phần ACME Việt Nam',
                slug: 'acme-vn',
                status: 'Active',
                timeZone: 'Asia/Ho_Chi_Minh',
                defaultCurrency: 'VND',
                countryCode: 'VN',
                suspendedReason: null,
                suspendedAt: null,
                createdAt: '2026-11-21T10:00:00Z',
                updatedAt: '2026-11-21T10:00:00Z',
                rowVersion: 1,
              },
            ],
            page: 1,
            pageSize: 15,
            totalCount: 1,
            totalPages: 1,
            hasNext: false,
            hasPrevious: false,
          },
          meta: { correlationId: 'c6' },
        });
      }
      if (url.includes('/api/v1/platform/configuration/feature-flags')) {
        return jsonRes({
          success: true,
          data: [{ key: 'new_ui', isEnabled: true, description: null, version: 1, isTenantOverride: false }],
          meta: { correlationId: 'c6b' },
        });
      }
      return jsonRes({ success: true, data: { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }, meta: { correlationId: 'c7' } });
    });

    const user = userEvent.setup();
    renderAt('/login');

    await user.type(await screen.findByLabelText('Email quản trị'), 'superadmin@nexus.local');
    await user.type(screen.getByLabelText('Mật khẩu'), 'SuperAdmin!2026#');
    await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));

    expect(await screen.findByText('Tổng quan Nền tảng (NEXUS C)')).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Tenants' }));
    expect(await screen.findByText('Công ty Cổ phần ACME Việt Nam')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tạm ngưng' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Lý do tạm ngưng/), 'Quá hạn thanh toán');
    await user.click(screen.getByRole('button', { name: 'Xác nhận tạm ngưng' }));

    await waitFor(() => expect(suspendSpy).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole('button', { name: 'Đăng xuất' }));
    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument();
  });

  it('hides platform-only Tenants navigation for Tenant-scoped users and blocks direct /core/tenants access', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'tenant-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'tenant-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-tenant',
      email: 'tadmin@acme.vn',
      tenantId: 't-acme',
      scope: 'tenant',
      sessionId: 'sess-t',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({
          success: true,
          data: {
            userId: 'u-tenant',
            tenantId: 't-acme',
            scope: 'tenant',
            isPlatformUser: false,
            roles: ['TenantAdmin'],
            permissions: ['platform.organizations.read', 'platform.users.manage', 'platform.api.read'],
          },
          meta: { correlationId: 'c-perm' },
        });
      }
      return jsonRes({ success: true, data: [], meta: { correlationId: 'c2' } });
    });

    renderAt('/core/tenants');

    expect(await screen.findByText(/chỉ dành cho quản trị viên cấp Platform/i)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Tenants' })).not.toBeInTheDocument();
  });

  it('renders live OpenAPI endpoints in API Explorer and honest Not Available panels for missing API Keys endpoint', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'admin-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'admin-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-admin',
      email: 'admin@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 'sess-1',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({
          success: true,
          data: {
            userId: 'u-admin',
            tenantId: null,
            scope: 'platform',
            isPlatformUser: true,
            roles: ['PlatformSuperAdmin'],
            permissions: ['platform.api.read', 'platform.operations.read'],
          },
          meta: { correlationId: 'c-perm' },
        });
      }
      if (url.includes('/api/v1/platform/tenants/accessible')) {
        return jsonRes({ success: true, data: [], meta: { correlationId: 'c-acc' } });
      }
      if (url.endsWith('/openapi/v1.json')) {
        return jsonRes({
          openapi: '3.1.1',
          info: { title: 'NEXUS C API', version: '1.0.0' },
          paths: {
            '/api/v1/platform/tenants': {
              get: { tags: ['Tenants'], summary: 'List tenants', responses: { '200': { description: 'OK' } } },
            },
          },
        });
      }
      return new Response('Healthy', { status: 200 });
    });

    const user = userEvent.setup();
    renderAt('/api-management/explorer');

    expect(await screen.findByText('/api/v1/platform/tenants')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'API Keys' }));
    expect(await screen.findByText('API Keys Management')).toBeInTheDocument();
    expect(screen.getByText('Not Available')).toBeInTheDocument();
  });

  it('renders Audit Center strictly read-only with zero edit or delete buttons', async () => {
    tokenStore.setFromAuthResponse({
      accessToken: 'admin-jwt',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'admin-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'u-admin',
      email: 'auditor@nexus.local',
      tenantId: null,
      scope: 'platform',
      sessionId: 'sess-1',
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/v1/auth/permissions')) {
        return jsonRes({
          success: true,
          data: {
            userId: 'u-admin',
            tenantId: null,
            scope: 'platform',
            isPlatformUser: true,
            roles: ['Auditor'],
            permissions: ['platform.audit.read'],
          },
          meta: { correlationId: 'c-perm' },
        });
      }
      if (url.includes('/api/v1/platform/audit')) {
        return jsonRes({
          success: true,
          data: {
            items: [
              {
                id: 'aud-1',
                actorUserId: 'u-1',
                actorEmail: 'admin@nexus.local',
                tenantId: null,
                action: 'platform.tenant.create',
                resourceType: 'Tenant',
                resourceId: 't-1',
                occurredAt: '2026-11-21T12:00:00Z',
                ipAddress: '127.0.0.1',
                correlationId: 'corr-xyz',
                result: 'Success',
                reason: null,
                beforeMetadataJson: null,
                afterMetadataJson: '{"code":"ACME"}',
                isCrossTenantPlatformAccess: false,
              },
            ],
            page: 1,
            pageSize: 20,
            totalCount: 1,
            totalPages: 1,
            hasNext: false,
            hasPrevious: false,
          },
          meta: { correlationId: 'c-aud' },
        });
      }
      return jsonRes({ success: true, data: [], meta: { correlationId: 'c-empty' } });
    });

    renderAt('/audit');

    expect(await screen.findByText('platform.tenant.create')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /xóa|delete|sửa|edit/i })).not.toBeInTheDocument();
  });
});
