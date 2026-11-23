import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Plus, RefreshCw } from 'lucide-react';
import type { TenantDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import type { ColumnDef } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog, Dialog } from '../../components/dialogs/Dialog';
import { tenantsApi } from '../../services/api/modules/tenants.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

const createTenantSchema = z.object({
  code: z.string().min(2, 'Tối thiểu 2 ký tự').max(32, 'Tối đa 32 ký tự'),
  name: z.string().min(2, 'Tên tenant là bắt buộc').max(200),
  slug: z.string().min(2, 'Slug là bắt buộc').max(100),
  timeZone: z.string().min(1),
  defaultCurrency: z.string().length(3, 'Mã tiền tệ ISO 3 ký tự (vd: VND, USD)'),
  countryCode: z.string().length(2, 'Mã quốc gia ISO 2 ký tự (vd: VN, US)'),
});

type CreateTenantForm = z.infer<typeof createTenantSchema>;

export default function TenantsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editTenant, setEditTenant] = useState<TenantDto | null>(null);
  const [editName, setEditName] = useState('');
  const [editTz, setEditTz] = useState('');
  const [editCurrency, setEditCurrency] = useState('');
  const [editCountry, setEditCountry] = useState('');
  const [suspendTarget, setSuspendTarget] = useState<TenantDto | null>(null);
  const [suspendReason, setSuspendReason] = useState('');

  const queryParams = { page, pageSize: 15, search: search.trim() || undefined, status: statusFilter || undefined };

  const listQuery = useQuery({
    queryKey: queryKeys.tenants(queryParams),
    queryFn: ({ signal }) => tenantsApi.list(queryParams, signal),
  });

  const form = useForm<CreateTenantForm>({
    resolver: zodResolver(createTenantSchema),
    defaultValues: {
      code: '',
      name: '',
      slug: '',
      timeZone: 'Asia/Ho_Chi_Minh',
      defaultCurrency: 'VND',
      countryCode: 'VN',
    },
  });

  const createMutation = useMutation({
    mutationFn: (values: CreateTenantForm) => tenantsApi.create(values),
    onSuccess: (created) => {
      notify(`Đã tạo tenant ${created.code}.`, 'success');
      setCreateOpen(false);
      form.reset();
      void qc.invalidateQueries({ queryKey: ['tenants'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const updateMutation = useMutation({
    mutationFn: (tenant: TenantDto) =>
      tenantsApi.update(tenant.id, {
        name: editName.trim(),
        timeZone: editTz.trim(),
        defaultCurrency: editCurrency.trim().toUpperCase(),
        countryCode: editCountry.trim().toUpperCase(),
        expectedRowVersion: tenant.rowVersion,
      }),
    onSuccess: () => {
      notify('Cập nhật tenant thành công.', 'success');
      setEditTenant(null);
      void qc.invalidateQueries({ queryKey: ['tenants'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const suspendMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => tenantsApi.suspend(id, reason),
    onSuccess: () => {
      notify('Đã tạm ngưng tenant.', 'success');
      setSuspendTarget(null);
      setSuspendReason('');
      void qc.invalidateQueries({ queryKey: ['tenants'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const activateMutation = useMutation({
    mutationFn: (id: string) => tenantsApi.activate(id),
    onSuccess: () => {
      notify('Đã kích hoạt lại tenant.', 'success');
      void qc.invalidateQueries({ queryKey: ['tenants'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const canCreate = auth.hasPermission('platform.tenants.create');
  const canUpdate = auth.hasPermission('platform.tenants.update');
  const canSuspend = auth.hasPermission('platform.tenants.suspend');

  const columns: ColumnDef<TenantDto>[] = [
    { key: 'code', header: 'Mã Tenant', render: (t) => <span className="mono">{t.code}</span> },
    {
      key: 'name',
      header: 'Tên tổ chức',
      render: (t) => (
        <div>
          <div style={{ fontWeight: 500 }}>{t.name}</div>
          <div className="mono" style={{ fontSize: 11.5, color: 'var(--color-text-muted)' }}>
            {t.slug}
          </div>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Trạng thái',
      render: (t) => <Badge variant={statusToBadgeVariant(t.status)}>{t.status}</Badge>,
    },
    {
      key: 'locale',
      header: 'Khu vực / Tiền tệ',
      render: (t) => `${t.countryCode} · ${t.defaultCurrency} (${t.timeZone})`,
    },
    { key: 'createdAt', header: 'Ngày tạo', render: (t) => formatDateTime(t.createdAt) },
    {
      key: 'actions',
      header: 'Thao tác',
      render: (t) => (
        <div style={{ display: 'flex', gap: 6 }}>
          {t.status === 'Active' && (
            <button
              type="button"
              className="btn btn-sm"
              disabled={auth.tenantId === t.id}
              onClick={() => void auth.switchTenant(t.id)}
            >
              {auth.tenantId === t.id ? 'Đang chọn' : 'Chọn ngữ cảnh'}
            </button>
          )}
          {canUpdate && (
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => {
                setEditTenant(t);
                setEditName(t.name);
                setEditTz(t.timeZone);
                setEditCurrency(t.defaultCurrency);
                setEditCountry(t.countryCode);
              }}
            >
              Sửa
            </button>
          )}
          {canSuspend && t.status === 'Active' && (
            <button
              type="button"
              className="btn btn-sm btn-danger"
              onClick={() => {
                setSuspendTarget(t);
                setSuspendReason('');
              }}
            >
              Tạm ngưng
            </button>
          )}
          {canSuspend && t.status === 'Suspended' && (
            <button
              type="button"
              className="btn btn-sm btn-primary"
              disabled={activateMutation.isPending}
              onClick={() => activateMutation.mutate(t.id)}
            >
              Kích hoạt
            </button>
          )}
        </div>
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title="Quản lý Tenants"
        subtitle="Danh sách tổ chức đa khách hàng trên nền tảng NEXUS C (phân trang & tìm kiếm phía máy chủ)."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Core' }, { label: 'Tenants' }]}
        actions={
          <>
            <button type="button" className="btn" onClick={() => void listQuery.refetch()}>
              <RefreshCw size={14} /> Làm mới
            </button>
            {canCreate && (
              <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
                <Plus size={15} /> Tạo Tenant
              </button>
            )}
          </>
        }
      />

      <DataTable
        columns={columns}
        rows={listQuery.data?.items ?? []}
        rowKey={(t) => t.id}
        isLoading={listQuery.isLoading}
        error={listQuery.error}
        onRetry={() => void listQuery.refetch()}
        emptyTitle="Chưa có tenant nào"
        emptyDescription="Tạo tenant mới hoặc thay đổi bộ lọc tìm kiếm."
        toolbar={
          <>
            <input
              type="search"
              className="input"
              style={{ maxWidth: 280 }}
              placeholder="Tìm theo mã, tên hoặc slug..."
              aria-label="Tìm kiếm tenant"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
            />
            <select
              className="select"
              style={{ width: 170 }}
              aria-label="Lọc theo trạng thái"
              value={statusFilter}
              onChange={(e) => {
                setStatusFilter(e.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả trạng thái</option>
              <option value="Active">Active</option>
              <option value="Suspended">Suspended</option>
              <option value="Archived">Archived</option>
            </select>
          </>
        }
        pagination={
          listQuery.data
            ? {
                page: listQuery.data.page,
                pageSize: listQuery.data.pageSize,
                totalCount: listQuery.data.totalCount,
                totalPages: listQuery.data.totalPages,
                onPageChange: setPage,
              }
            : undefined
        }
      />

      {/* Create Tenant Dialog */}
      <Dialog
        open={createOpen}
        title="Tạo Tenant mới"
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button
              type="submit"
              form="create-tenant-form"
              className="btn btn-primary"
              disabled={createMutation.isPending}
            >
              {createMutation.isPending ? 'Đang tạo...' : 'Tạo Tenant'}
            </button>
          </>
        }
      >
        <form id="create-tenant-form" onSubmit={form.handleSubmit((v) => createMutation.mutate(v))}>
          <div className="form-field">
            <label className="form-label" htmlFor="t-code">Mã Tenant</label>
            <input id="t-code" className="input mono" placeholder="ACME01" {...form.register('code')} />
            {form.formState.errors.code && <span className="form-error">{form.formState.errors.code.message}</span>}
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="t-name">Tên tổ chức</label>
            <input id="t-name" className="input" placeholder="Công ty Cổ phần ACME" {...form.register('name')} />
            {form.formState.errors.name && <span className="form-error">{form.formState.errors.name.message}</span>}
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="t-slug">Slug</label>
            <input id="t-slug" className="input mono" placeholder="acme-corp" {...form.register('slug')} />
            {form.formState.errors.slug && <span className="form-error">{form.formState.errors.slug.message}</span>}
          </div>
          <div className="grid-2">
            <div className="form-field">
              <label className="form-label" htmlFor="t-currency">Tiền tệ (ISO 3)</label>
              <input id="t-currency" className="input mono" {...form.register('defaultCurrency')} />
              {form.formState.errors.defaultCurrency && (
                <span className="form-error">{form.formState.errors.defaultCurrency.message}</span>
              )}
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="t-country">Quốc gia (ISO 2)</label>
              <input id="t-country" className="input mono" {...form.register('countryCode')} />
              {form.formState.errors.countryCode && (
                <span className="form-error">{form.formState.errors.countryCode.message}</span>
              )}
            </div>
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="t-tz">Múi giờ (IANA)</label>
            <input id="t-tz" className="input" {...form.register('timeZone')} />
          </div>
        </form>
      </Dialog>

      {/* Edit Tenant Dialog */}
      <Dialog
        open={editTenant !== null}
        title={editTenant ? `Cập nhật Tenant ${editTenant.code}` : ''}
        onClose={() => setEditTenant(null)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setEditTenant(null)}>Hủy</button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={updateMutation.isPending}
              onClick={() => {
                if (editTenant) updateMutation.mutate(editTenant);
              }}
            >
              {updateMutation.isPending ? 'Đang lưu...' : 'Lưu thay đổi'}
            </button>
          </>
        }
      >
        <div className="form-field">
          <label className="form-label" htmlFor="edit-t-name">Tên tổ chức</label>
          <input id="edit-t-name" className="input" value={editName} onChange={(e) => setEditName(e.target.value)} />
        </div>
        <div className="grid-2">
          <div className="form-field">
            <label className="form-label" htmlFor="edit-t-cur">Tiền tệ</label>
            <input id="edit-t-cur" className="input mono" value={editCurrency} onChange={(e) => setEditCurrency(e.target.value)} />
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="edit-t-cc">Quốc gia</label>
            <input id="edit-t-cc" className="input mono" value={editCountry} onChange={(e) => setEditCountry(e.target.value)} />
          </div>
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="edit-t-tz">Múi giờ</label>
          <input id="edit-t-tz" className="input" value={editTz} onChange={(e) => setEditTz(e.target.value)} />
        </div>
      </Dialog>

      {/* Suspend Confirmation Dialog */}
      <ConfirmDialog
        open={suspendTarget !== null}
        title={`Tạm ngưng Tenant ${suspendTarget?.code ?? ''}`}
        message="Khi tạm ngưng, toàn bộ yêu cầu nghiệp vụ của tenant này sẽ bị backend từ chối với mã TENANT_SUSPENDED."
        confirmLabel="Xác nhận tạm ngưng"
        danger
        isPending={suspendMutation.isPending}
        onCancel={() => setSuspendTarget(null)}
        onConfirm={() => {
          if (suspendTarget && suspendReason.trim()) {
            suspendMutation.mutate({ id: suspendTarget.id, reason: suspendReason.trim() });
          } else {
            notify('Vui lòng nhập lý do tạm ngưng.', 'danger');
          }
        }}
      >
        <div className="form-field" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="suspend-reason">Lý do tạm ngưng (bắt buộc cho Audit log)</label>
          <input
            id="suspend-reason"
            className="input"
            placeholder="Ví dụ: Vi phạm chính sách / quá hạn thanh toán"
            value={suspendReason}
            onChange={(e) => setSuspendReason(e.target.value)}
          />
        </div>
      </ConfirmDialog>
    </div>
  );
}
