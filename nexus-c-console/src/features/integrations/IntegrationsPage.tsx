import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant, useToast } from '../../components/feedback/Feedback';
import { Dialog } from '../../components/dialogs/Dialog';
import { integrationsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

export default function IntegrationsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [tab, setTab] = useState<'providers' | 'connections'>('providers');
  const [selectedConnId, setSelectedConnId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [category, setCategory] = useState('ERP');
  const [providerId, setProviderId] = useState('');
  const [credRef, setCredRef] = useState('vault://nexus/tenant/connector');

  const providersQuery = useQuery({
    queryKey: queryKeys.providers,
    queryFn: ({ signal }) => integrationsApi.listProviders(signal),
  });

  const connectionsQuery = useQuery({
    queryKey: queryKeys.connections,
    queryFn: ({ signal }) => integrationsApi.listConnections(signal),
    enabled: Boolean(auth.tenantId),
  });

  const syncLogsQuery = useQuery({
    queryKey: queryKeys.syncLogs(selectedConnId ?? ''),
    queryFn: ({ signal }) => integrationsApi.listSyncLogs(selectedConnId!, signal),
    enabled: Boolean(selectedConnId && auth.tenantId),
  });

  const addProvider = useMutation({
    mutationFn: () => integrationsApi.registerProvider({ code, name, category, maxRetries: 3, timeoutSeconds: 30 }),
    onSuccess: () => {
      notify('Đã đăng ký nhà cung cấp tích hợp.', 'success');
      setCreateOpen(false);
      setCode('');
      setName('');
      void qc.invalidateQueries({ queryKey: queryKeys.providers });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const addConnection = useMutation({
    mutationFn: () => integrationsApi.createConnection({ providerId, name, credentialReference: credRef, maxRetries: 3, timeoutSeconds: 30 }),
    onSuccess: () => {
      notify('Đã tạo kết nối tích hợp.', 'success');
      setCreateOpen(false);
      setName('');
      void qc.invalidateQueries({ queryKey: queryKeys.connections });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  return (
    <div>
      <PageHeader
        title="Integration Hub"
        subtitle="Danh mục nhà cung cấp (E-Invoice, Banking, Logistics, Email, S3, AI), kết nối theo tenant và lịch sử đồng bộ."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Integrations' }]}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
            <Plus size={15} /> {tab === 'providers' ? 'Đăng ký Provider' : 'Tạo Kết nối'}
          </button>
        }
      />

      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'providers'} className={`tab-btn ${tab === 'providers' ? 'active' : ''}`} onClick={() => setTab('providers')}>
          Provider Registry ({providersQuery.data?.length ?? 0})
        </button>
        <button type="button" role="tab" aria-selected={tab === 'connections'} className={`tab-btn ${tab === 'connections' ? 'active' : ''}`} onClick={() => setTab('connections')}>
          Tenant Connections ({connectionsQuery.data?.length ?? 0})
        </button>
      </div>

      {tab === 'providers' && (
        <DataTable
          columns={[
            { key: 'code', header: 'Mã', render: (p) => <span className="mono">{p.code}</span> },
            { key: 'name', header: 'Nhà cung cấp', render: (p) => p.name },
            { key: 'category', header: 'Nhóm', render: (p) => <Badge variant="neutral">{p.category}</Badge> },
            { key: 'retry', header: 'Retry / Timeout', render: (p) => `${p.defaultMaxRetries} lần · ${p.defaultTimeoutSeconds}s` },
            { key: 'status', header: 'Trạng thái', render: (p) => <Badge variant={p.isActive ? 'success' : 'neutral'}>{p.isActive ? 'Active' : 'Inactive'}</Badge> },
          ]}
          rows={providersQuery.data ?? []}
          rowKey={(p) => p.id}
          isLoading={providersQuery.isLoading}
          error={providersQuery.error}
          onRetry={() => void providersQuery.refetch()}
          emptyTitle="Chưa có nhà cung cấp nào được đăng ký"
          emptyDescription="Các dịch vụ bên ngoài chưa có credentials sẽ ở trạng thái Not Configured."
        />
      )}

      {tab === 'connections' && (
        !auth.tenantId ? (
          <div className="alert alert-info">Kết nối tích hợp thuộc phạm vi Tenant. Hãy chọn một Tenant ở thanh tiêu đề trên cùng.</div>
        ) : (
          <div className="grid-2">
            <DataTable
              columns={[
                { key: 'name', header: 'Kết nối', render: (c) => c.name },
                { key: 'cred', header: 'Credential Ref', render: (c) => <span className="mono">{c.credentialReference}</span> },
                { key: 'status', header: 'Trạng thái', render: (c) => <Badge variant={statusToBadgeVariant(c.status)}>{c.status}</Badge> },
                {
                  key: 'logs',
                  header: 'Sync Logs',
                  render: (c) => (
                    <button type="button" className="btn btn-sm" onClick={() => setSelectedConnId(c.id)}>
                      Xem log
                    </button>
                  ),
                },
              ]}
              rows={connectionsQuery.data ?? []}
              rowKey={(c) => c.id}
              isLoading={connectionsQuery.isLoading}
              error={connectionsQuery.error}
              onRetry={() => void connectionsQuery.refetch()}
            />

            <DataTable
              columns={[
                { key: 'dir', header: 'Chiều', render: (s) => s.direction },
                { key: 'status', header: 'Kết quả', render: (s) => <Badge variant={statusToBadgeVariant(s.status)}>{s.status}</Badge> },
                { key: 'count', header: 'Bản ghi', render: (s) => s.recordsProcessed },
                { key: 'at', header: 'Thời gian', render: (s) => formatDateTime(s.startedAt) },
              ]}
              rows={syncLogsQuery.data ?? []}
              rowKey={(s) => s.id}
              isLoading={syncLogsQuery.isLoading}
              error={syncLogsQuery.error}
              emptyTitle={selectedConnId ? 'Chưa có lịch sử đồng bộ' : 'Chọn một kết nối để xem Sync History'}
            />
          </div>
        )
      )}

      <Dialog
        open={createOpen}
        title={tab === 'providers' ? 'Đăng ký Integration Provider' : 'Tạo Integration Connection'}
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => (tab === 'providers' ? addProvider.mutate() : addConnection.mutate())}
            >
              Lưu
            </button>
          </>
        }
      >
        {tab === 'providers' ? (
          <>
            <div className="form-field">
              <label className="form-label" htmlFor="p-code">Mã Provider</label>
              <input id="p-code" className="input mono" placeholder="EINVOICE_VN" value={code} onChange={(e) => setCode(e.target.value)} />
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="p-name">Tên nhà cung cấp</label>
              <input id="p-name" className="input" value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="p-cat">Nhóm</label>
              <select id="p-cat" className="select" value={category} onChange={(e) => setCategory(e.target.value)}>
                <option value="EInvoice">E-Invoice</option>
                <option value="Banking">Banking</option>
                <option value="DigitalSignature">Digital Signature</option>
                <option value="GPS">GPS</option>
                <option value="Logistics">Logistics</option>
                <option value="Email">Email</option>
                <option value="ObjectStorage">Object Storage</option>
                <option value="AI">AI</option>
                <option value="ERP">ERP</option>
              </select>
            </div>
          </>
        ) : (
          <>
            <div className="form-field">
              <label className="form-label" htmlFor="c-prov">Provider</label>
              <select id="c-prov" className="select" value={providerId} onChange={(e) => setProviderId(e.target.value)}>
                <option value="">-- Chọn Provider --</option>
                {(providersQuery.data ?? []).map((p) => (
                  <option key={p.id} value={p.id}>{p.code} — {p.name}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="c-name">Tên kết nối</label>
              <input id="c-name" className="input" value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="c-ref">Credential Reference (vault://, env:// hoặc kms://)</label>
              <input id="c-ref" className="input mono" value={credRef} onChange={(e) => setCredRef(e.target.value)} />
              <span className="form-hint">Tuyệt đối không lưu API key/password thô.</span>
            </div>
          </>
        )}
      </Dialog>
    </div>
  );
}
