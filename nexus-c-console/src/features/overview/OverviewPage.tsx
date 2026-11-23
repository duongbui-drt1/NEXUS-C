import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { RefreshCw } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, ErrorState, LoadingState, statusToBadgeVariant } from '../../components/feedback/Feedback';
import { auditApi, configurationApi, systemApi } from '../../services/api/modules/platform.api';
import { tenantsApi } from '../../services/api/modules/tenants.api';
import { usersApi } from '../../services/api/modules/users.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { formatDateTime } from '../../utils/format';

export default function OverviewPage() {
  const auth = useAuth();

  const healthQuery = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => systemApi.getHealth(signal),
    staleTime: 30_000,
  });

  const tenantsQuery = useQuery({
    queryKey: queryKeys.tenants({ page: 1, pageSize: 1 }),
    queryFn: ({ signal }) => tenantsApi.list({ page: 1, pageSize: 1 }, signal),
    enabled: auth.isPlatformUser && auth.hasPermission('platform.tenants.read'),
    staleTime: 60_000,
  });

  const usersQuery = useQuery({
    queryKey: queryKeys.users({ page: 1, pageSize: 1 }),
    queryFn: ({ signal }) => usersApi.list({ page: 1, pageSize: 1 }, signal),
    enabled: auth.hasPermission('platform.users.read'),
    staleTime: 60_000,
  });

  const flagsQuery = useQuery({
    queryKey: queryKeys.featureFlags,
    queryFn: ({ signal }) => configurationApi.listFlags(signal),
    enabled: auth.hasPermission('platform.configuration.read'),
    staleTime: 60_000,
  });

  const auditQuery = useQuery({
    queryKey: queryKeys.audit({ page: 1, pageSize: 6 }),
    queryFn: ({ signal }) => auditApi.query({ page: 1, pageSize: 6 }, signal),
    enabled: auth.hasPermission('platform.audit.read'),
    staleTime: 30_000,
  });

  const refreshAll = () => {
    void healthQuery.refetch();
    if (tenantsQuery.isEnabled) void tenantsQuery.refetch();
    if (usersQuery.isEnabled) void usersQuery.refetch();
    if (flagsQuery.isEnabled) void flagsQuery.refetch();
    if (auditQuery.isEnabled) void auditQuery.refetch();
  };

  return (
    <div>
      <PageHeader
        title="Tổng quan Nền tảng (NEXUS C)"
        subtitle="Giám sát trạng thái trực tiếp từ NEXUS C Backend. Mọi số liệu chưa có API backend đều được hiển thị trung thực ở trạng thái Not Available."
        breadcrumbs={[{ label: 'Overview' }]}
        actions={
          <button type="button" className="btn" onClick={refreshAll}>
            <RefreshCw size={14} /> Làm mới thủ công
          </button>
        }
      />

      <div className="metric-grid">
        <div className="metric-box">
          <div className="metric-label">
            <span>API Liveness</span>
            <Badge variant={healthQuery.data?.live.healthy ? 'success' : 'danger'}>
              {healthQuery.isLoading ? 'Checking' : healthQuery.data?.live.healthy ? 'Healthy' : 'Unavailable'}
            </Badge>
          </div>
          <div className="metric-value">
            {healthQuery.isLoading ? '…' : healthQuery.data?.live.healthy ? 'OK' : 'DOWN'}
          </div>
          <div className="metric-meta">
            Cập nhật: {formatDateTime(healthQuery.data?.checkedAt)}
          </div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Database & Readiness</span>
            <Badge variant={healthQuery.data?.ready.healthy ? 'success' : 'danger'}>
              {healthQuery.isLoading ? 'Checking' : healthQuery.data?.ready.healthy ? 'Ready' : 'Degraded'}
            </Badge>
          </div>
          <div className="metric-value">
            {healthQuery.isLoading ? '…' : healthQuery.data?.ready.healthy ? 'Ready' : `HTTP ${healthQuery.data?.ready.status ?? 0}`}
          </div>
          <div className="metric-meta">Endpoint: /health/ready</div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Tenants (Tổng)</span>
            <Badge variant="neutral">Platform</Badge>
          </div>
          <div className="metric-value">
            {!auth.isPlatformUser
              ? 'Tenant Scope'
              : tenantsQuery.isLoading
                ? '…'
                : tenantsQuery.data?.totalCount ?? '—'}
          </div>
          <div className="metric-meta">
            {auth.isPlatformUser ? <Link to="/core/tenants">Quản lý Tenants →</Link> : 'Chỉ hiển thị ở phạm vi Platform'}
          </div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Users (Tổng)</span>
            <Badge variant="neutral">Identity</Badge>
          </div>
          <div className="metric-value">
            {usersQuery.isLoading ? '…' : usersQuery.data?.totalCount ?? '—'}
          </div>
          <div className="metric-meta">
            <Link to="/core/users">Quản lý Người dùng →</Link>
          </div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Feature Flags</span>
            <Badge variant="neutral">Config</Badge>
          </div>
          <div className="metric-value">
            {flagsQuery.isLoading ? '…' : Array.isArray(flagsQuery.data) ? flagsQuery.data.length : '—'}
          </div>
          <div className="metric-meta">
            Đang bật: {(Array.isArray(flagsQuery.data) ? flagsQuery.data : []).filter((f) => f.isEnabled).length}
          </div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Events & Background Jobs</span>
            <Badge variant="warning">Not Available</Badge>
          </div>
          <div className="metric-value" style={{ fontSize: 15, color: 'var(--color-text-muted)' }}>
            Chưa có Telemetry API
          </div>
          <div className="metric-meta">
            <Link to="/operations">Xem chi tiết Operations →</Link>
          </div>
        </div>
      </div>

      <div className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Hoạt động quản trị gần đây (Audit Log)</h2>
          <Link to="/audit" style={{ fontSize: 13 }}>Xem toàn bộ Audit Center →</Link>
        </div>

        {!auth.hasPermission('platform.audit.read') ? (
          <div className="panel-body">
            <div className="alert alert-danger" role="alert">Vui lòng liên hệ Admin để xem chi tiết</div>
          </div>
        ) : auditQuery.isLoading ? (
          <LoadingState />
        ) : auditQuery.error ? (
          <div className="panel-body">
            <ErrorState error={auditQuery.error} onRetry={() => void auditQuery.refetch()} />
          </div>
        ) : (auditQuery.data?.items.length ?? 0) === 0 ? (
          <div className="panel-body" style={{ color: 'var(--color-text-muted)' }}>Chưa ghi nhận hoạt động quản trị nào.</div>
        ) : (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Actor</th>
                  <th>Action</th>
                  <th>Resource</th>
                  <th>Kết quả</th>
                </tr>
              </thead>
              <tbody>
                {auditQuery.data!.items.map((ev) => (
                  <tr key={ev.id}>
                    <td>{formatDateTime(ev.occurredAt)}</td>
                    <td>{ev.actorEmail ?? ev.actorUserId ?? 'System'}</td>
                    <td className="mono">{ev.action}</td>
                    <td className="mono">{ev.resourceType}{ev.resourceId ? `:${ev.resourceId.slice(0, 8)}` : ''}</td>
                    <td><Badge variant={statusToBadgeVariant(ev.result)}>{ev.result}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
