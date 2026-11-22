import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RefreshCw, RotateCcw } from 'lucide-react';
import type { DeadLetterMessageDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, EmptyState, ErrorState, LoadingState, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog } from '../../components/dialogs/Dialog';
import { operationsApi, systemApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

type OpsTab = 'jobs' | 'outbox' | 'inbox' | 'dead-letters';

export default function OperationsPage() {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { notify } = useToast();

  const [tab, setTab] = useState<OpsTab>('jobs');
  const [outboxStatus, setOutboxStatus] = useState('');
  const [page, setPage] = useState(1);
  const [retryTarget, setRetryTarget] = useState<DeadLetterMessageDto | null>(null);
  const [retryNote, setRetryNote] = useState('');

  const canManageOps = auth.hasPermission('platform.operations.manage');

  const healthQuery = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => systemApi.getHealth(signal),
    staleTime: 30_000,
  });

  const jobsQuery = useQuery({
    queryKey: queryKeys.opsJobs,
    queryFn: ({ signal }) => operationsApi.listJobs(signal),
    staleTime: 15_000,
  });

  const outboxQuery = useQuery({
    queryKey: queryKeys.opsOutbox({ page, status: outboxStatus }),
    queryFn: ({ signal }) =>
      operationsApi.listOutbox({ page, pageSize: 20, status: outboxStatus || undefined }, signal),
    enabled: tab === 'outbox',
  });

  const inboxQuery = useQuery({
    queryKey: queryKeys.opsInbox({ page }),
    queryFn: ({ signal }) => operationsApi.listInbox({ page, pageSize: 20 }, signal),
    enabled: tab === 'inbox',
  });

  const dlqQuery = useQuery({
    queryKey: queryKeys.opsDeadLetters({ page }),
    queryFn: ({ signal }) => operationsApi.listDeadLetters({ page, pageSize: 20 }, signal),
    enabled: tab === 'dead-letters' || tab === 'jobs',
  });

  const retryMutation = useMutation({
    mutationFn: ({ id, note }: { id: string; note?: string }) => operationsApi.retryDeadLetter(id, note),
    onSuccess: () => {
      notify('Đã đưa bản ghi Dead Letter quay lại hàng đợi Outbox và ghi Audit Log.', 'success');
      setRetryTarget(null);
      setRetryNote('');
      void queryClient.invalidateQueries({ queryKey: ['operations'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const unresolvedDlqCount = (dlqQuery.data?.items ?? []).filter((d) => !d.isResolved).length;

  const refreshAll = () => {
    void healthQuery.refetch();
    void jobsQuery.refetch();
    if (tab === 'outbox') void outboxQuery.refetch();
    if (tab === 'inbox') void inboxQuery.refetch();
    void dlqQuery.refetch();
  };

  return (
    <div>
      <PageHeader
        title="Events & Background Jobs"
        subtitle="Giám sát sức khỏe hệ thống, Outbox, Inbox idempotency, Dead Letter Queue và tiến trình nền trực tiếp từ NEXUS C Backend."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Operations' }]}
        actions={
          <button type="button" className="btn" onClick={refreshAll}>
            <RefreshCw size={14} /> Làm mới
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
            <span>Registered Background Jobs</span>
            <Badge variant="info">Live</Badge>
          </div>
          <div className="metric-value">{jobsQuery.isLoading ? '…' : (jobsQuery.data?.length ?? 0)}</div>
          <div className="metric-meta">/api/v1/platform/operations/jobs</div>
        </div>

        <div className="metric-box">
          <div className="metric-label">
            <span>Unresolved Dead Letters</span>
            <Badge variant={unresolvedDlqCount > 0 ? 'danger' : 'success'}>
              {unresolvedDlqCount > 0 ? 'Action Needed' : 'Clear'}
            </Badge>
          </div>
          <div className="metric-value">{dlqQuery.isLoading ? '…' : unresolvedDlqCount}</div>
          <div className="metric-meta">Tổng DLQ: {dlqQuery.data?.totalCount ?? 0}</div>
        </div>
      </div>

      <div className="tabs" role="tablist">
        {(
          [
            ['jobs', 'Background Jobs'],
            ['outbox', 'Outbox Queue'],
            ['inbox', 'Inbox Processed'],
            ['dead-letters', 'Dead Letters (DLQ)'],
          ] as Array<[OpsTab, string]>
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            className={`tab-btn ${tab === id ? 'active' : ''}`}
            onClick={() => {
              setTab(id);
              setPage(1);
            }}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'jobs' && (
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Trạng thái tiến trình nền (Runtime Tracker)</h2>
          </div>
          {jobsQuery.isLoading ? (
            <LoadingState />
          ) : jobsQuery.error ? (
            <div className="panel-body"><ErrorState error={jobsQuery.error} onRetry={() => void jobsQuery.refetch()} /></div>
          ) : !jobsQuery.data?.length ? (
            <EmptyState title="Chưa có job nào được đăng ký" description="Tiến trình Outbox sẽ xuất hiện khi OutboxProcessorBackgroundService khởi chạy." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Job Name</th>
                  <th>Status</th>
                  <th>Total Runs</th>
                  <th>Items Processed</th>
                  <th>Failures</th>
                  <th>Last Completed</th>
                  <th>Last Error</th>
                </tr>
              </thead>
              <tbody>
                {jobsQuery.data.map((job) => (
                  <tr key={job.jobName}>
                    <td className="mono" style={{ fontWeight: 600 }}>{job.jobName}</td>
                    <td>
                      <Badge variant={job.status === 'Degraded' ? 'danger' : job.status === 'Running' ? 'info' : 'success'}>
                        {job.status}
                      </Badge>
                    </td>
                    <td className="mono">{job.totalRuns}</td>
                    <td className="mono">{job.totalItemsProcessed}</td>
                    <td className="mono">{job.totalFailures}</td>
                    <td>{formatDateTime(job.lastRunCompletedAt)}</td>
                    <td className="mono" style={{ fontSize: 12, color: 'var(--color-danger)' }}>{job.lastError ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {tab === 'outbox' && (
        <div className="panel">
          <div className="table-toolbar">
            <select
              className="select"
              style={{ width: 180 }}
              aria-label="Lọc trạng thái Outbox"
              value={outboxStatus}
              onChange={(e) => {
                setOutboxStatus(e.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả trạng thái</option>
              <option value="Pending">Pending</option>
              <option value="Published">Published</option>
              <option value="DeadLettered">DeadLettered</option>
            </select>
            <span className="form-hint" style={{ marginLeft: 'auto' }}>
              Tổng: {outboxQuery.data?.totalCount ?? 0} sự kiện
            </span>
          </div>
          {outboxQuery.isLoading ? (
            <LoadingState />
          ) : outboxQuery.error ? (
            <div className="panel-body"><ErrorState error={outboxQuery.error} onRetry={() => void outboxQuery.refetch()} /></div>
          ) : !outboxQuery.data?.items.length ? (
            <EmptyState title="Không có sự kiện Outbox" description="Chưa có bản ghi Outbox nào khớp bộ lọc." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Event Type</th>
                  <th>Status</th>
                  <th>Retries</th>
                  <th>Correlation ID</th>
                  <th>Created</th>
                  <th>Last Error</th>
                </tr>
              </thead>
              <tbody>
                {outboxQuery.data.items.map((m) => (
                  <tr key={m.id}>
                    <td className="mono">{m.eventType}</td>
                    <td>
                      <Badge variant={m.status === 'Published' ? 'success' : m.status === 'DeadLettered' ? 'danger' : 'warning'}>
                        {m.status}
                      </Badge>
                    </td>
                    <td className="mono">{m.retryCount}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{m.correlationId}</td>
                    <td>{formatDateTime(m.createdAt)}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{m.lastError ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {tab === 'inbox' && (
        <div className="panel">
          {inboxQuery.isLoading ? (
            <LoadingState />
          ) : inboxQuery.error ? (
            <div className="panel-body"><ErrorState error={inboxQuery.error} onRetry={() => void inboxQuery.refetch()} /></div>
          ) : !inboxQuery.data?.items.length ? (
            <EmptyState title="Chưa có bản ghi Inbox" description="Các sự kiện đã tiêu thụ idempotently bởi consumers sẽ hiển thị tại đây." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Event Type</th>
                  <th>Consumer</th>
                  <th>Event ID</th>
                  <th>Tenant ID</th>
                  <th>Processed At</th>
                </tr>
              </thead>
              <tbody>
                {inboxQuery.data.items.map((m) => (
                  <tr key={m.id}>
                    <td className="mono">{m.eventType}</td>
                    <td className="mono">{m.consumerName}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{m.eventId}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{m.tenantId ?? 'Platform'}</td>
                    <td>{formatDateTime(m.processedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {tab === 'dead-letters' && (
        <div className="panel">
          {dlqQuery.isLoading ? (
            <LoadingState />
          ) : dlqQuery.error ? (
            <div className="panel-body"><ErrorState error={dlqQuery.error} onRetry={() => void dlqQuery.refetch()} /></div>
          ) : !dlqQuery.data?.items.length ? (
            <EmptyState title="Dead Letter Queue trống" description="Không có sự kiện lỗi nào đang chờ xử lý." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Event Type</th>
                  <th>Status</th>
                  <th>Retries (Auto / Manual)</th>
                  <th>Correlation ID</th>
                  <th>Reason</th>
                  <th>Failed At</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {dlqQuery.data.items.map((d) => (
                  <tr key={d.id}>
                    <td className="mono">{d.eventType}</td>
                    <td>
                      <Badge variant={d.isResolved ? 'success' : 'danger'}>
                        {d.isResolved ? 'Resolved / Requeued' : 'Dead Lettered'}
                      </Badge>
                    </td>
                    <td className="mono">{d.retryCount} / {d.manualRetryCount}/{d.maxManualRetries}</td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{d.correlationId}</td>
                    <td style={{ fontSize: 12.5 }}>{d.reason}</td>
                    <td>{formatDateTime(d.failedAt)}</td>
                    <td>
                      {canManageOps && !d.isResolved && d.manualRetryCount < d.maxManualRetries ? (
                        <button
                          type="button"
                          className="btn btn-sm btn-primary"
                          onClick={() => setRetryTarget(d)}
                        >
                          <RotateCcw size={13} /> Retry
                        </button>
                      ) : (
                        <span className="form-hint">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      <ConfirmDialog
        open={retryTarget !== null}
        title="Xác nhận chạy lại sự kiện Dead Letter"
        message={`Bạn có chắc muốn đưa sự kiện ${retryTarget?.eventType ?? ''} (EventId: ${retryTarget?.eventId ?? ''}) quay lại Outbox? Hành động này sẽ được ghi nhận vào Audit Log.`}
        confirmLabel="Xác nhận Retry"
        isPending={retryMutation.isPending}
        onCancel={() => {
          setRetryTarget(null);
          setRetryNote('');
        }}
        onConfirm={() => {
          if (retryTarget) {
            retryMutation.mutate({ id: retryTarget.id, note: retryNote.trim() || undefined });
          }
        }}
      >
        <div className="form-field" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="dlq-retry-note">Ghi chú vận hành (tùy chọn)</label>
          <input
            id="dlq-retry-note"
            className="input"
            placeholder="Ví dụ: Đã khôi phục kết nối RabbitMQ"
            value={retryNote}
            onChange={(e) => setRetryNote(e.target.value)}
          />
        </div>
      </ConfirmDialog>
    </div>
  );
}
