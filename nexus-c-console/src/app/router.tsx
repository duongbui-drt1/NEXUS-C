import { Suspense, lazy } from 'react';
import type { ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '../components/layout/AppShell';
import { LoadingState } from '../components/feedback/Feedback';
import { useAuth } from '../services/auth/AuthContext';

// Strict route-level code splitting (PART 9 Performance Budget)
const LoginPage = lazy(() => import('../features/auth/LoginPage'));
const ProfileSessionsPage = lazy(() => import('../features/auth/ProfileSessionsPage'));
const OverviewPage = lazy(() => import('../features/overview/OverviewPage'));
const TenantsPage = lazy(() => import('../features/tenants/TenantsPage'));
const OrganizationsPage = lazy(() => import('../features/organizations/OrganizationsPage'));
const UsersPage = lazy(() => import('../features/users/UsersPage'));
const AccessControlPage = lazy(() => import('../features/access-control/AccessControlPage'));
const ApplicationsPage = lazy(() => import('../features/applications/ApplicationsPage'));
const ApiManagementPage = lazy(() => import('../features/api-management/ApiManagementPage'));
const IntegrationsPage = lazy(() => import('../features/integrations/IntegrationsPage'));
const OperationsPage = lazy(() => import('../features/operations/OperationsPage'));
const AuditPage = lazy(() => import('../features/audit/AuditPage'));
const SubscriptionsPage = lazy(() => import('../features/subscriptions/SubscriptionsPage'));
const SettingsPage = lazy(() => import('../features/settings/SettingsPage'));

function RequireAuth({ children }: { children: ReactNode }) {
  const auth = useAuth();
  if (!auth.isAuthenticated) {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

function RequirePermission({
  permission,
  platformOnly,
  children,
}: {
  permission?: string;
  platformOnly?: boolean;
  children: ReactNode;
}) {
  const auth = useAuth();

  if (auth.permissionStatus === 'loading' || auth.permissionStatus === 'idle') {
    return <LoadingState label="Đang xác minh quyền truy cập..." />;
  }

  if (auth.permissionStatus === 'failed') {
    return (
      <div className="alert alert-danger" role="alert">
        <div>
          <strong>Không thể tải danh sách quyền từ máy chủ (Fail-Closed).</strong>
          <div style={{ marginTop: 4 }}>Quyền truy cập mặc định bị từ chối cho tới khi xác minh thành công.</div>
          <button type="button" className="btn btn-sm" style={{ marginTop: 8 }} onClick={() => void auth.reloadPermissions()}>
            Thử tải lại quyền
          </button>
        </div>
      </div>
    );
  }

  if (platformOnly && !auth.isPlatformUser) {
    return (
      <div className="alert alert-danger" role="alert">
        Chức năng này chỉ dành cho quản trị viên cấp Platform. Mọi truy cập trái phép đều bị backend từ chối (HTTP 403).
      </div>
    );
  }
  if (permission && !auth.hasPermission(permission)) {
    return (
      <div className="alert alert-danger" role="alert">
        Bạn không có quyền <span className="mono">{permission}</span> để truy cập khu vực này.
      </div>
    );
  }
  return <>{children}</>;
}

function LazyWrap({ children }: { children: ReactNode }) {
  return <Suspense fallback={<LoadingState label="Đang tải phân hệ..." />}>{children}</Suspense>;
}

export function AppRouter() {
  return (
    <Routes>
      <Route path="/login" element={<LazyWrap><LoginPage /></LazyWrap>} />

      <Route
        path="/"
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to="/overview" replace />} />
        <Route path="overview" element={<LazyWrap><OverviewPage /></LazyWrap>} />
        <Route path="profile" element={<LazyWrap><ProfileSessionsPage /></LazyWrap>} />

        <Route path="core" element={<Navigate to="/core/tenants" replace />} />
        <Route
          path="core/tenants"
          element={
            <RequirePermission permission="platform.tenants.read" platformOnly>
              <LazyWrap><TenantsPage /></LazyWrap>
            </RequirePermission>
          }
        />
        <Route
          path="core/organizations"
          element={
            <RequirePermission permission="platform.organizations.read">
              <LazyWrap><OrganizationsPage /></LazyWrap>
            </RequirePermission>
          }
        />
        <Route
          path="core/users"
          element={
            <RequirePermission permission="platform.users.read">
              <LazyWrap><UsersPage /></LazyWrap>
            </RequirePermission>
          }
        />
        <Route
          path="core/access-control"
          element={
            <RequirePermission permission="platform.roles.read">
              <LazyWrap><AccessControlPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="applications"
          element={
            <RequirePermission permission="platform.applications.read">
              <LazyWrap><ApplicationsPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="api-management/*"
          element={
            <RequirePermission permission="platform.api.read">
              <LazyWrap><ApiManagementPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="integrations"
          element={
            <RequirePermission permission="platform.integrations.read">
              <LazyWrap><IntegrationsPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="operations"
          element={
            <RequirePermission permission="platform.operations.read" platformOnly>
              <LazyWrap><OperationsPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="audit"
          element={
            <RequirePermission permission="platform.audit.read">
              <LazyWrap><AuditPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="subscriptions"
          element={
            <RequirePermission permission="platform.subscriptions.read">
              <LazyWrap><SubscriptionsPage /></LazyWrap>
            </RequirePermission>
          }
        />

        <Route
          path="settings"
          element={
            <RequirePermission permission="platform.configuration.read">
              <LazyWrap><SettingsPage /></LazyWrap>
            </RequirePermission>
          }
        />
      </Route>

      <Route path="*" element={<Navigate to="/overview" replace />} />
    </Routes>
  );
}
