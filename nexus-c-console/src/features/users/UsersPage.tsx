import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, RefreshCw } from 'lucide-react';
import type { UserDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog, Dialog } from '../../components/dialogs/Dialog';
import { usersApi } from '../../services/api/modules/users.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

export default function UsersPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [roleName, setRoleName] = useState('Employee');
  const [isPlatformUser, setIsPlatformUser] = useState(false);

  const [disableTarget, setDisableTarget] = useState<UserDto | null>(null);
  const [disableReason, setDisableReason] = useState('');

  const params = { page, pageSize: 15, search: search.trim() || undefined };
  const usersQuery = useQuery({
    queryKey: queryKeys.users(params),
    queryFn: ({ signal }) => usersApi.list(params, signal),
  });

  const createMutation = useMutation({
    mutationFn: () =>
      usersApi.create({
        email: email.trim(),
        password,
        fullName: fullName.trim(),
        tenantId: auth.tenantId,
        roleName: roleName.trim() || undefined,
        isPlatformUser: auth.isPlatformUser && isPlatformUser,
      }),
    onSuccess: (u) => {
      notify(`Đã tạo người dùng ${u.email}.`, 'success');
      setCreateOpen(false);
      setEmail('');
      setPassword('');
      setFullName('');
      void qc.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const disableMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => usersApi.disable(id, reason),
    onSuccess: () => {
      notify('Đã vô hiệu hóa tài khoản và thu hồi toàn bộ phiên đăng nhập.', 'success');
      setDisableTarget(null);
      setDisableReason('');
      void qc.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const enableMutation = useMutation({
    mutationFn: (id: string) => usersApi.enable(id),
    onSuccess: () => {
      notify('Đã kích hoạt lại tài khoản.', 'success');
      void qc.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const canCreate = auth.hasPermission('platform.users.create');
  const canManage = auth.hasPermission('platform.users.manage');

  return (
    <div>
      <PageHeader
        title="Quản lý Người dùng"
        subtitle="Danh mục tài khoản định danh, trạng thái khóa và vô hiệu hóa phiên truy cập."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Core' }, { label: 'Users' }]}
        actions={
          <>
            <button type="button" className="btn" onClick={() => void usersQuery.refetch()}>
              <RefreshCw size={14} /> Làm mới
            </button>
            {canCreate && (
              <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
                <Plus size={15} /> Tạo người dùng
              </button>
            )}
          </>
        }
      />

      <DataTable
        columns={[
          {
            key: 'email',
            header: 'Người dùng',
            render: (u) => (
              <div>
                <div style={{ fontWeight: 500 }}>{u.fullName}</div>
                <div className="mono" style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                  {u.email}
                </div>
              </div>
            ),
          },
          {
            key: 'scope',
            header: 'Phạm vi',
            render: (u) => <Badge variant={u.isPlatformUser ? 'info' : 'neutral'}>{u.isPlatformUser ? 'Platform' : 'Tenant'}</Badge>,
          },
          {
            key: 'status',
            header: 'Trạng thái',
            render: (u) =>
              !u.isActive ? (
                <Badge variant="danger">Disabled</Badge>
              ) : u.isLockedOut ? (
                <Badge variant="warning">Locked Out</Badge>
              ) : (
                <Badge variant="success">Active</Badge>
              ),
          },
          { key: 'createdAt', header: 'Ngày tạo', render: (u) => formatDateTime(u.createdAt) },
          {
            key: 'actions',
            header: 'Thao tác',
            render: (u) => (
              <div style={{ display: 'flex', gap: 6 }}>
                {canManage && u.isActive && u.id !== auth.userId && (
                  <button type="button" className="btn btn-sm btn-danger" onClick={() => setDisableTarget(u)}>
                    Vô hiệu hóa
                  </button>
                )}
                {canManage && (!u.isActive || u.isLockedOut) && (
                  <button type="button" className="btn btn-sm btn-primary" onClick={() => enableMutation.mutate(u.id)}>
                    Mở khóa
                  </button>
                )}
              </div>
            ),
          },
        ]}
        rows={usersQuery.data?.items ?? []}
        rowKey={(u) => u.id}
        isLoading={usersQuery.isLoading}
        error={usersQuery.error}
        onRetry={() => void usersQuery.refetch()}
        toolbar={
          <input
            type="search"
            className="input"
            style={{ maxWidth: 300 }}
            placeholder="Tìm theo email hoặc họ tên..."
            aria-label="Tìm kiếm người dùng"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        }
        pagination={usersQuery.data ? { ...usersQuery.data, onPageChange: setPage } : undefined}
      />

      <Dialog
        open={createOpen}
        title="Tạo người dùng mới"
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button type="button" className="btn btn-primary" disabled={createMutation.isPending} onClick={() => createMutation.mutate()}>
              {createMutation.isPending ? 'Đang tạo...' : 'Tạo tài khoản'}
            </button>
          </>
        }
      >
        <div className="form-field">
          <label className="form-label" htmlFor="u-name">Họ và tên</label>
          <input id="u-name" className="input" value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="u-email">Email</label>
          <input id="u-email" type="email" className="input" value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="u-pass">Mật khẩu khởi tạo (≥ 10 ký tự, hoa/thường/số/đặc biệt)</label>
          <input id="u-pass" type="password" className="input" value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="u-role">Vai trò gán mặc định</label>
          <select id="u-role" className="select" value={roleName} onChange={(e) => setRoleName(e.target.value)}>
            <option value="Employee">Employee</option>
            <option value="TenantAdmin">TenantAdmin</option>
            <option value="TenantOwner">TenantOwner</option>
            <option value="CFO">CFO</option>
            <option value="Accountant">Accountant</option>
            <option value="Auditor">Auditor</option>
            {auth.isPlatformUser && <option value="PlatformSupport">PlatformSupport</option>}
            {auth.isPlatformUser && <option value="PlatformSuperAdmin">PlatformSuperAdmin</option>}
          </select>
        </div>
        {auth.isPlatformUser && (
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
            <input type="checkbox" checked={isPlatformUser} onChange={(e) => setIsPlatformUser(e.target.checked)} />
            Tài khoản quản trị cấp Platform
          </label>
        )}
      </Dialog>

      <ConfirmDialog
        open={disableTarget !== null}
        title={`Vô hiệu hóa ${disableTarget?.email ?? ''}`}
        message="Tài khoản sẽ bị khóa ngay lập tức và toàn bộ phiên đăng nhập hiện hành sẽ bị thu hồi."
        confirmLabel="Vô hiệu hóa"
        danger
        isPending={disableMutation.isPending}
        onCancel={() => setDisableTarget(null)}
        onConfirm={() => {
          if (disableTarget && disableReason.trim()) {
            disableMutation.mutate({ id: disableTarget.id, reason: disableReason.trim() });
          } else {
            notify('Vui lòng nhập lý do vô hiệu hóa.', 'danger');
          }
        }}
      >
        <div className="form-field" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="dis-reason">Lý do</label>
          <input id="dis-reason" className="input" value={disableReason} onChange={(e) => setDisableReason(e.target.value)} />
        </div>
      </ConfirmDialog>
    </div>
  );
}
