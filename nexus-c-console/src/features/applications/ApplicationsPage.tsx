import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, ErrorState, LoadingState, NotAvailablePanel } from '../../components/feedback/Feedback';
import { subscriptionsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';

const ECOSYSTEM_APPS = [
  { code: 'NEXUS_F', name: 'NEXUS F — Finance', description: 'Kế toán, sổ cái, hóa đơn và quản trị tài chính doanh nghiệp.' },
  { code: 'NEXUS_E', name: 'NEXUS E — Express', description: 'Kho vận (WMS), vận tải (TMS) và điều phối logistics.' },
  { code: 'NEXUS_M', name: 'NEXUS M — Management', description: 'Báo cáo điều hành, KPI và phân tích quản trị cấp lãnh đạo.' },
];

export default function ApplicationsPage() {
  const auth = useAuth();
  const hasTenant = Boolean(auth.tenantId);

  const plansQuery = useQuery({
    queryKey: queryKeys.plans,
    queryFn: ({ signal }) => subscriptionsApi.listPlans(signal),
  });

  const subQuery = useQuery({
    queryKey: queryKeys.currentSubscription,
    queryFn: ({ signal }) => subscriptionsApi.getCurrent(signal),
    enabled: hasTenant,
    retry: false,
  });

  return (
    <div>
      <PageHeader
        title="Quản lý Ứng dụng (NEXUS F / E / M)"
        subtitle="Theo dõi quyền sử dụng (entitlements) của 3 sản phẩm nghiệp vụ theo gói thuê bao. Entitlement không thay thế backend authorization."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Applications' }]}
        actions={
          <Link to="/subscriptions" className="btn btn-primary">
            Quản lý Gói & Cấp quyền thuê bao
          </Link>
        }
      />

      <div className="metric-grid">
        {ECOSYSTEM_APPS.map((app) => {
          const ent = subQuery.data?.entitlements.find((e) => e.featureCode === app.code);
          const enabledInPlans = (plansQuery.data ?? []).filter((p) =>
            p.features.some((f) => f.featureCode === app.code && f.isEnabled),
          ).length;

          return (
            <div key={app.code} className="metric-box">
              <div className="metric-label">
                <span className="mono">{app.code}</span>
                {hasTenant ? (
                  <Badge variant={ent?.isEnabled ? 'success' : 'neutral'}>
                    {subQuery.isLoading ? 'Checking' : ent?.isEnabled ? 'Entitled' : 'Not Entitled'}
                  </Badge>
                ) : (
                  <Badge variant="info">{enabledInPlans} Plan(s)</Badge>
                )}
              </div>
              <div style={{ fontWeight: 600, fontSize: 15, marginTop: 6 }}>{app.name}</div>
              <div className="metric-meta">{app.description}</div>
              {ent?.quotaLimit != null && (
                <div className="metric-meta mono">Quota limit: {ent.quotaLimit}</div>
              )}
            </div>
          );
        })}
      </div>

      {plansQuery.isLoading ? (
        <LoadingState />
      ) : plansQuery.error ? (
        <ErrorState error={plansQuery.error} onRetry={() => void plansQuery.refetch()} />
      ) : null}

      <NotAvailablePanel
        title="Application Registry & Service Version Metadata"
        endpoint="GET /api/v1/platform/applications"
        description="NEXUS C Backend hiện đã cung cấp API quản lý Plans & Entitlements (NEXUS_F / NEXUS_E / NEXUS_M) qua module Subscription, nhưng chưa cung cấp endpoint Application Registry trả về phiên bản build/triển khai thực tế của từng dịch vụ F, E, M."
      />
    </div>
  );
}
