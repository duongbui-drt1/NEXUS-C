import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, RefreshCw, Trash2 } from 'lucide-react';
import type { PlanDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog, Dialog } from '../../components/dialogs/Dialog';
import { SearchableSelect } from '../../components/forms/SearchableSelect';
import { referenceDataApi, subscriptionsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

export default function SubscriptionsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [assignOpen, setAssignOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<PlanDto | null>(null);
  const [deleteReason, setDeleteReason] = useState('');

  // Plan form
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [desc, setDesc] = useState('');
  const [price, setPrice] = useState('199.00');
  const [currency, setCurrency] = useState('USD');
  const [enableF, setEnableF] = useState(true);
  const [enableE, setEnableE] = useState(false);
  const [enableM, setEnableM] = useState(false);
  const [enableAi, setEnableAi] = useState(false);

  // Assign form
  const [targetTenantId, setTargetTenantId] = useState('');
  const [targetPlanId, setTargetPlanId] = useState('');
  const [days, setDays] = useState('365');

  const plansQuery = useQuery({
    queryKey: queryKeys.plans,
    queryFn: ({ signal }) => subscriptionsApi.listPlans(signal),
  });

  const currentSubQuery = useQuery({
    queryKey: queryKeys.currentSubscription,
    queryFn: ({ signal }) => subscriptionsApi.getCurrent(signal),
    enabled: Boolean(auth.tenantId),
    retry: false,
  });

  const fxRatesQuery = useQuery({
    queryKey: queryKeys.fxRates('USD'),
    queryFn: ({ signal }) => referenceDataApi.getFxRates('USD', signal),
    staleTime: 60_000,
    retry: false,
  });

  const createPlan = useMutation({
    mutationFn: () =>
      subscriptionsApi.createPlan({
        code,
        name,
        description: desc,
        monthlyPrice: Number(price),
        currency: currency || 'USD',
        features: [
          { featureCode: 'NEXUS_F', isEnabled: enableF, quotaLimit: enableF ? 1000 : 0 },
          { featureCode: 'NEXUS_E', isEnabled: enableE, quotaLimit: enableE ? 1000 : 0 },
          { featureCode: 'NEXUS_M', isEnabled: enableM, quotaLimit: enableM ? 100 : 0 },
          { featureCode: 'AI_GATEWAY', isEnabled: enableAi, quotaLimit: enableAi ? 50000 : 0 },
        ],
      }),
    onSuccess: (p) => {
      notify(`Đã tạo gói ${p.code}.`, 'success');
      setCreateOpen(false);
      setCode('');
      setName('');
      setDesc('');
      void qc.invalidateQueries({ queryKey: queryKeys.plans });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const deletePlanMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason?: string }) => subscriptionsApi.deleteOrArchivePlan(id, reason),
    onSuccess: (res) => {
      notify(res.message, res.outcome === 'HardDeleted' ? 'success' : 'info');
      setDeleteTarget(null);
      setDeleteReason('');
      void qc.invalidateQueries({ queryKey: queryKeys.plans });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const refreshFxMutation = useMutation({
    mutationFn: () => referenceDataApi.refreshFxRates('USD'),
    onSuccess: (res) => {
      notify(`Đã cập nhật ${res.ratesUpdated} tỷ giá tham chiếu từ ${res.provider}.`, 'success');
      void qc.invalidateQueries({ queryKey: queryKeys.fxRates('USD') });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const assignPlan = useMutation({
    mutationFn: () => {
      const now = new Date();
      const ends = new Date(now.getTime() + Number(days) * 86_400_000);
      return subscriptionsApi.assignPlan({
        tenantId: targetTenantId.trim(),
        planId: targetPlanId,
        startsAt: now.toISOString(),
        endsAt: ends.toISOString(),
        seatCount: 25,
      });
    },
    onSuccess: () => {
      notify('Đã gán gói thuê bao cho tenant.', 'success');
      setAssignOpen(false);
      void qc.invalidateQueries({ queryKey: queryKeys.currentSubscription });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const canManage = auth.hasPermission('platform.subscriptions.manage');
  const canDeletePlan = auth.hasPermission('platform.subscriptions.delete') || canManage;
  const canRefreshFx = auth.hasPermission('platform.reference-data.manage');

  return (
    <div>
      <PageHeader
        title="Gói Thuê bao, Tiền tệ & Entitlements"
        subtitle="Quản lý gói SaaS, xóa/lưu trữ an toàn không làm gãy lịch sử thuê bao, danh mục tiền tệ ISO 4217 và tỷ giá FX tham chiếu."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Subscriptions' }]}
        actions={
          canManage ? (
            <>
              <button type="button" className="btn" onClick={() => setAssignOpen(true)}>
                Gán gói cho Tenant
              </button>
              <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
                <Plus size={15} /> Tạo gói SaaS
              </button>
            </>
          ) : undefined
        }
      />

      {auth.tenantId && currentSubQuery.data && (
        <div className="panel" style={{ marginBottom: 16 }}>
          <div className="panel-header">
            <h2 className="panel-title">Thuê bao hiện tại của Tenant</h2>
            <Badge variant={statusToBadgeVariant(currentSubQuery.data.status)}>{currentSubQuery.data.status}</Badge>
          </div>
          <div className="panel-body">
            <div style={{ marginBottom: 10, fontSize: 13 }}>
              Hiệu lực: <strong>{formatDateTime(currentSubQuery.data.startsAt)}</strong> →{' '}
              <strong>{formatDateTime(currentSubQuery.data.endsAt)}</strong>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {currentSubQuery.data.entitlements.map((e) => (
                <Badge key={e.featureCode} variant={e.isEnabled ? 'success' : 'neutral'}>
                  {e.featureCode}: {e.isEnabled ? 'Enabled' : 'Disabled'}
                </Badge>
              ))}
            </div>
          </div>
        </div>
      )}

      <DataTable
        columns={[
          { key: 'code', header: 'Mã gói', render: (p) => <span className="mono">{p.code}</span> },
          {
            key: 'name',
            header: 'Tên gói',
            render: (p) => (
              <div>
                <div style={{ fontWeight: 500, display: 'flex', gap: 6, alignItems: 'center' }}>
                  <span>{p.name}</span>
                  {(!p.isActive || p.archivedAt) && <Badge variant="warning">Archived</Badge>}
                </div>
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{p.description}</div>
              </div>
            ),
          },
          { key: 'price', header: 'Giá / tháng', render: (p) => <span className="tabular">{p.monthlyPrice.toFixed(2)} {p.currency}</span> },
          {
            key: 'features',
            header: 'Tính năng đi kèm',
            render: (p) => (
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {p.features.filter((f) => f.isEnabled).map((f) => (
                  <Badge key={f.featureCode} variant="info">{f.featureCode}</Badge>
                ))}
              </div>
            ),
          },
          {
            key: 'actions',
            header: 'Thao tác',
            render: (p) =>
              canDeletePlan && p.isActive ? (
                <button type="button" className="btn btn-sm btn-danger" onClick={() => setDeleteTarget(p)}>
                  <Trash2 size={12} /> Xóa / Lưu trữ
                </button>
              ) : (
                <span className="form-hint">—</span>
              ),
          },
        ]}
        rows={plansQuery.data ?? []}
        rowKey={(p) => p.id}
        isLoading={plansQuery.isLoading}
        error={plansQuery.error}
        onRetry={() => void plansQuery.refetch()}
      />

      <div className="panel" style={{ marginTop: 16 }}>
        <div className="panel-header">
          <div>
            <h2 className="panel-title">Tỷ giá FX Tham chiếu (Base: USD)</h2>
            <div className="form-hint">Indicative FX reference rate — not a settlement rate.</div>
          </div>
          {canRefreshFx && (
            <button
              type="button"
              className="btn btn-sm"
              disabled={refreshFxMutation.isPending}
              onClick={() => refreshFxMutation.mutate()}
            >
              <RefreshCw size={13} /> {refreshFxMutation.isPending ? 'Đang cập nhật...' : 'Làm mới tỷ giá (ECB)'}
            </button>
          )}
        </div>
        <div className="panel-body">
          {!fxRatesQuery.data?.length ? (
            <div className="form-hint">Chưa có quan sát tỷ giá FX nào trong hệ thống. Bấm &quot;Làm mới tỷ giá (ECB)&quot; để nạp từ Frankfurter ECB.</div>
          ) : (
            <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
              {fxRatesQuery.data.slice(0, 12).map((r) => (
                <div key={r.id} style={{ border: '1px solid var(--color-border)', borderRadius: 6, padding: '8px 12px', minWidth: 160 }}>
                  <div className="mono" style={{ fontWeight: 600 }}>1 {r.baseCurrency} = {r.rate} {r.quoteCurrency}</div>
                  <div style={{ display: 'flex', gap: 6, marginTop: 4, alignItems: 'center' }}>
                    <Badge variant={r.isStale ? 'warning' : 'success'}>{r.isStale ? 'Stale' : 'Fresh'}</Badge>
                    <span className="form-hint">{r.provider}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      <Dialog
        open={createOpen}
        title="Tạo gói SaaS mới"
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button type="button" className="btn btn-primary" disabled={createPlan.isPending} onClick={() => createPlan.mutate()}>
              Tạo gói
            </button>
          </>
        }
      >
        <div className="form-field">
          <label className="form-label" htmlFor="pl-code">Mã gói</label>
          <input id="pl-code" className="input mono" placeholder="ENTERPRISE" value={code} onChange={(e) => setCode(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="pl-name">Tên gói</label>
          <input id="pl-name" className="input" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="pl-desc">Mô tả</label>
          <input id="pl-desc" className="input" value={desc} onChange={(e) => setDesc(e.target.value)} />
        </div>
        <div className="grid-2">
          <div className="form-field">
            <label className="form-label" htmlFor="pl-price">Giá tháng</label>
            <input id="pl-price" type="number" step="0.01" className="input" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>
          <SearchableSelect id="pl-cur" label="Tiền tệ (ISO 4217)" source="currency" value={currency} onChange={setCurrency} />
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
          <label><input type="checkbox" checked={enableF} onChange={(e) => setEnableF(e.target.checked)} /> NEXUS_F</label>
          <label><input type="checkbox" checked={enableE} onChange={(e) => setEnableE(e.target.checked)} /> NEXUS_E</label>
          <label><input type="checkbox" checked={enableM} onChange={(e) => setEnableM(e.target.checked)} /> NEXUS_M</label>
          <label><input type="checkbox" checked={enableAi} onChange={(e) => setEnableAi(e.target.checked)} /> AI_GATEWAY</label>
        </div>
      </Dialog>

      <Dialog
        open={assignOpen}
        title="Gán gói thuê bao cho Tenant"
        onClose={() => setAssignOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setAssignOpen(false)}>Hủy</button>
            <button type="button" className="btn btn-primary" disabled={assignPlan.isPending} onClick={() => assignPlan.mutate()}>
              Áp dụng
            </button>
          </>
        }
      >
        <div className="form-field">
          <label className="form-label" htmlFor="as-tid">Tenant UUID</label>
          <input id="as-tid" className="input mono" value={targetTenantId} onChange={(e) => setTargetTenantId(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="as-pid">Chọn gói SaaS (chỉ gói đang hoạt động)</label>
          <select id="as-pid" className="select" value={targetPlanId} onChange={(e) => setTargetPlanId(e.target.value)}>
            <option value="">-- Chọn gói --</option>
            {(plansQuery.data ?? []).filter((p) => p.isActive).map((p) => (
              <option key={p.id} value={p.id}>{p.code} — {p.name}</option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="as-days">Thời hạn (ngày)</label>
          <input id="as-days" type="number" className="input" value={days} onChange={(e) => setDays(e.target.value)} />
        </div>
      </Dialog>

      <ConfirmDialog
        open={deleteTarget !== null}
        title={`Xóa / Lưu trữ gói ${deleteTarget?.code ?? ''}`}
        message="Nếu gói chưa có thuê bao nào tham chiếu, hệ thống sẽ xóa vĩnh viễn. Nếu đã có lịch sử thuê bao, gói sẽ được chuyển sang trạng thái Archived an toàn."
        confirmLabel="Xác nhận"
        danger
        isPending={deletePlanMutation.isPending}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget) {
            deletePlanMutation.mutate({ id: deleteTarget.id, reason: deleteReason.trim() || undefined });
          }
        }}
      >
        <div className="form-field" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="del-plan-reason">Lý do (tùy chọn)</label>
          <input id="del-plan-reason" className="input" value={deleteReason} onChange={(e) => setDeleteReason(e.target.value)} />
        </div>
      </ConfirmDialog>
    </div>
  );
}
