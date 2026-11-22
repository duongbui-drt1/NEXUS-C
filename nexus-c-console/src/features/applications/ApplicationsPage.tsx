import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { RefreshCw } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, ErrorState, LoadingState, useToast } from '../../components/feedback/Feedback';
import { applicationsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';

export default function ApplicationsPage() {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { notify } = useToast();
  const hasTenant = Boolean(auth.tenantId);
  const canManageApps = auth.hasPermission('platform.applications.manage');

  const registryQuery = useQuery({
    queryKey: queryKeys.applications,
    queryFn: ({ signal }) => applicationsApi.listRegistry(signal),
  });

  const tenantAppsQuery = useQuery({
    queryKey: queryKeys.tenantApplications,
    queryFn: ({ signal }) => applicationsApi.listTenantApplications(signal),
    enabled: hasTenant,
    retry: false,
  });

  const entitlementsQuery = useQuery({
    queryKey: queryKeys.entitlements,
    queryFn: ({ signal }) => applicationsApi.listEntitlements(signal),
    enabled: hasTenant,
    retry: false,
  });

  const toggleMutation = useMutation({
    mutationFn: ({ code, activate }: { code: string; activate: boolean }) =>
      activate ? applicationsApi.activateForTenant(code) : applicationsApi.deactivateForTenant(code),
    onSuccess: (res) => {
      notify(
        `${res.isActivated ? 'Đã kích hoạt' : 'Đã tắt'} ứng dụng ${res.applicationCode} cho tenant hiện tại.`,
        'success',
      );
      void queryClient.invalidateQueries({ queryKey: queryKeys.tenantApplications });
      void queryClient.invalidateQueries({ queryKey: queryKeys.entitlements });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const tenantAppMap = new Map((tenantAppsQuery.data ?? []).map((a) => [a.applicationCode, a]));
  const entitlementMap = new Map((entitlementsQuery.data ?? []).map((e) => [e.featureCode, e]));

  return (
    <div>
      <PageHeader
        title="Ecosystem Application Registry (NEXUS C / F / E / M)"
        subtitle="Danh mục ứng dụng chính thức của hệ sinh thái NEXUS cùng trạng thái kích hoạt & quyền sử dụng (entitlements) theo từng Tenant."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Applications' }]}
        actions={
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              type="button"
              className="btn"
              onClick={() => {
                void registryQuery.refetch();
                if (hasTenant) {
                  void tenantAppsQuery.refetch();
                  void entitlementsQuery.refetch();
                }
              }}
            >
              <RefreshCw size={14} /> Làm mới
            </button>
            <Link to="/subscriptions" className="btn btn-primary">
              Quản lý Gói & Thuê bao
            </Link>
          </div>
        }
      />

      <div className="alert alert-info" style={{ marginBottom: 16 }}>
        <strong>Lưu ý kiến trúc:</strong> Việc một ứng dụng được đăng ký trong Application Registry phản ánh siêu dữ liệu hợp đồng & tương thích phiên bản — không đồng nghĩa tiến trình triển khai thực tế của F/E/M đang chạy online.
      </div>

      {registryQuery.isLoading ? (
        <LoadingState label="Đang tải danh mục ứng dụng NEXUS..." />
      ) : registryQuery.error ? (
        <ErrorState error={registryQuery.error} onRetry={() => void registryQuery.refetch()} />
      ) : (
        <div className="metric-grid">
          {(registryQuery.data ?? []).map((app) => {
            const tenantState = tenantAppMap.get(app.code);
            const ent = entitlementMap.get(app.code);
            return (
              <div key={app.code} className="metric-box">
                <div className="metric-label">
                  <span className="mono" style={{ fontWeight: 600 }}>{app.code}</span>
                  <div style={{ display: 'flex', gap: 6 }}>
                    <Badge variant={app.status === 'Active' ? 'success' : 'info'}>{app.status}</Badge>
                    <Badge variant="neutral">v{app.currentVersion}</Badge>
                  </div>
                </div>
                <div style={{ fontWeight: 600, fontSize: 15, marginTop: 6 }}>{app.name}</div>
                <div className="metric-meta">{app.description}</div>
                <div className="metric-meta mono" style={{ marginTop: 6 }}>
                  Channel: {app.releaseChannel} · Min Core: v{app.minPlatformVersion}
                </div>
                {app.moduleDependencies.length > 0 && (
                  <div className="metric-meta mono">
                    Dependencies: {app.moduleDependencies.join(', ')}
                  </div>
                )}

                {hasTenant && (
                  <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--color-border)', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      <Badge variant={tenantState?.isEntitled ? 'success' : 'neutral'}>
                        {tenantState?.isEntitled ? 'Entitled' : tenantState?.entitlementReason ?? 'Not Entitled'}
                      </Badge>
                      {ent?.quotaLimit != null && (
                        <Badge variant="neutral">
                          Usage: {ent.currentUsage}/{ent.quotaLimit}
                        </Badge>
                      )}
                    </div>

                    {canManageApps && app.code !== 'NEXUS_C' && (
                      <button
                        type="button"
                        className="btn btn-sm"
                        disabled={toggleMutation.isPending}
                        onClick={() =>
                          toggleMutation.mutate({
                            code: app.code,
                            activate: !(tenantState?.isActivated ?? false),
                          })
                        }
                      >
                        {tenantState?.isActivated ? 'Tắt cho Tenant' : 'Kích hoạt'}
                      </button>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
