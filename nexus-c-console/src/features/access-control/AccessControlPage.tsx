import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, useToast } from '../../components/feedback/Feedback';
import { Dialog } from '../../components/dialogs/Dialog';
import { accessControlApi } from '../../services/api/modules/users.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';

export default function AccessControlPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [tab, setTab] = useState<'roles' | 'permissions'>('roles');
  const [createOpen, setCreateOpen] = useState(false);
  const [roleName, setRoleName] = useState('');
  const [roleDesc, setRoleDesc] = useState('');
  const [selectedPerms, setSelectedPerms] = useState<string[]>([]);

  const rolesQuery = useQuery({
    queryKey: queryKeys.roles,
    queryFn: ({ signal }) => accessControlApi.listRoles(signal),
  });

  const permsQuery = useQuery({
    queryKey: queryKeys.permissions,
    queryFn: ({ signal }) => accessControlApi.listPermissions(signal),
  });

  const createRoleMutation = useMutation({
    mutationFn: () => accessControlApi.createRole({ name: roleName.trim(), description: roleDesc.trim(), permissionCodes: selectedPerms }),
    onSuccess: (r) => {
      notify(`Đã tạo vai trò ${r.name}.`, 'success');
      setCreateOpen(false);
      setRoleName('');
      setRoleDesc('');
      setSelectedPerms([]);
      void qc.invalidateQueries({ queryKey: queryKeys.roles });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const togglePerm = (code: string) => {
    setSelectedPerms((prev) => (prev.includes(code) ? prev.filter((p) => p !== code) : [...prev, code]));
  };

  const canCreateRole = auth.hasPermission('platform.roles.create');

  return (
    <div>
      <PageHeader
        title="Phân quyền (RBAC & Scopes)"
        subtitle="Quản lý vai trò hệ thống, vai trò tùy chỉnh và ma trận quyền theo phạm vi Platform / Tenant."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Core' }, { label: 'Access Control' }]}
        actions={
          canCreateRole ? (
            <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
              <Plus size={15} /> Tạo vai trò
            </button>
          ) : undefined
        }
      />

      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'roles'} className={`tab-btn ${tab === 'roles' ? 'active' : ''}`} onClick={() => setTab('roles')}>
          Vai trò ({rolesQuery.data?.length ?? 0})
        </button>
        <button type="button" role="tab" aria-selected={tab === 'permissions'} className={`tab-btn ${tab === 'permissions' ? 'active' : ''}`} onClick={() => setTab('permissions')}>
          Danh mục Quyền ({permsQuery.data?.length ?? 0})
        </button>
      </div>

      {tab === 'roles' && (
        <DataTable
          columns={[
            {
              key: 'name',
              header: 'Vai trò',
              render: (r) => (
                <div>
                  <span style={{ fontWeight: 600 }}>{r.name}</span>{' '}
                  {r.isSystemRole && <Badge variant="neutral">System</Badge>}
                  <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{r.description}</div>
                </div>
              ),
            },
            {
              key: 'scope',
              header: 'Phạm vi',
              render: (r) => <Badge variant={r.scope === 'Platform' ? 'info' : 'neutral'}>{r.scope}</Badge>,
            },
            {
              key: 'perms',
              header: 'Số quyền',
              render: (r) => <span className="tabular">{r.permissions.length} quyền</span>,
            },
          ]}
          rows={rolesQuery.data ?? []}
          rowKey={(r) => r.id}
          isLoading={rolesQuery.isLoading}
          error={rolesQuery.error}
          onRetry={() => void rolesQuery.refetch()}
        />
      )}

      {tab === 'permissions' && (
        <DataTable
          columns={[
            { key: 'code', header: 'Mã quyền', render: (p) => <span className="mono">{p.code}</span> },
            { key: 'name', header: 'Mô tả', render: (p) => p.name },
            { key: 'category', header: 'Nhóm', render: (p) => p.category },
            { key: 'scope', header: 'Phạm vi', render: (p) => <Badge variant={p.scope === 'Platform' ? 'info' : 'neutral'}>{p.scope}</Badge> },
          ]}
          rows={permsQuery.data ?? []}
          rowKey={(p) => p.id}
          isLoading={permsQuery.isLoading}
          error={permsQuery.error}
          onRetry={() => void permsQuery.refetch()}
        />
      )}

      <Dialog
        open={createOpen}
        title="Tạo vai trò tùy chỉnh"
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button type="button" className="btn btn-primary" disabled={createRoleMutation.isPending} onClick={() => createRoleMutation.mutate()}>
              {createRoleMutation.isPending ? 'Đang tạo...' : 'Tạo vai trò'}
            </button>
          </>
        }
      >
        <div className="form-field">
          <label className="form-label" htmlFor="r-name">Tên vai trò</label>
          <input id="r-name" className="input" value={roleName} onChange={(e) => setRoleName(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="r-desc">Mô tả</label>
          <input id="r-desc" className="input" value={roleDesc} onChange={(e) => setRoleDesc(e.target.value)} />
        </div>
        <div className="form-field">
          <span className="form-label">Chọn quyền được cấp ({selectedPerms.length})</span>
          <div style={{ maxHeight: 220, overflowY: 'auto', border: '1px solid var(--color-border)', borderRadius: 4, padding: 8 }}>
            {(permsQuery.data ?? []).map((p) => (
              <label key={p.code} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', fontSize: 12.5 }}>
                <input type="checkbox" checked={selectedPerms.includes(p.code)} onChange={() => togglePerm(p.code)} />
                <span className="mono">{p.code}</span>
                <span style={{ color: 'var(--color-text-muted)' }}>({p.scope})</span>
              </label>
            ))}
          </div>
        </div>
      </Dialog>
    </div>
  );
}
