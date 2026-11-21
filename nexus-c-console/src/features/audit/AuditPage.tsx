import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { RefreshCw } from 'lucide-react';
import type { AuditEventDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant } from '../../components/feedback/Feedback';
import { Dialog } from '../../components/dialogs/Dialog';
import { auditApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { formatDateTime, shortId } from '../../utils/format';

export default function AuditPage() {
  const auth = useAuth();
  const [page, setPage] = useState(1);
  const [actionFilter, setActionFilter] = useState('');
  const [resourceFilter, setResourceFilter] = useState('');
  const [tenantFilter, setTenantFilter] = useState('');
  const [selectedEvent, setSelectedEvent] = useState<AuditEventDto | null>(null);

  const params = {
    page,
    pageSize: 20,
    action: actionFilter.trim() || undefined,
    resourceType: resourceFilter.trim() || undefined,
    tenantId: auth.isPlatformUser && tenantFilter.trim() ? tenantFilter.trim() : undefined,
  };

  const auditQuery = useQuery({
    queryKey: queryKeys.audit(params),
    queryFn: ({ signal }) => auditApi.query(params, signal),
  });

  return (
    <div>
      <PageHeader
        title="Audit Center (Chỉ đọc — Append-Only)"
        subtitle="Nhật ký kiểm toán bất biến của toàn hệ thống. Không có chức năng chỉnh sửa hoặc xóa bản ghi kiểm toán."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Audit Center' }]}
        actions={
          <button type="button" className="btn" onClick={() => void auditQuery.refetch()}>
            <RefreshCw size={14} /> Làm mới
          </button>
        }
      />

      <DataTable
        columns={[
          { key: 'time', header: 'Thời gian', render: (e) => formatDateTime(e.occurredAt) },
          { key: 'actor', header: 'Actor', render: (e) => e.actorEmail ?? shortId(e.actorUserId) },
          { key: 'action', header: 'Action', render: (e) => <span className="mono">{e.action}</span> },
          {
            key: 'resource',
            header: 'Resource',
            render: (e) => (
              <span className="mono">
                {e.resourceType}
                {e.resourceId ? `:${shortId(e.resourceId)}` : ''}
              </span>
            ),
          },
          { key: 'tenant', header: 'Tenant', render: (e) => <span className="mono">{e.tenantId ? shortId(e.tenantId) : 'Platform'}</span> },
          { key: 'result', header: 'Kết quả', render: (e) => <Badge variant={statusToBadgeVariant(e.result)}>{e.result}</Badge> },
          {
            key: 'detail',
            header: 'Chi tiết',
            render: (e) => (
              <button type="button" className="btn btn-sm" onClick={() => setSelectedEvent(e)}>
                Xem
              </button>
            ),
          },
        ]}
        rows={auditQuery.data?.items ?? []}
        rowKey={(e) => e.id}
        isLoading={auditQuery.isLoading}
        error={auditQuery.error}
        onRetry={() => void auditQuery.refetch()}
        toolbar={
          <>
            <input
              type="search"
              className="input mono"
              style={{ maxWidth: 230 }}
              placeholder="Lọc action (vd: identity.login)"
              aria-label="Lọc theo action"
              value={actionFilter}
              onChange={(e) => {
                setActionFilter(e.target.value);
                setPage(1);
              }}
            />
            <input
              type="search"
              className="input"
              style={{ maxWidth: 190 }}
              placeholder="Lọc resourceType (vd: Tenant)"
              aria-label="Lọc theo resourceType"
              value={resourceFilter}
              onChange={(e) => {
                setResourceFilter(e.target.value);
                setPage(1);
              }}
            />
            {auth.isPlatformUser && (
              <input
                type="search"
                className="input mono"
                style={{ maxWidth: 240 }}
                placeholder="Lọc Tenant UUID (Platform Admin)"
                aria-label="Lọc theo Tenant ID"
                value={tenantFilter}
                onChange={(e) => {
                  setTenantFilter(e.target.value);
                  setPage(1);
                }}
              />
            )}
          </>
        }
        pagination={auditQuery.data ? { ...auditQuery.data, onPageChange: setPage } : undefined}
      />

      <Dialog
        open={selectedEvent !== null}
        title="Chi tiết Audit Event (Bất biến)"
        onClose={() => setSelectedEvent(null)}
        footer={
          <button type="button" className="btn" onClick={() => setSelectedEvent(null)}>
            Đóng
          </button>
        }
      >
        {selectedEvent && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div className="mono"><strong>ID:</strong> {selectedEvent.id}</div>
            <div className="mono"><strong>Correlation ID:</strong> {selectedEvent.correlationId}</div>
            <div><strong>Actor:</strong> {selectedEvent.actorEmail ?? selectedEvent.actorUserId ?? 'System'}</div>
            <div><strong>IP:</strong> <span className="mono">{selectedEvent.ipAddress ?? '—'}</span></div>
            <div><strong>Reason:</strong> {selectedEvent.reason ?? '—'}</div>
            {selectedEvent.beforeMetadataJson && (
              <div>
                <strong>Before Metadata (Redacted):</strong>
                <pre className="mono" style={{ background: 'var(--color-surface-subtle)', padding: 8, borderRadius: 4, overflow: 'auto', maxHeight: 160 }}>
                  {selectedEvent.beforeMetadataJson}
                </pre>
              </div>
            )}
            {selectedEvent.afterMetadataJson && (
              <div>
                <strong>After Metadata (Redacted):</strong>
                <pre className="mono" style={{ background: 'var(--color-surface-subtle)', padding: 8, borderRadius: 4, overflow: 'auto', maxHeight: 160 }}>
                  {selectedEvent.afterMetadataJson}
                </pre>
              </div>
            )}
          </div>
        )}
      </Dialog>
    </div>
  );
}
