import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { KeyRound, Play, Plus, RefreshCw, RotateCw, Trash2 } from 'lucide-react';
import type { OpenApiOperation, WebhookDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, EmptyState, ErrorState, LoadingState, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog, Dialog } from '../../components/dialogs/Dialog';
import { apiRequest } from '../../services/api/client';
import { apiManagementApi, integrationsApi, systemApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

type ApiTab = 'overview' | 'explorer' | 'clients' | 'keys' | 'rate-limits' | 'logs' | 'webhooks';

const TAB_ROUTES: Array<{ id: ApiTab; label: string; path: string }> = [
  { id: 'overview', label: 'API Overview', path: '/api-management' },
  { id: 'explorer', label: 'API Explorer (OpenAPI)', path: '/api-management/explorer' },
  { id: 'clients', label: 'API Clients', path: '/api-management/clients' },
  { id: 'keys', label: 'API Keys', path: '/api-management/keys' },
  { id: 'rate-limits', label: 'Rate Limiting', path: '/api-management/rate-limits' },
  { id: 'logs', label: 'API Logs', path: '/api-management/logs' },
  { id: 'webhooks', label: 'Webhooks', path: '/api-management/webhooks' },
];

interface EndpointRow {
  method: string;
  path: string;
  tag: string;
  summary: string;
  operation: OpenApiOperation;
}

export default function ApiManagementPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { notify } = useToast();

  const canManageApi = auth.hasPermission('platform.api.manage');
  const canManageRateLimits = auth.hasPermission('platform.rate-limits.manage') || canManageApi;
  const canReadApiLogs = auth.hasPermission('platform.api-logs.read') || auth.hasPermission('platform.api.read');
  const canManageIntegrations = auth.hasPermission('platform.integrations.manage');

  const activeTab: ApiTab =
    TAB_ROUTES.find((t) => location.pathname === t.path)?.id ??
    TAB_ROUTES.find((t) => t.id !== 'overview' && location.pathname.startsWith(t.path))?.id ??
    'overview';

  const [filter, setFilter] = useState('');
  const [selectedEndpoint, setSelectedEndpoint] = useState<EndpointRow | null>(null);
  const [tryResult, setTryResult] = useState<string | null>(null);
  const [tryLoading, setTryLoading] = useState(false);

  // One-time secret reveal dialog (never persisted to localStorage)
  const [revealedSecret, setRevealedSecret] = useState<{ title: string; secret: string } | null>(null);

  // Service client creation form
  const [clientId, setClientId] = useState('');
  const [clientName, setClientName] = useState('');
  const [clientAppCode, setClientAppCode] = useState('NEXUS_F');
  const [clientScopes, setClientScopes] = useState('platform.organizations.read, platform.subscriptions.read');
  const [revokeClientId, setRevokeClientId] = useState<string | null>(null);

  // API key creation form
  const [keyName, setKeyName] = useState('');
  const [keyScopes, setKeyScopes] = useState('platform.organizations.read');
  const [revokeKeyId, setRevokeKeyId] = useState<string | null>(null);

  // Webhook form & selection state
  const [whEventType, setWhEventType] = useState('platform.tenant.created.v1');
  const [whUrl, setWhUrl] = useState('');
  const [whSecretRef, setWhSecretRef] = useState('vault://nexus/webhooks/signing-key');
  const [selectedWebhook, setSelectedWebhook] = useState<WebhookDto | null>(null);
  const [deleteWebhookId, setDeleteWebhookId] = useState<string | null>(null);

  // Distributed Rate Limiting policy form
  const [rlName, setRlName] = useState('');
  const [rlRoute, setRlRoute] = useState('/api/v1/*');
  const [rlScope, setRlScope] = useState('HumanSession');
  const [rlPermit, setRlPermit] = useState('120');
  const [rlWindow, setRlWindow] = useState('60');

  // API Request Logs filters
  const [logRouteFilter, setLogRouteFilter] = useState('');
  const [logDecisionFilter, setLogDecisionFilter] = useState('');
  const [logPage, setLogPage] = useState(1);

  const healthQuery = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => systemApi.getHealth(signal),
    staleTime: 30_000,
  });

  const openapiQuery = useQuery({
    queryKey: queryKeys.openapi,
    queryFn: ({ signal }) => systemApi.getOpenApiSpec(signal),
    staleTime: 300_000,
    retry: false,
  });

  const clientsQuery = useQuery({
    queryKey: queryKeys.apiClients,
    queryFn: ({ signal }) => apiManagementApi.listClients(signal),
    enabled: activeTab === 'clients' || activeTab === 'overview',
    retry: false,
  });

  const keysQuery = useQuery({
    queryKey: queryKeys.apiKeys,
    queryFn: ({ signal }) => apiManagementApi.listKeys(signal),
    enabled: activeTab === 'keys' || activeTab === 'overview',
    retry: false,
  });

  const webhooksQuery = useQuery({
    queryKey: queryKeys.webhooks,
    queryFn: ({ signal }) => integrationsApi.listWebhooks(signal),
    enabled: activeTab === 'webhooks' && Boolean(auth.tenantId),
    retry: false,
  });

  const deliveriesQuery = useQuery({
    queryKey: queryKeys.webhookDeliveries(selectedWebhook?.id ?? ''),
    queryFn: ({ signal }) => integrationsApi.listWebhookDeliveries(selectedWebhook!.id, signal),
    enabled: activeTab === 'webhooks' && Boolean(selectedWebhook?.id),
    retry: false,
  });

  const rateLimitsQuery = useQuery({
    queryKey: queryKeys.rateLimits,
    queryFn: ({ signal }) => apiManagementApi.listRateLimits(signal),
    enabled: activeTab === 'rate-limits',
    retry: false,
  });

  const logQueryParams = {
    route: logRouteFilter.trim() || undefined,
    rateLimitDecision: logDecisionFilter || undefined,
    page: logPage,
    pageSize: 25,
  };
  const apiLogsQuery = useQuery({
    queryKey: queryKeys.apiLogs(logQueryParams),
    queryFn: ({ signal }) => apiManagementApi.queryApiLogs(logQueryParams, signal),
    enabled: activeTab === 'logs' && canReadApiLogs,
    retry: false,
  });

  const createRateLimitMutation = useMutation({
    mutationFn: () =>
      apiManagementApi.createRateLimit({
        name: rlName.trim(),
        routePattern: rlRoute.trim(),
        scopeType: rlScope,
        permitLimit: Number(rlPermit),
        windowSeconds: Number(rlWindow),
        isEnabled: true,
        priority: 50,
      }),
    onSuccess: (p) => {
      notify(`Đã tạo chính sách Rate Limit "${p.name}".`, 'success');
      setRlName('');
      void queryClient.invalidateQueries({ queryKey: queryKeys.rateLimits });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const deleteRateLimitMutation = useMutation({
    mutationFn: (id: string) => apiManagementApi.deleteRateLimit(id),
    onSuccess: () => {
      notify('Đã xóa chính sách Rate Limit.', 'success');
      void queryClient.invalidateQueries({ queryKey: queryKeys.rateLimits });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const endpoints = useMemo<EndpointRow[]>(() => {
    const paths = openapiQuery.data?.paths;
    if (!paths) return [];
    const list: EndpointRow[] = [];
    for (const [path, methods] of Object.entries(paths)) {
      for (const [method, op] of Object.entries(methods)) {
        if (!['get', 'post', 'put', 'patch', 'delete'].includes(method.toLowerCase())) continue;
        list.push({
          method: method.toUpperCase(),
          path,
          tag: op.tags?.[0] ?? 'General',
          summary: op.summary ?? op.description ?? '',
          operation: op,
        });
      }
    }
    return list.sort((a, b) => a.path.localeCompare(b.path));
  }, [openapiQuery.data]);

  const filteredEndpoints = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return endpoints;
    return endpoints.filter(
      (e) =>
        e.path.toLowerCase().includes(q) ||
        e.method.toLowerCase().includes(q) ||
        e.tag.toLowerCase().includes(q),
    );
  }, [endpoints, filter]);

  const createClientMutation = useMutation({
    mutationFn: () =>
      apiManagementApi.createClient({
        clientId: clientId.trim(),
        name: clientName.trim(),
        applicationCode: clientAppCode.trim() || null,
        allowedScopes: clientScopes.split(',').map((s) => s.trim()).filter(Boolean),
      }),
    onSuccess: (res) => {
      setClientId('');
      setClientName('');
      setRevealedSecret({
        title: `Client Secret cho ${res.client.clientId} (chỉ hiển thị 1 lần)`,
        secret: res.clientSecret,
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiClients });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const rotateClientMutation = useMutation({
    mutationFn: (id: string) => apiManagementApi.rotateClientSecret(id),
    onSuccess: (res) => {
      setRevealedSecret({
        title: `Secret mới cho ${res.client.clientId} (chỉ hiển thị 1 lần)`,
        secret: res.clientSecret,
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiClients });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const revokeClientMutation = useMutation({
    mutationFn: (id: string) => apiManagementApi.revokeClient(id),
    onSuccess: () => {
      notify('Đã thu hồi Service Client thành công.', 'success');
      setRevokeClientId(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiClients });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const createKeyMutation = useMutation({
    mutationFn: () =>
      apiManagementApi.createKey({
        name: keyName.trim(),
        allowedScopes: keyScopes.split(',').map((s) => s.trim()).filter(Boolean),
        tenantId: auth.tenantId,
      }),
    onSuccess: (res) => {
      setKeyName('');
      setRevealedSecret({
        title: `API Key "${res.apiKey.name}" (chỉ hiển thị 1 lần)`,
        secret: res.rawApiKey,
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiKeys });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const rotateKeyMutation = useMutation({
    mutationFn: (id: string) => apiManagementApi.rotateKey(id),
    onSuccess: (res) => {
      setRevealedSecret({
        title: `API Key mới cho "${res.apiKey.name}" (chỉ hiển thị 1 lần)`,
        secret: res.rawApiKey,
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiKeys });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const revokeKeyMutation = useMutation({
    mutationFn: (id: string) => apiManagementApi.revokeKey(id),
    onSuccess: () => {
      notify('Đã thu hồi API Key.', 'success');
      setRevokeKeyId(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.apiKeys });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const registerWebhook = useMutation({
    mutationFn: () =>
      integrationsApi.registerWebhook({
        eventType: whEventType.trim(),
        targetUrl: whUrl.trim(),
        signingSecretRef: whSecretRef.trim(),
      }),
    onSuccess: (wh) => {
      notify(`Đã đăng ký webhook cho sự kiện ${wh.eventType}.`, 'success');
      setWhUrl('');
      void queryClient.invalidateQueries({ queryKey: queryKeys.webhooks });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const toggleWebhookMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? integrationsApi.enableWebhook(id) : integrationsApi.disableWebhook(id),
    onSuccess: () => {
      notify('Đã cập nhật trạng thái Webhook.', 'success');
      void queryClient.invalidateQueries({ queryKey: queryKeys.webhooks });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const rotateWebhookSecretMutation = useMutation({
    mutationFn: (id: string) => integrationsApi.rotateWebhookSecret(id),
    onSuccess: (res) => {
      setRevealedSecret({
        title: `Signing Secret mới (v${res.webhook.secretVersion ?? 2}) cho Webhook (chỉ hiển thị 1 lần)`,
        secret: res.signingSecret,
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.webhooks });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const deleteWebhookMutation = useMutation({
    mutationFn: (id: string) => integrationsApi.deleteWebhook(id),
    onSuccess: () => {
      notify('Đã xóa Webhook.', 'success');
      setDeleteWebhookId(null);
      setSelectedWebhook(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.webhooks });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const handleSafeGetExecute = async (ep: EndpointRow) => {
    if (ep.method !== 'GET' || ep.path.includes('{')) return;
    setTryLoading(true);
    setTryResult(null);
    try {
      const res = await apiRequest<unknown>(ep.path, { method: 'GET' });
      setTryResult(JSON.stringify(res, null, 2));
    } catch (err) {
      setTryResult(`Error: ${getErrorMessage(err)}`);
    } finally {
      setTryLoading(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="API Management"
        subtitle="Quản lý OpenAPI thực tế, OAuth 2.0 Service Clients (NEXUS F/E/M), Scoped API Keys và Webhooks của NEXUS C."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'API Management' }]}
        actions={
          <button
            type="button"
            className="btn"
            onClick={() => {
              void healthQuery.refetch();
              void openapiQuery.refetch();
              void clientsQuery.refetch();
              void keysQuery.refetch();
            }}
          >
            <RefreshCw size={14} /> Làm mới
          </button>
        }
      />

      <div className="tabs" role="tablist">
        {TAB_ROUTES.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={activeTab === t.id}
            className={`tab-btn ${activeTab === t.id ? 'active' : ''}`}
            onClick={() => navigate(t.path)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {activeTab === 'overview' && (
        <div className="metric-grid">
          <div className="metric-box">
            <div className="metric-label">
              <span>API Readiness</span>
              <Badge variant={healthQuery.data?.ready.healthy ? 'success' : 'danger'}>
                {healthQuery.data?.ready.healthy ? 'Healthy' : 'Unavailable'}
              </Badge>
            </div>
            <div className="metric-value">{healthQuery.data?.ready.healthy ? 'Online' : 'Offline'}</div>
            <div className="metric-meta">Kiểm tra lúc: {formatDateTime(healthQuery.data?.checkedAt)}</div>
          </div>

          <div className="metric-box">
            <div className="metric-label">
              <span>Registered Endpoints</span>
              <Badge variant="info">{openapiQuery.data?.info?.version ?? 'v1'}</Badge>
            </div>
            <div className="metric-value">{openapiQuery.isLoading ? '…' : endpoints.length}</div>
            <div className="metric-meta">Đọc trực tiếp từ /openapi/v1.json</div>
          </div>

          <div className="metric-box">
            <div className="metric-label">
              <span>Active Service Clients</span>
              <Badge variant="success">OAuth 2.0</Badge>
            </div>
            <div className="metric-value">
              {clientsQuery.isLoading ? '…' : (clientsQuery.data?.filter((c) => c.isActive).length ?? 0)}
            </div>
            <div className="metric-meta">POST /api/v1/auth/token (client_credentials)</div>
          </div>

          <div className="metric-box">
            <div className="metric-label">
              <span>Active Scoped API Keys</span>
              <Badge variant="neutral">Hashed</Badge>
            </div>
            <div className="metric-value">
              {keysQuery.isLoading ? '…' : (keysQuery.data?.filter((k) => k.isActive).length ?? 0)}
            </div>
            <div className="metric-meta">Xác thực SHA-256 một chiều, giới hạn scope</div>
          </div>
        </div>
      )}

      {activeTab === 'explorer' && (
        <div className="grid-2">
          <div className="panel">
            <div className="table-toolbar">
              <input
                type="search"
                className="input"
                placeholder="Lọc endpoint theo đường dẫn, HTTP method hoặc tag..."
                aria-label="Lọc OpenAPI endpoints"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
              />
            </div>

            {openapiQuery.isLoading ? (
              <LoadingState label="Đang tải OpenAPI spec từ /openapi/v1.json..." />
            ) : openapiQuery.error ? (
              <div className="panel-body">
                <ErrorState error={openapiQuery.error} onRetry={() => void openapiQuery.refetch()} />
              </div>
            ) : (
              <div style={{ maxHeight: 540, overflowY: 'auto' }}>
                <table className="data-table">
                  <thead>
                    <tr>
                      <th style={{ width: 80 }}>Method</th>
                      <th>Endpoint</th>
                      <th>Tag</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredEndpoints.map((ep) => (
                      <tr
                        key={`${ep.method}-${ep.path}`}
                        style={{ cursor: 'pointer' }}
                        onClick={() => {
                          setSelectedEndpoint(ep);
                          setTryResult(null);
                        }}
                      >
                        <td>
                          <Badge
                            variant={
                              ep.method === 'GET' ? 'info' : ep.method === 'DELETE' ? 'danger' : ep.method === 'POST' ? 'success' : 'warning'
                            }
                          >
                            {ep.method}
                          </Badge>
                        </td>
                        <td className="mono">{ep.path}</td>
                        <td>{ep.tag}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Chi tiết Endpoint</h2>
              {selectedEndpoint && selectedEndpoint.method === 'GET' && !selectedEndpoint.path.includes('{') && (
                <button
                  type="button"
                  className="btn btn-sm btn-primary"
                  disabled={tryLoading}
                  onClick={() => void handleSafeGetExecute(selectedEndpoint)}
                >
                  <Play size={13} /> {tryLoading ? 'Đang gọi...' : 'Chạy GET an toàn'}
                </button>
              )}
            </div>
            <div className="panel-body">
              {!selectedEndpoint ? (
                <p style={{ color: 'var(--color-text-muted)', margin: 0 }}>
                  Chọn một endpoint ở bảng bên trái để xem chi tiết từ OpenAPI spec thực tế.
                </p>
              ) : (
                <div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
                    <Badge variant="info">{selectedEndpoint.method}</Badge>
                    <span className="mono" style={{ fontWeight: 600 }}>{selectedEndpoint.path}</span>
                  </div>
                  {tryResult && (
                    <pre className="mono" style={{ background: 'var(--color-surface-subtle)', border: '1px solid var(--color-border)', borderRadius: 4, padding: 10, maxHeight: 280, overflow: 'auto', margin: 0 }}>
                      {tryResult}
                    </pre>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'clients' && (
        <div className="grid-2">
          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">OAuth 2.0 Service Clients</h2>
            </div>
            {clientsQuery.isLoading ? (
              <LoadingState />
            ) : clientsQuery.error ? (
              <div className="panel-body"><ErrorState error={clientsQuery.error} onRetry={() => void clientsQuery.refetch()} /></div>
            ) : !clientsQuery.data?.length ? (
              <EmptyState title="Chưa có Service Client" description="Đăng ký client_credentials cho NEXUS F, E hoặc M ở biểu mẫu bên cạnh." />
            ) : (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Client ID</th>
                    <th>App</th>
                    <th>Status</th>
                    <th>Scopes</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {clientsQuery.data.map((c) => (
                    <tr key={c.id}>
                      <td>
                        <div className="mono" style={{ fontWeight: 600 }}>{c.clientId}</div>
                        <div className="form-hint">{c.name} · {c.secretPrefix}…</div>
                      </td>
                      <td className="mono">{c.applicationCode ?? '—'}</td>
                      <td>
                        <Badge variant={c.isActive ? 'success' : 'danger'}>{c.isActive ? 'Active' : 'Revoked'}</Badge>
                      </td>
                      <td className="mono" style={{ fontSize: 11.5 }}>{c.allowedScopes.join(', ')}</td>
                      <td>
                        {canManageApi && c.isActive && (
                          <div style={{ display: 'flex', gap: 6 }}>
                            <button type="button" className="btn btn-sm" onClick={() => rotateClientMutation.mutate(c.id)}>
                              <RotateCw size={12} /> Rotate
                            </button>
                            <button type="button" className="btn btn-sm btn-danger" onClick={() => setRevokeClientId(c.id)}>
                              Revoke
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Đăng ký Service Client mới</h2>
            </div>
            <div className="panel-body">
              {!canManageApi ? (
                <div className="alert alert-info">Cần quyền <span className="mono">platform.api.manage</span> để tạo hoặc xoay vòng Service Client.</div>
              ) : (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    createClientMutation.mutate();
                  }}
                >
                  <div className="form-field">
                    <label className="form-label" htmlFor="sc-id">Client ID</label>
                    <input id="sc-id" className="input mono" placeholder="nexus-f-service" value={clientId} onChange={(e) => setClientId(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="sc-name">Tên hiển thị</label>
                    <input id="sc-name" className="input" placeholder="NEXUS Finance Backend" value={clientName} onChange={(e) => setClientName(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="sc-app">Application Code</label>
                    <select id="sc-app" className="select" value={clientAppCode} onChange={(e) => setClientAppCode(e.target.value)}>
                      <option value="NEXUS_F">NEXUS_F</option>
                      <option value="NEXUS_E">NEXUS_E</option>
                      <option value="NEXUS_M">NEXUS_M</option>
                      <option value="">External Partner</option>
                    </select>
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="sc-scopes">Allowed Scopes (phân tách bởi dấu phẩy, cấm wildcard *)</label>
                    <input id="sc-scopes" className="input mono" value={clientScopes} onChange={(e) => setClientScopes(e.target.value)} required />
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={createClientMutation.isPending}>
                    <Plus size={14} /> {createClientMutation.isPending ? 'Đang tạo...' : 'Tạo Service Client'}
                  </button>
                </form>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'keys' && (
        <div className="grid-2">
          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Scoped API Keys (SHA-256 Hashed)</h2>
            </div>
            {keysQuery.isLoading ? (
              <LoadingState />
            ) : keysQuery.error ? (
              <div className="panel-body"><ErrorState error={keysQuery.error} onRetry={() => void keysQuery.refetch()} /></div>
            ) : !keysQuery.data?.length ? (
              <EmptyState title="Chưa có API Key" description="Tạo API Key giới hạn scope ở biểu mẫu bên cạnh." />
            ) : (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Tên & Prefix</th>
                    <th>Status</th>
                    <th>Scopes</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {keysQuery.data.map((k) => (
                    <tr key={k.id}>
                      <td>
                        <div style={{ fontWeight: 600 }}>{k.name}</div>
                        <div className="mono form-hint">{k.keyPrefix}…</div>
                      </td>
                      <td>
                        <Badge variant={k.isActive ? 'success' : 'danger'}>{k.isActive ? 'Active' : 'Revoked'}</Badge>
                      </td>
                      <td className="mono" style={{ fontSize: 11.5 }}>{k.allowedScopes.join(', ')}</td>
                      <td>
                        {canManageApi && k.isActive && (
                          <div style={{ display: 'flex', gap: 6 }}>
                            <button type="button" className="btn btn-sm" onClick={() => rotateKeyMutation.mutate(k.id)}>
                              <RotateCw size={12} /> Rotate
                            </button>
                            <button type="button" className="btn btn-sm btn-danger" onClick={() => setRevokeKeyId(k.id)}>
                              Revoke
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Tạo Scoped API Key</h2>
            </div>
            <div className="panel-body">
              {!canManageApi ? (
                <div className="alert alert-info">Cần quyền <span className="mono">platform.api.manage</span> để tạo hoặc thu hồi API Key.</div>
              ) : (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    createKeyMutation.mutate();
                  }}
                >
                  <div className="form-field">
                    <label className="form-label" htmlFor="ak-name">Tên API Key</label>
                    <input id="ak-name" className="input" placeholder="ERP Sync Reader" value={keyName} onChange={(e) => setKeyName(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="ak-scopes">Explicit Permission Scopes</label>
                    <input id="ak-scopes" className="input mono" value={keyScopes} onChange={(e) => setKeyScopes(e.target.value)} required />
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={createKeyMutation.isPending}>
                    <KeyRound size={14} /> {createKeyMutation.isPending ? 'Đang tạo...' : 'Tạo API Key'}
                  </button>
                </form>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'rate-limits' && (
        <div className="grid-2">
          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Distributed Rate Limit Policies ({rateLimitsQuery.data?.length ?? 0})</h2>
            </div>
            {rateLimitsQuery.isLoading ? (
              <LoadingState />
            ) : rateLimitsQuery.error ? (
              <div className="panel-body"><ErrorState error={rateLimitsQuery.error} onRetry={() => void rateLimitsQuery.refetch()} /></div>
            ) : !rateLimitsQuery.data?.length ? (
              <EmptyState title="Chưa có chính sách Rate Limit" description="Tạo chính sách giới hạn lưu lượng phân tán mới ở biểu mẫu bên cạnh." />
            ) : (
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Tên & Route</th>
                    <th>Scope</th>
                    <th>Limit</th>
                    <th>Status</th>
                    <th>Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {rateLimitsQuery.data.map((p) => (
                    <tr key={p.id}>
                      <td>
                        <div className="mono" style={{ fontWeight: 600 }}>{p.name}</div>
                        <div className="mono form-hint">{p.routePattern}</div>
                      </td>
                      <td><Badge variant="info">{p.scopeType}</Badge></td>
                      <td className="mono">{p.permitLimit} / {p.windowSeconds}s</td>
                      <td><Badge variant={p.isEnabled ? 'success' : 'neutral'}>{p.isEnabled ? 'Enabled' : 'Disabled'}</Badge></td>
                      <td>
                        {canManageRateLimits && (
                          <button type="button" className="btn btn-sm btn-danger" onClick={() => deleteRateLimitMutation.mutate(p.id)}>
                            <Trash2 size={12} /> Xóa
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Tạo chính sách Rate Limit phân tán</h2>
            </div>
            <div className="panel-body">
              {!canManageRateLimits ? (
                <div className="alert alert-info">Cần quyền <span className="mono">platform.rate-limits.manage</span> để chỉnh sửa chính sách Rate Limit.</div>
              ) : (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    createRateLimitMutation.mutate();
                  }}
                >
                  <div className="form-field">
                    <label className="form-label" htmlFor="rl-name">Tên chính sách</label>
                    <input id="rl-name" className="input mono" placeholder="custom-export-limit" value={rlName} onChange={(e) => setRlName(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="rl-route">Route Pattern</label>
                    <input id="rl-route" className="input mono" value={rlRoute} onChange={(e) => setRlRoute(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="rl-scope">Scope Type</label>
                    <select id="rl-scope" className="select" value={rlScope} onChange={(e) => setRlScope(e.target.value)}>
                      <option value="AnonymousIp">AnonymousIp</option>
                      <option value="HumanSession">HumanSession</option>
                      <option value="ServiceClient">ServiceClient</option>
                      <option value="ApiKey">ApiKey</option>
                      <option value="Expensive">Expensive</option>
                    </select>
                  </div>
                  <div className="grid-2">
                    <div className="form-field">
                      <label className="form-label" htmlFor="rl-permit">Permit Limit</label>
                      <input id="rl-permit" type="number" min={1} className="input" value={rlPermit} onChange={(e) => setRlPermit(e.target.value)} required />
                    </div>
                    <div className="form-field">
                      <label className="form-label" htmlFor="rl-win">Window (giây)</label>
                      <input id="rl-win" type="number" min={1} className="input" value={rlWindow} onChange={(e) => setRlWindow(e.target.value)} required />
                    </div>
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={createRateLimitMutation.isPending}>
                    <Plus size={14} /> {createRateLimitMutation.isPending ? 'Đang tạo...' : 'Tạo chính sách'}
                  </button>
                </form>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'logs' && (
        <div className="panel">
          <div className="table-toolbar" style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
            <input
              type="search"
              className="input"
              style={{ maxWidth: 260 }}
              placeholder="Lọc theo route (/api/v1/...)..."
              aria-label="Lọc theo route"
              value={logRouteFilter}
              onChange={(e) => {
                setLogRouteFilter(e.target.value);
                setLogPage(1);
              }}
            />
            <select
              className="select"
              style={{ maxWidth: 180 }}
              aria-label="Lọc quyết định Rate Limit"
              value={logDecisionFilter}
              onChange={(e) => {
                setLogDecisionFilter(e.target.value);
                setLogPage(1);
              }}
            >
              <option value="">Tất cả Rate Limit</option>
              <option value="Allowed">Allowed</option>
              <option value="Throttled">Throttled (429)</option>
            </select>
            <button type="button" className="btn btn-sm" onClick={() => void apiLogsQuery.refetch()}>
              <RefreshCw size={13} /> Làm mới
            </button>
            <span className="form-hint" style={{ marginLeft: 'auto' }}>
              Tổng: {apiLogsQuery.data?.totalCount ?? 0} bản ghi (tuyệt đối không lưu headers nhạy cảm/body)
            </span>
          </div>
          {apiLogsQuery.isLoading ? (
            <LoadingState />
          ) : apiLogsQuery.error ? (
            <div className="panel-body"><ErrorState error={apiLogsQuery.error} onRetry={() => void apiLogsQuery.refetch()} /></div>
          ) : !apiLogsQuery.data?.items.length ? (
            <EmptyState title="Chưa có nhật ký API khớp bộ lọc" description="Các yêu cầu HTTP tới /api/v1/* sẽ được ghi nhận tại đây." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Method & Route</th>
                  <th>Status</th>
                  <th>Latency</th>
                  <th>Actor</th>
                  <th>Rate Limit</th>
                  <th>Correlation ID</th>
                </tr>
              </thead>
              <tbody>
                {apiLogsQuery.data.items.map((l) => (
                  <tr key={l.id}>
                    <td>{formatDateTime(l.timestamp)}</td>
                    <td className="mono" style={{ fontSize: 12 }}>
                      <strong>{l.method}</strong> {l.normalizedRoute}
                    </td>
                    <td>
                      <Badge variant={l.statusCode >= 500 ? 'danger' : l.statusCode >= 400 ? 'warning' : 'success'}>
                        {l.statusCode}
                      </Badge>
                    </td>
                    <td className="mono">{l.durationMs.toFixed(1)} ms</td>
                    <td><Badge variant="neutral">{l.actorType}</Badge></td>
                    <td>
                      <Badge variant={l.rateLimitDecision === 'Throttled' ? 'danger' : 'success'}>
                        {l.rateLimitDecision}
                      </Badge>
                    </td>
                    <td className="mono" style={{ fontSize: 11.5 }}>{l.correlationId.slice(0, 12)}…</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {activeTab === 'webhooks' && (
        <div className="grid-2">
          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Đăng ký & Danh sách Webhook</h2>
            </div>
            <div className="panel-body">
              {!auth.tenantId ? (
                <div className="alert alert-info" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
                  <span>Webhooks thuộc phạm vi Tenant (<span className="mono">platform.integrations.manage</span>). Chọn ngay một Tenant có sẵn:</span>
                  {auth.accessibleTenants.length > 0 && (
                    <select
                      className="select"
                      style={{ width: 'auto', minWidth: 220 }}
                      aria-label="Chọn nhanh Tenant cho Webhooks"
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
                <>
                  {canManageIntegrations && (
                    <form
                      style={{ marginBottom: 16 }}
                      onSubmit={(e) => {
                        e.preventDefault();
                        registerWebhook.mutate();
                      }}
                    >
                      <div className="form-field">
                        <label className="form-label" htmlFor="wh-event">Loại sự kiện (EventType)</label>
                        <input id="wh-event" className="input mono" value={whEventType} onChange={(e) => setWhEventType(e.target.value)} required />
                      </div>
                      <div className="form-field">
                        <label className="form-label" htmlFor="wh-url">Target URL (bắt buộc HTTPS công khai, chống SSRF)</label>
                        <input id="wh-url" type="url" className="input mono" placeholder="https://hooks.partner.example/nexus" value={whUrl} onChange={(e) => setWhUrl(e.target.value)} required />
                      </div>
                      <div className="form-field">
                        <label className="form-label" htmlFor="wh-secret">Signing Secret Ref (vault:// hoặc env://)</label>
                        <input id="wh-secret" className="input mono" value={whSecretRef} onChange={(e) => setWhSecretRef(e.target.value)} required />
                      </div>
                      <button type="submit" className="btn btn-primary" disabled={registerWebhook.isPending}>
                        {registerWebhook.isPending ? 'Đang đăng ký...' : 'Đăng ký Webhook'}
                      </button>
                    </form>
                  )}

                  {webhooksQuery.isLoading ? (
                    <LoadingState />
                  ) : !webhooksQuery.data?.length ? (
                    <EmptyState title="Chưa có Webhook" description="Đăng ký Webhook HTTPS mới ở biểu mẫu phía trên." />
                  ) : (
                    <table className="data-table">
                      <thead>
                        <tr>
                          <th>Event / Target</th>
                          <th>Status</th>
                          <th>Thao tác</th>
                        </tr>
                      </thead>
                      <tbody>
                        {webhooksQuery.data.map((w) => (
                          <tr key={w.id} style={{ cursor: 'pointer' }} onClick={() => setSelectedWebhook(w)}>
                            <td>
                              <div className="mono" style={{ fontWeight: 600 }}>{w.eventType}</div>
                              <div className="mono form-hint">{w.targetUrl}</div>
                            </td>
                            <td>
                              <Badge variant={w.isActive ? 'success' : 'neutral'}>{w.isActive ? 'Active' : 'Disabled'}</Badge>
                            </td>
                            <td>
                              {canManageIntegrations && (
                                <div style={{ display: 'flex', gap: 4 }} onClick={(e) => e.stopPropagation()}>
                                  <button type="button" className="btn btn-sm" onClick={() => toggleWebhookMutation.mutate({ id: w.id, active: !w.isActive })}>
                                    {w.isActive ? 'Tắt' : 'Bật'}
                                  </button>
                                  <button type="button" className="btn btn-sm" onClick={() => rotateWebhookSecretMutation.mutate(w.id)}>
                                    Rotate
                                  </button>
                                  <button type="button" className="btn btn-sm btn-danger" aria-label="Xóa webhook" onClick={() => setDeleteWebhookId(w.id)}>
                                    <Trash2 size={12} />
                                  </button>
                                </div>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </>
              )}
            </div>
          </div>

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Webhook Delivery History</h2>
            </div>
            <div className="panel-body">
              {!selectedWebhook ? (
                <p className="form-hint">Chọn một Webhook ở bảng bên trái để xem lịch sử giao nhận (deliveries) và chữ ký HMAC-SHA256.</p>
              ) : deliveriesQuery.isLoading ? (
                <LoadingState />
              ) : !deliveriesQuery.data?.length ? (
                <EmptyState title="Chưa có lần gửi nào" description={`Webhook ${selectedWebhook.eventType} chưa phát sinh delivery attempt.`} />
              ) : (
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Status</th>
                      <th>HTTP</th>
                      <th>Attempts</th>
                      <th>Time</th>
                    </tr>
                  </thead>
                  <tbody>
                    {deliveriesQuery.data.map((d) => (
                      <tr key={d.id}>
                        <td><Badge variant={d.status === 'Succeeded' ? 'success' : d.status === 'Failed' ? 'danger' : 'warning'}>{d.status}</Badge></td>
                        <td className="mono">{d.httpStatusCode ?? '—'}</td>
                        <td className="mono">{d.attemptCount}</td>
                        <td>{formatDateTime(d.attemptedAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </div>
      )}

      <Dialog
        open={revealedSecret !== null}
        title={revealedSecret?.title ?? 'Secret'}
        onClose={() => setRevealedSecret(null)}
        footer={
          <button type="button" className="btn btn-primary" onClick={() => setRevealedSecret(null)}>
            Đã sao chép an toàn — Đóng
          </button>
        }
      >
        <div className="alert alert-warning" style={{ marginBottom: 12 }}>
          Secret này được lưu một chiều (hash) trên máy chủ và <strong>không bao giờ hiển thị lại</strong> sau khi bạn đóng hộp thoại này.
        </div>
        <pre className="mono" style={{ padding: 12, background: 'var(--color-surface-subtle)', border: '1px solid var(--color-border)', borderRadius: 4, wordBreak: 'break-all' }}>
          {revealedSecret?.secret}
        </pre>
      </Dialog>

      <ConfirmDialog
        open={revokeClientId !== null}
        title="Thu hồi OAuth 2.0 Service Client"
        message="Mọi access token hiện hành và yêu cầu cấp token mới của client này sẽ bị từ chối ngay lập tức."
        confirmLabel="Thu hồi Client"
        danger
        isPending={revokeClientMutation.isPending}
        onCancel={() => setRevokeClientId(null)}
        onConfirm={() => { if (revokeClientId) revokeClientMutation.mutate(revokeClientId); }}
      />

      <ConfirmDialog
        open={revokeKeyId !== null}
        title="Thu hồi API Key"
        message="API Key này sẽ ngừng hoạt động ngay lập tức và không thể khôi phục."
        confirmLabel="Thu hồi Key"
        danger
        isPending={revokeKeyMutation.isPending}
        onCancel={() => setRevokeKeyId(null)}
        onConfirm={() => { if (revokeKeyId) revokeKeyMutation.mutate(revokeKeyId); }}
      />

      <ConfirmDialog
        open={deleteWebhookId !== null}
        title="Xóa Webhook Registration"
        message="Xác nhận xóa vĩnh viễn webhook này và toàn bộ lịch sử gửi đi liên quan?"
        confirmLabel="Xóa Webhook"
        danger
        isPending={deleteWebhookMutation.isPending}
        onCancel={() => setDeleteWebhookId(null)}
        onConfirm={() => { if (deleteWebhookId) deleteWebhookMutation.mutate(deleteWebhookId); }}
      />
    </div>
  );
}
