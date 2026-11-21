import { useQuery } from '@tanstack/react-query';
import { RefreshCw } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, NotAvailablePanel } from '../../components/feedback/Feedback';
import { systemApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { formatDateTime } from '../../utils/format';

export default function OperationsPage() {
  const healthQuery = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => systemApi.getHealth(signal),
    staleTime: 30_000,
  });

  return (
    <div>
      <PageHeader
        title="Events & Background Jobs"
        subtitle="Giám sát sức khỏe hệ thống, Outbox/Inbox, Dead Letter Queue và tiến trình nền (không tự động polling dày đặc)."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Operations' }]}
        actions={
          <button type="button" className="btn" onClick={() => void healthQuery.refetch()}>
            <RefreshCw size={14} /> Kiểm tra lại
          </button>
        }
      />

      <div className="metric-grid">
        <div className="metric-box">
          <div className="metric-label">
            <span>API Process Liveness</span>
            <Badge variant={healthQuery.data?.live.healthy ? 'success' : 'danger'}>
              {healthQuery.isLoading ? 'Checking' : healthQuery.data?.live.healthy ? 'Healthy' : 'Down'}
            </Badge>
          </div>
          <div className="metric-value">{healthQuery.data?.live.healthy ? 'UP' : 'DOWN'}</div>
          <div className="metric-meta">Cập nhật: {formatDateTime(healthQuery.data?.checkedAt)}</div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>PostgreSQL Readiness</span>
            <Badge variant={healthQuery.data?.ready.healthy ? 'success' : 'danger'}>
              {healthQuery.isLoading ? 'Checking' : healthQuery.data?.ready.healthy ? 'Ready' : 'Degraded'}
            </Badge>
          </div>
          <div className="metric-value">{healthQuery.data?.ready.healthy ? 'CONNECTED' : 'ERROR'}</div>
          <div className="metric-meta">/health/ready</div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Outbox / Inbox / DLQ Backlog</span>
            <Badge variant="warning">Not Available</Badge>
          </div>
          <div className="metric-value" style={{ fontSize: 15, color: 'var(--color-text-muted)' }}>
            Chưa có Admin API
          </div>
          <div className="metric-meta">Không hiển thị số liệu giả lập</div>
        </div>
      </div>

      <NotAvailablePanel
        title="Outbox, Inbox, Dead Letter Queue & Background Job Inspection"
        endpoint="GET /api/v1/platform/operations/outbox | /inbox | /dead-letters | /jobs"
        description="Hạ tầng Transactional Outbox, Inbox idempotency và Dead Letter Queue đã vận hành ở tầng PostgreSQL (schema messaging) và RabbitMQ trên NEXUS C Backend, nhưng hiện chưa có REST Admin API cho phép Console liệt kê hàng đợi hoặc gửi lệnh Retry/Reprocess. Các nút thao tác giả lập không được tạo để đảm bảo an toàn vận hành."
      />
    </div>
  );
}
