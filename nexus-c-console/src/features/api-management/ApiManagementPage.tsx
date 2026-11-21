import { useMemo, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { Play, RefreshCw } from 'lucide-react';
import type { OpenApiOperation } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, ErrorState, LoadingState, NotAvailablePanel, useToast } from '../../components/feedback/Feedback';
import { apiRequest } from '../../services/api/client';
import { integrationsApi, systemApi } from '../../services/api/modules/platform.api';
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
  const { notify } = useToast();

  const activeTab: ApiTab =
    TAB_ROUTES.find((t) => location.pathname === t.path)?.id ??
    TAB_ROUTES.find((t) => t.id !== 'overview' && location.pathname.startsWith(t.path))?.id ??
    'overview';

  const [filter, setFilter] = useState('');
  const [selectedEndpoint, setSelectedEndpoint] = useState<EndpointRow | null>(null);
  const [tryResult, setTryResult] = useState<string | null>(null);
  const [tryLoading, setTryLoading] = useState(false);

  // Webhook form state
  const [whEventType, setWhEventType] = useState('platform.tenant.created.v1');
  const [whUrl, setWhUrl] = useState('');
  const [whSecretRef, setWhSecretRef] = useState('vault://nexus/webhooks/signing-key');

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
        subtitle="Kiểm soát đặc tả OpenAPI thực tế, trạng thái API, Webhooks và tích hợp Gateway của NEXUS C."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'API Management' }]}
        actions={
          <button
            type="button"
            className="btn"
            onClick={() => {
              void healthQuery.refetch();
              void openapiQuery.refetch();
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
        <div>
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
                <span>OpenAPI Specification</span>
                <Badge variant="info">{openapiQuery.data?.openapi ?? '3.x'}</Badge>
              </div>
              <div className="metric-value">{openapiQuery.data?.info?.version ?? 'v1'}</div>
              <div className="metric-meta">{openapiQuery.data?.info?.title ?? '/openapi/v1.json'}</div>
            </div>

            <div className="metric-box">
              <div className="metric-label">
                <span>Registered Endpoints</span>
                <Badge variant="neutral">Live Spec</Badge>
              </div>
              <div className="metric-value">{openapiQuery.isLoading ? '…' : endpoints.length}</div>
              <div className="metric-meta">Đọc trực tiếp từ /openapi/v1.json</div>
            </div>

            <div className="metric-box">
              <div className="metric-label">
                <span>Request Volume / Latency / Error Rate</span>
                <Badge variant="warning">Not Available</Badge>
              </div>
              <div className="metric-value" style={{ fontSize: 15, color: 'var(--color-text-muted)' }}>
                Chưa có Metrics API
              </div>
              <div className="metric-meta">Backend xuất OpenTelemetry OTLP; chưa có REST summary endpoint</div>
            </div>
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
                <p className="form-hint" style={{ marginTop: 8 }}>
                  Lưu ý: Trên môi trường Production, hãy bật <span className="mono">OpenApi:Enabled=true</span> ở mạng nội bộ nếu cần sử dụng API Explorer.
                </p>
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
                              ep.method === 'GET'
                                ? 'info'
                                : ep.method === 'DELETE'
                                  ? 'danger'
                                  : ep.method === 'POST'
                                    ? 'success'
                                    : 'warning'
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
                  Chọn một endpoint ở bảng bên trái để xem tham số, schema và mã phản hồi từ OpenAPI spec thực tế. Các thao tác ghi/xóa (POST/PUT/DELETE) bị khóa thực thi trực tiếp trong Explorer để tránh phá hủy dữ liệu.
                </p>
              ) : (
                <div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
                    <Badge variant="info">{selectedEndpoint.method}</Badge>
                    <span className="mono" style={{ fontWeight: 600 }}>{selectedEndpoint.path}</span>
                  </div>

                  {selectedEndpoint.operation.parameters && selectedEndpoint.operation.parameters.length > 0 && (
                    <div style={{ marginBottom: 12 }}>
                      <div style={{ fontWeight: 600, fontSize: 12.5, marginBottom: 6 }}>Parameters</div>
                      {selectedEndpoint.operation.parameters.map((p) => (
                        <div key={`${p.in}-${p.name}`} className="mono" style={{ fontSize: 12, padding: '2px 0' }}>
                          {p.name} ({p.in}){p.required ? ' *' : ''}
                        </div>
                      ))}
                    </div>
                  )}

                  {selectedEndpoint.operation.responses && (
                    <div style={{ marginBottom: 12 }}>
                      <div style={{ fontWeight: 600, fontSize: 12.5, marginBottom: 6 }}>Responses</div>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        {Object.entries(selectedEndpoint.operation.responses).map(([code, r]) => (
                          <Badge key={code} variant="neutral">{code} {r.description ?? ''}</Badge>
                        ))}
                      </div>
                    </div>
                  )}

                  {tryResult && (
                    <div>
                      <div style={{ fontWeight: 600, fontSize: 12.5, marginBottom: 6 }}>Kết quả GET trực tiếp</div>
                      <pre
                        className="mono"
                        style={{
                          background: 'var(--color-surface-subtle)',
                          border: '1px solid var(--color-border)',
                          borderRadius: 4,
                          padding: 10,
                          maxHeight: 280,
                          overflow: 'auto',
                          margin: 0,
                        }}
                      >
                        {tryResult}
                      </pre>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'clients' && (
        <NotAvailablePanel
          title="API Clients Management"
          endpoint="GET/POST /api/v1/platform/api-clients"
          description="NEXUS C Backend Foundation hiện chưa triển khai bảng và API quản lý OAuth/Machine API Clients. Giao diện sẽ được kích hoạt ngay khi backend bổ sung contract tương ứng."
        />
      )}

      {activeTab === 'keys' && (
        <NotAvailablePanel
          title="API Keys Management"
          endpoint="GET/POST/DELETE /api/v1/platform/api-keys"
          description="Backend chưa cung cấp endpoint tạo, xoay vòng hoặc thu hồi API Key. Theo quy tắc bảo mật NEXUS, frontend tuyệt đối không tự sinh khóa giả hay lưu API secret vào localStorage."
        />
      )}

      {activeTab === 'rate-limits' && (
        <NotAvailablePanel
          title="Dynamic Rate Limiting Policies"
          endpoint="GET/PUT /api/v1/platform/rate-limits"
          description="NEXUS C Backend hiện áp dụng giới hạn lưu lượng qua cấu hình tĩnh (RateLimiting:PermitLimit & AuthPermitLimit). API quản lý chính sách động theo từng client chưa được triển khai."
        />
      )}

      {activeTab === 'logs' && (
        <NotAvailablePanel
          title="API Request Telemetry Logs"
          endpoint="GET /api/v1/platform/api-logs"
          description="NEXUS C Backend hiện ghi log HTTP có cấu trúc qua Serilog và OpenTelemetry (đã lọc bỏ mật khẩu, Authorization header và token). Endpoint truy vấn API logs trực tiếp qua REST chưa được triển khai."
        />
      )}

      {activeTab === 'webhooks' && (
        <div className="grid-2">
          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Đăng ký Webhook mới (Backend Live)</h2>
            </div>
            <div className="panel-body">
              {!auth.tenantId ? (
                <div className="alert alert-info">
                  Đăng ký Webhook thuộc phạm vi Tenant (<span className="mono">platform.integrations.manage</span>). Hãy chọn một Tenant ở thanh tiêu đề.
                </div>
              ) : (
                <form
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
                    <label className="form-label" htmlFor="wh-url">Target URL (bắt buộc HTTPS)</label>
                    <input id="wh-url" type="url" className="input mono" placeholder="https://hooks.partner.example/nexus" value={whUrl} onChange={(e) => setWhUrl(e.target.value)} required />
                  </div>
                  <div className="form-field">
                    <label className="form-label" htmlFor="wh-secret">Signing Secret Reference (vault:// hoặc env://)</label>
                    <input id="wh-secret" className="input mono" value={whSecretRef} onChange={(e) => setWhSecretRef(e.target.value)} required />
                    <span className="form-hint">Không nhập secret thô; backend chỉ chấp nhận tham chiếu vault:// hoặc env://.</span>
                  </div>
                  <button type="submit" className="btn btn-primary" disabled={registerWebhook.isPending}>
                    {registerWebhook.isPending ? 'Đang đăng ký...' : 'Đăng ký Webhook'}
                  </button>
                </form>
              )}
            </div>
          </div>

          <NotAvailablePanel
            title="Webhook List & Delivery History"
            endpoint="GET /api/v1/platform/integrations/webhooks"
            description="Backend đã hỗ trợ đăng ký webhook (POST /api/v1/platform/integrations/webhooks), nhưng chưa cung cấp endpoint liệt kê danh sách webhook và lịch sử gửi (delivery history)."
          />
        </div>
      )}
    </div>
  );
}
