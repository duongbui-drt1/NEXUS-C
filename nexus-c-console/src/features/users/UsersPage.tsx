import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, RefreshCw, ShieldCheck, UserCog } from 'lucide-react';
import type { UserDto } from '../../types/api';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog, Dialog } from '../../components/dialogs/Dialog';
import { SearchableSelect } from '../../components/forms/SearchableSelect';
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

  // Safe User Detail & Profile Editor state (strictly zero role/permission fields!)
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [profFullName, setProfFullName] = useState('');
  const [profFirstName, setProfFirstName] = useState('');
  const [profLastName, setProfLastName] = useState('');
  const [profPhone, setProfPhone] = useState('');
  const [profJobTitle, setProfJobTitle] = useState('');
  const [profCountry, setProfCountry] = useState('');
  const [profTimeZone, setProfTimeZone] = useState('');
  const [profLocale, setProfLocale] = useState('');
  const [profNotes, setProfNotes] = useState('');

  const params = { page, pageSize: 15, search: search.trim() || undefined };
  const usersQuery = useQuery({
    queryKey: queryKeys.users(params),
    queryFn: ({ signal }) => usersApi.list(params, signal),
  });

  const clerkStatusQuery = useQuery({
    queryKey: queryKeys.clerkSyncStatus,
    queryFn: ({ signal }) => usersApi.getClerkSyncStatus(signal),
    enabled: auth.isPlatformUser && auth.hasPermission('platform.users.manage'),
    retry: false,
  });

  const detailQuery = useQuery({
    queryKey: queryKeys.userDetail(selectedUserId ?? ''),
    queryFn: async ({ signal }) => {
      const d = await usersApi.getDetail(selectedUserId!, signal);
      setProfFullName(d.fullName ?? '');
      setProfFirstName(d.firstName ?? '');
      setProfLastName(d.lastName ?? '');
      setProfPhone(d.phoneNumber ?? '');
      setProfJobTitle(d.jobTitle ?? '');
      setProfCountry(d.countryCode ?? '');
      setProfTimeZone(d.timeZone ?? '');
      setProfLocale(d.locale ?? '');
      setProfNotes(d.adminNotes ?? '');
      return d;
    },
    enabled: Boolean(selectedUserId),
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

  const updateProfileMutation = useMutation({
    mutationFn: () =>
      usersApi.updateProfile(selectedUserId!, {
        fullName: profFullName.trim() || undefined,
        firstName: profFirstName.trim() || null,
        lastName: profLastName.trim() || null,
        phoneNumber: profPhone.trim() || null,
        jobTitle: profJobTitle.trim() || null,
        countryCode: profCountry || null,
        timeZone: profTimeZone || null,
        locale: profLocale || null,
        adminNotes: profNotes.trim() || null,
      }),
    onSuccess: () => {
      notify('Đã cập nhật hồ sơ an toàn của người dùng.', 'success');
      void qc.invalidateQueries({ queryKey: ['users'] });
      if (selectedUserId) {
        void qc.invalidateQueries({ queryKey: queryKeys.userDetail(selectedUserId) });
      }
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
  const canUpdateProfile = auth.hasPermission('platform.users.profile.update') || canManage;

  return (
    <div>
      <PageHeader
        title="Quản lý Người dùng"
        subtitle="Danh mục tài khoản định danh, đồng bộ Clerk External Auth, chỉnh sửa hồ sơ an toàn và kiểm soát phiên truy cập."
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

      {clerkStatusQuery.data && (
        <div className="panel" style={{ marginBottom: 16 }}>
          <div className="panel-body" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <ShieldCheck size={18} />
              <div>
                <div style={{ fontWeight: 600, fontSize: 13.5 }}>Clerk Identity Federation</div>
                <div className="mono form-hint">App ID: {clerkStatusQuery.data.appId}</div>
              </div>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
              <Badge variant={clerkStatusQuery.data.isEnabled ? 'success' : 'neutral'}>
                {clerkStatusQuery.data.isEnabled ? 'Clerk Auth Enabled' : 'Local JWT Active (Clerk Staged)'}
              </Badge>
              <Badge variant="info">Linked: {clerkStatusQuery.data.linkedIdentitiesCount}</Badge>
              {clerkStatusQuery.data.failedSyncCount > 0 && (
                <Badge variant="danger">Sync Errors: {clerkStatusQuery.data.failedSyncCount}</Badge>
              )}
            </div>
          </div>
        </div>
      )}

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
                <button type="button" className="btn btn-sm" onClick={() => setSelectedUserId(u.id)}>
                  <UserCog size={12} /> Hồ sơ
                </button>
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
        open={selectedUserId !== null}
        title={`Chi tiết & Hồ sơ Người dùng${detailQuery.data ? ` — ${detailQuery.data.email}` : ''}`}
        onClose={() => setSelectedUserId(null)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setSelectedUserId(null)}>Đóng</button>
            {canUpdateProfile && (
              <button
                type="button"
                className="btn btn-primary"
                disabled={updateProfileMutation.isPending || detailQuery.isLoading}
                onClick={() => updateProfileMutation.mutate()}
              >
                {updateProfileMutation.isPending ? 'Đang lưu...' : 'Lưu hồ sơ an toàn'}
              </button>
            )}
          </>
        }
      >
        {detailQuery.isLoading ? (
          <div className="form-hint">Đang tải thông tin chi tiết...</div>
        ) : detailQuery.data ? (
          <>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
              <Badge variant="neutral">ID: {detailQuery.data.id.slice(0, 8)}…</Badge>
              <Badge variant={detailQuery.data.clerkSubject ? 'success' : 'neutral'}>
                Clerk: {detailQuery.data.clerkSubject ?? 'Chưa liên kết'}
              </Badge>
              <Badge variant="info">Sessions: {detailQuery.data.activeSessionCount}</Badge>
              <Badge variant="neutral">Tenants: {detailQuery.data.tenantMemberships.length}</Badge>
            </div>
            <div className="alert alert-info" style={{ marginBottom: 12, fontSize: 12 }}>
              Biểu mẫu hồ sơ chỉ cập nhật thông tin phi đặc quyền. Phân quyền (RBAC) và tư cách Tenant được quản lý tách biệt tại Access Control.
            </div>
            <div className="grid-2">
              <div className="form-field">
                <label className="form-label" htmlFor="pf-first">Họ (First Name)</label>
                <input id="pf-first" className="input" value={profFirstName} onChange={(e) => setProfFirstName(e.target.value)} disabled={!canUpdateProfile} />
              </div>
              <div className="form-field">
                <label className="form-label" htmlFor="pf-last">Tên (Last Name)</label>
                <input id="pf-last" className="input" value={profLastName} onChange={(e) => setProfLastName(e.target.value)} disabled={!canUpdateProfile} />
              </div>
            </div>
            <div className="grid-2">
              <div className="form-field">
                <label className="form-label" htmlFor="pf-full">Tên hiển thị</label>
                <input id="pf-full" className="input" value={profFullName} onChange={(e) => setProfFullName(e.target.value)} disabled={!canUpdateProfile} />
              </div>
              <div className="form-field">
                <label className="form-label" htmlFor="pf-title">Chức danh</label>
                <input id="pf-title" className="input" value={profJobTitle} onChange={(e) => setProfJobTitle(e.target.value)} disabled={!canUpdateProfile} />
              </div>
            </div>
            <div className="grid-2">
              <div className="form-field">
                <label className="form-label" htmlFor="pf-phone">Số điện thoại</label>
                <input id="pf-phone" className="input" value={profPhone} onChange={(e) => setProfPhone(e.target.value)} disabled={!canUpdateProfile} />
              </div>
              <SearchableSelect id="pf-country" label="Quốc gia" source="country" value={profCountry} onChange={setProfCountry} disabled={!canUpdateProfile} />
            </div>
            <div className="grid-2">
              <SearchableSelect id="pf-tz" label="Múi giờ (IANA)" source="timezone" value={profTimeZone} onChange={setProfTimeZone} disabled={!canUpdateProfile} />
              <SearchableSelect id="pf-locale" label="Ngôn ngữ / Locale" source="locale" value={profLocale} onChange={setProfLocale} disabled={!canUpdateProfile} />
            </div>
            <div className="form-field" style={{ marginBottom: 0 }}>
              <label className="form-label" htmlFor="pf-notes">Ghi chú quản trị</label>
              <input id="pf-notes" className="input" value={profNotes} onChange={(e) => setProfNotes(e.target.value)} disabled={!canUpdateProfile} />
            </div>
          </>
        ) : null}
      </Dialog>

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
