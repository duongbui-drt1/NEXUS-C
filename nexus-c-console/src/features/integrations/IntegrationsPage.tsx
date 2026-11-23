import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Bot, Mail, Play, Plus, Power, Trash2 } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant, useToast } from '../../components/feedback/Feedback';
import { Dialog } from '../../components/dialogs/Dialog';
import { emailApi, integrationsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

type IntegrationTab = 'providers' | 'connections' | 'email' | 'ai-governance';

export default function IntegrationsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [tab, setTab] = useState<IntegrationTab>('providers');
  const [selectedConnId, setSelectedConnId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  // Provider / Connection form
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [category, setCategory] = useState('ERP');
  const [providerId, setProviderId] = useState('');
  const [credRef, setCredRef] = useState('vault://nexus/tenant/connector');

  // Transactional Email send form
  const [emailTo, setEmailTo] = useState('');
  const [emailSubject, setEmailSubject] = useState('NEXUS C Transactional Verification');
  const [emailBody, setEmailBody] = useState('Email test từ nền tảng NEXUS C qua Resend.');

  // AI Governance policy form
  const [aiCapCode, setAiCapCode] = useState('DEFAULT');
  const [aiEnabled, setAiEnabled] = useState(true);
  const [aiKillSwitch, setAiKillSwitch] = useState(false);
  const [aiRequireApproval, setAiRequireApproval] = useState(true);
  const [aiBudget, setAiBudget] = useState('100000');
  const [aiClassification, setAiClassification] = useState('Internal');

  const canManageIntegrations = auth.hasPermission('platform.integrations.manage');
  const canManageEmail = auth.hasPermission('platform.email.manage');

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

  const emailStatusQuery = useQuery({
    queryKey: queryKeys.emailStatus,
    queryFn: ({ signal }) => emailApi.getStatus(signal),
    enabled: tab === 'email' && canManageEmail,
    retry: false,
  });

  const emailDeliveriesQuery = useQuery({
    queryKey: queryKeys.emailDeliveries({ page: 1 }),
    queryFn: ({ signal }) => emailApi.listDeliveries({ page: 1, pageSize: 20 }, signal),
    enabled: tab === 'email',
    retry: false,
  });

  const emailSuppressionsQuery = useQuery({
    queryKey: queryKeys.emailSuppressions,
    queryFn: ({ signal }) => emailApi.listSuppressions(signal),
    enabled: tab === 'email' && canManageEmail,
    retry: false,
  });

  const aiPoliciesQuery = useQuery({
    queryKey: queryKeys.aiPolicies,
    queryFn: ({ signal }) => integrationsApi.listAiPolicies(signal),
    enabled: tab === 'ai-governance' && Boolean(auth.tenantId),
    retry: false,
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

  const testConnMutation = useMutation({
    mutationFn: (id: string) => integrationsApi.testConnection(id),
    onSuccess: (res) => {
      notify(res.summary, res.passed ? 'success' : 'danger');
      void qc.invalidateQueries({ queryKey: queryKeys.connections });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const toggleConnMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? integrationsApi.reconnectConnection(id) : integrationsApi.disableConnection(id),
    onSuccess: () => {
      notify('Đã cập nhật trạng thái kết nối.', 'success');
      void qc.invalidateQueries({ queryKey: queryKeys.connections });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const deleteConnMutation = useMutation({
    mutationFn: (id: string) => integrationsApi.deleteConnection(id),
    onSuccess: () => {
      notify('Đã xóa hoặc lưu trữ kết nối an toàn.', 'success');
      void qc.invalidateQueries({ queryKey: queryKeys.connections });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const sendEmailMutation = useMutation({
    mutationFn: () =>
      emailApi.send({
        to: emailTo.trim(),
        subject: emailSubject.trim(),
        body: emailBody.trim(),
        isHtml: false,
      }),
    onSuccess: (d) => {
      notify(`Đã gửi email tới ${d.recipientMasked} (${d.status}).`, 'success');
      setEmailTo('');
      void qc.invalidateQueries({ queryKey: ['email'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const upsertAiPolicyMutation = useMutation({
    mutationFn: () =>
      integrationsApi.upsertAiPolicy({
        capabilityCode: aiCapCode.trim(),
        isEnabled: aiEnabled,
        killSwitchActive: aiKillSwitch,
        requireHumanApproval: aiRequireApproval,
        monthlyTokenBudget: Number(aiBudget),
        dataClassification: aiClassification,
      }),
    onSuccess: (p) => {
      notify(`Đã lưu chính sách AI Governance cho "${p.capabilityCode}".`, 'success');
      void qc.invalidateQueries({ queryKey: queryKeys.aiPolicies });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  return (
    <div>
      <PageHeader
        title="Integration Hub, Shared Email & AI Governance"
        subtitle="Danh mục nhà cung cấp, kết nối theo tenant, hạ tầng Email giao dịch (Resend) và quản trị rủi ro AI (Kill-Switch & Token Budget)."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Integrations' }]}
        actions={
          tab === 'providers' || tab === 'connections' ? (
            <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
              <Plus size={15} /> {tab === 'providers' ? 'Đăng ký Provider' : 'Tạo Kết nối'}
            </button>
          ) : undefined
        }
      />

      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'providers'} className={`tab-btn ${tab === 'providers' ? 'active' : ''}`} onClick={() => setTab('providers')}>
          Provider Registry ({providersQuery.data?.length ?? 0})
        </button>
        <button type="button" role="tab" aria-selected={tab === 'connections'} className={`tab-btn ${tab === 'connections' ? 'active' : ''}`} onClick={() => setTab('connections')}>
          Tenant Connections ({connectionsQuery.data?.length ?? 0})
        </button>
        <button type="button" role="tab" aria-selected={tab === 'email'} className={`tab-btn ${tab === 'email' ? 'active' : ''}`} onClick={() => setTab('email')}>
          <Mail size={13} /> Shared Email (Resend)
        </button>
        <button type="button" role="tab" aria-selected={tab === 'ai-governance'} className={`tab-btn ${tab === 'ai-governance' ? 'active' : ''}`} onClick={() => setTab('ai-governance')}>
          <Bot size={13} /> AI Governance
        </button>
      </div>

      {tab === 'providers' && (
        <DataTable
          columns={[
            { key: 'code', header: 'Mã', render: (p) => <span className="mono">{p.code}</span> },
            { key: 'name', header: 'Nhà cung cấp', render: (p) => p.name },
            { key: 'category', header: 'Nhóm', render: (p) => <Badge variant="neutral">{p.category}</Badge> },
            { key: 'hosts', header: 'Allowed Hosts', render: (p) => <span className="mono">{p.allowedHosts || '—'}</span> },
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
          <div className="alert alert-info" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
            <span>Kết nối tích hợp thuộc phạm vi Tenant. Chọn ngay một Tenant có sẵn để quản lý kết nối:</span>
            {auth.accessibleTenants.length > 0 && (
              <select
                className="select"
                style={{ width: 'auto', minWidth: 240 }}
                aria-label="Chọn nhanh Tenant cho Kết nối"
                value=""
                onChange={(e) => {
                  if (e.target.value) void auth.switchTenant(e.target.value);
                }}
              >
                <option value="">-- Chọn Tenant có sẵn --</option>
                {auth.accessibleTenants.map((t) => (
                  <option key={t.id} value={t.id}>
                    🏢 {t.code} — {t.name}
                  </option>
                ))}
              </select>
            )}
          </div>
        ) : (
          <div className="grid-2">
            <DataTable
              columns={[
                { key: 'name', header: 'Kết nối', render: (c) => c.name },
                { key: 'cred', header: 'Credential Ref', render: (c) => <span className="mono">{c.credentialReference}</span> },
                {
                  key: 'status',
                  header: 'Trạng thái & Health',
                  render: (c) => (
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      <Badge variant={statusToBadgeVariant(c.status)}>{c.status}</Badge>
                      {c.healthStatus && <Badge variant={c.healthStatus === 'Healthy' ? 'success' : 'neutral'}>{c.healthStatus}</Badge>}
                    </div>
                  ),
                },
                {
                  key: 'logs',
                  header: 'Thao tác',
                  render: (c) => (
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                      <button type="button" className="btn btn-sm" onClick={() => setSelectedConnId(c.id)}>
                        Log
                      </button>
                      {canManageIntegrations && (
                        <>
                          <button type="button" className="btn btn-sm" onClick={() => testConnMutation.mutate(c.id)}>
                            <Play size={11} /> Test
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm"
                            onClick={() => toggleConnMutation.mutate({ id: c.id, active: c.status === 'Disconnected' })}
                          >
                            <Power size={11} /> {c.status === 'Disconnected' ? 'Reconnect' : 'Disable'}
                          </button>
                          <button type="button" className="btn btn-sm btn-danger" onClick={() => deleteConnMutation.mutate(c.id)}>
                            <Trash2 size={11} />
                          </button>
                        </>
                      )}
                    </div>
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

      {tab === 'email' && (
        <div className="grid-2">
          <div>
            {emailStatusQuery.data && (
              <div className="panel" style={{ marginBottom: 16 }}>
                <div className="panel-header">
                  <h2 className="panel-title">Trạng thái Email Provider ({emailStatusQuery.data.providerName})</h2>
                  <Badge variant={emailStatusQuery.data.isApiKeyConfigured ? 'success' : 'warning'}>
                    {emailStatusQuery.data.isApiKeyConfigured ? 'Resend API Ready' : 'Dev Fallback'}
                  </Badge>
                </div>
                <div className="panel-body">
                  <div className="mono" style={{ fontSize: 12.5, marginBottom: 8 }}>
                    From: {emailStatusQuery.data.defaultFromName} &lt;{emailStatusQuery.data.defaultFromAddress}&gt;
                  </div>
                  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                    <Badge variant="info">Total: {emailStatusQuery.data.totalDeliveries}</Badge>
                    <Badge variant="success">Delivered/Sent: {emailStatusQuery.data.sentOrDeliveredCount}</Badge>
                    <Badge variant="danger">Failed/Bounced: {emailStatusQuery.data.failedOrBouncedCount}</Badge>
                    <Badge variant="warning">Suppressed: {emailStatusQuery.data.activeSuppressionCount}</Badge>
                  </div>
                </div>
              </div>
            )}

            <DataTable
              columns={[
                { key: 'to', header: 'Người nhận (Masked)', render: (d) => <span className="mono">{d.recipientMasked}</span> },
                { key: 'subj', header: 'Tiêu đề', render: (d) => d.subject },
                {
                  key: 'status',
                  header: 'Trạng thái',
                  render: (d) => (
                    <Badge variant={d.status === 'Delivered' || d.status === 'Sent' ? 'success' : d.status === 'Bounced' || d.status === 'Failed' ? 'danger' : 'warning'}>
                      {d.status}
                    </Badge>
                  ),
                },
                { key: 'at', header: 'Thời gian', render: (d) => formatDateTime(d.createdAt) },
              ]}
              rows={emailDeliveriesQuery.data?.items ?? []}
              rowKey={(d) => d.id}
              isLoading={emailDeliveriesQuery.isLoading}
              error={emailDeliveriesQuery.error}
              emptyTitle="Chưa có lịch sử gửi Email"
              emptyDescription="Email giao dịch gửi qua ISharedEmailService sẽ được ghi nhận tại đây."
            />
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Gửi Email Giao dịch Kiểm thử</h2>
            </div>
            <div className="panel-body">
              {!canManageEmail ? (
                <div className="alert alert-info">Cần quyền <span className="mono">platform.email.manage</span> để gửi email trực tiếp hoặc quản lý danh sách chặn (suppressions).</div>
              ) : (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    sendEmailMutation.mutate();
                  }}
                >
                  <div className="form-field">
                    <label className="form-label" htmlFor="em-to">Người nhận (To)</label>
                    <input id="em-to" type="email" className="input" placeholder="user@example.com" value={emailTo} onChange={(e) => setEmailTo(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="em-subj">Tiêu đề</label>
                    <input id="em-subj" className="input" value={emailSubject} onChange={(e) => setEmailSubject(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="em-body">Nội dung</label>
                    <input id="em-body" className="input" value={emailBody} onChange={(e) => setEmailBody(e.target.value)} required />
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={sendEmailMutation.isPending}>
                    <Mail size={14} /> {sendEmailMutation.isPending ? 'Đang gửi...' : 'Gửi Email'}
                  </button>
                </form>
              )}

              {emailSuppressionsQuery.data && emailSuppressionsQuery.data.length > 0 && (
                <div style={{ marginTop: 16 }}>
                  <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 6 }}>Danh sách Chặn Tự động (Bounce / Complaint Suppressions)</div>
                  <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                    {emailSuppressionsQuery.data.map((s) => (
                      <Badge key={s.id} variant="danger">{s.recipientMasked} ({s.reason})</Badge>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {tab === 'ai-governance' && (
        !auth.tenantId ? (
          <div className="alert alert-info" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
            <span>Chính sách AI Governance thuộc phạm vi Tenant (FORCE RLS). Chọn ngay một Tenant có sẵn:</span>
            {auth.accessibleTenants.length > 0 && (
              <select
                className="select"
                style={{ width: 'auto', minWidth: 240 }}
                aria-label="Chọn nhanh Tenant cho AI Governance"
                value=""
                onChange={(e) => {
                  if (e.target.value) void auth.switchTenant(e.target.value);
                }}
              >
                <option value="">-- Chọn Tenant có sẵn --</option>
                {auth.accessibleTenants.map((t) => (
                  <option key={t.id} value={t.id}>
                    🏢 {t.code} — {t.name}
                  </option>
                ))}
              </select>
            )}
          </div>
        ) : (
          <div className="grid-2">
            <DataTable
              columns={[
                { key: 'cap', header: 'Capability', render: (p) => <span className="mono">{p.capabilityCode}</span> },
                {
                  key: 'kill',
                  header: 'Kill-Switch',
                  render: (p) => (
                    <Badge variant={p.killSwitchActive ? 'danger' : p.isEnabled ? 'success' : 'neutral'}>
                      {p.killSwitchActive ? 'KILL-SWITCH ACTIVE' : p.isEnabled ? 'Allowed' : 'Disabled'}
                    </Badge>
                  ),
                },
                {
                  key: 'appr',
                  header: 'Human Approval',
                  render: (p) => <Badge variant={p.requireHumanApproval ? 'warning' : 'neutral'}>{p.requireHumanApproval ? 'Required' : 'Optional'}</Badge>,
                },
                { key: 'budget', header: 'Monthly Token Budget', render: (p) => <span className="mono">{p.monthlyTokenBudget.toLocaleString()}</span> },
              ]}
              rows={aiPoliciesQuery.data ?? []}
              rowKey={(p) => p.id}
              isLoading={aiPoliciesQuery.isLoading}
              error={aiPoliciesQuery.error}
              emptyTitle="Chưa có chính sách AI riêng cho Tenant"
              emptyDescription="Thiết lập Kill-Switch, hạn mức Token hàng tháng và cổng phê duyệt con người ở biểu mẫu bên cạnh."
            />

            <div className="panel">
              <div className="panel-header">
                <h2 className="panel-title">Cấu hình Chính sách AI Governance</h2>
              </div>
              <div className="panel-body">
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    upsertAiPolicyMutation.mutate();
                  }}
                >
                  <div className="form-field">
                    <label className="form-label" htmlFor="ai-cap">Capability Code</label>
                    <input id="ai-cap" className="input mono" value={aiCapCode} onChange={(e) => setAiCapCode(e.target.value)} required />
                  </div>
                  <div className="grid-2">
                    <div className="form-field">
                      <label className="form-label" htmlFor="ai-budget">Monthly Token Budget</label>
                      <input id="ai-budget" type="number" min={0} className="input" value={aiBudget} onChange={(e) => setAiBudget(e.target.value)} required />
                    </div>
                    <div className="form-field">
                      <label className="form-label" htmlFor="ai-cls">Data Classification</label>
                      <select id="ai-cls" className="select" value={aiClassification} onChange={(e) => setAiClassification(e.target.value)}>
                        <option value="Public">Public</option>
                        <option value="Internal">Internal</option>
                        <option value="Confidential">Confidential</option>
                        <option value="Restricted">Restricted</option>
                      </select>
                    </div>
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 12, fontSize: 13 }}>
                    <label><input type="checkbox" checked={aiEnabled} onChange={(e) => setAiEnabled(e.target.checked)} /> Cho phép Capability hoạt động (IsEnabled)</label>
                    <label><input type="checkbox" checked={aiKillSwitch} onChange={(e) => setAiKillSwitch(e.target.checked)} /> Kích hoạt Khóa Khẩn cấp (Kill-Switch Active — chặn 403 ngay lập tức)</label>
                    <label><input type="checkbox" checked={aiRequireApproval} onChange={(e) => setAiRequireApproval(e.target.checked)} /> Bắt buộc Human Approval Token cho tác vụ hệ trọng</label>
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={upsertAiPolicyMutation.isPending || !canManageIntegrations}>
                    Lưu chính sách AI
                  </button>
                </form>
              </div>
            </div>
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
