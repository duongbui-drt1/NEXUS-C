import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, statusToBadgeVariant, useToast } from '../../components/feedback/Feedback';
import { Dialog } from '../../components/dialogs/Dialog';
import { organizationsApi } from '../../services/api/modules/organizations.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

type OrgTab = 'legal-entities' | 'branches' | 'units';

export default function OrganizationsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [tab, setTab] = useState<OrgTab>('legal-entities');
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [tin, setTin] = useState('');
  const [address, setAddress] = useState('');
  const [legalEntityId, setLegalEntityId] = useState('');
  const [parentId, setParentId] = useState('');
  const [unitType, setUnitType] = useState(2);

  const hasTenant = Boolean(auth.tenantId);

  const leQuery = useQuery({
    queryKey: queryKeys.legalEntities({ page, pageSize: 20 }),
    queryFn: ({ signal }) => organizationsApi.listLegalEntities({ page, pageSize: 20 }, signal),
    enabled: hasTenant,
  });

  const branchesQuery = useQuery({
    queryKey: queryKeys.branches({ page, pageSize: 20 }),
    queryFn: ({ signal }) => organizationsApi.listBranches({ page, pageSize: 20 }, signal),
    enabled: hasTenant && tab === 'branches',
  });

  const unitsQuery = useQuery({
    queryKey: queryKeys.orgUnits,
    queryFn: ({ signal }) => organizationsApi.listUnits(signal),
    enabled: hasTenant && tab === 'units',
  });

  const createLe = useMutation({
    mutationFn: () => organizationsApi.createLegalEntity({ code, legalName: name, taxIdentificationNumber: tin || undefined, address: address || undefined }),
    onSuccess: () => {
      notify('Đã tạo pháp nhân thành công.', 'success');
      setCreateOpen(false);
      setCode('');
      setName('');
      setTin('');
      setAddress('');
      void qc.invalidateQueries({ queryKey: ['organizations'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const createBranch = useMutation({
    mutationFn: () => organizationsApi.createBranch({ legalEntityId, code, name, address: address || undefined }),
    onSuccess: () => {
      notify('Đã tạo chi nhánh thành công.', 'success');
      setCreateOpen(false);
      setCode('');
      setName('');
      setAddress('');
      void qc.invalidateQueries({ queryKey: ['organizations'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const createUnit = useMutation({
    mutationFn: () => organizationsApi.createUnit({ parentId: parentId || null, code, name, unitType }),
    onSuccess: () => {
      notify('Đã tạo đơn vị tổ chức thành công.', 'success');
      setCreateOpen(false);
      setCode('');
      setName('');
      setParentId('');
      void qc.invalidateQueries({ queryKey: queryKeys.orgUnits });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const canManage = auth.hasPermission('platform.organizations.manage');

  return (
    <div>
      <PageHeader
        title="Quản lý Tổ chức"
        subtitle="Pháp nhân (Legal Entities), Chi nhánh (Branches) và Cơ cấu đơn vị phân cấp trong tenant hiện tại."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Core' }, { label: 'Organizations' }]}
        actions={
          hasTenant && canManage ? (
            <button type="button" className="btn btn-primary" onClick={() => setCreateOpen(true)}>
              <Plus size={15} /> Thêm mới
            </button>
          ) : undefined
        }
      />

      {!hasTenant ? (
        <div className="alert alert-info" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}>
          <span>Dữ liệu Tổ chức thuộc phạm vi Tenant (RLS). Chọn ngay một Tenant có sẵn để quản lý pháp nhân và chi nhánh:</span>
          {auth.accessibleTenants.length > 0 && (
            <select
              className="select"
              style={{ width: 'auto', minWidth: 240 }}
              aria-label="Chọn nhanh Tenant cho Tổ chức"
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
          <div className="tabs" role="tablist">
            <button type="button" role="tab" aria-selected={tab === 'legal-entities'} className={`tab-btn ${tab === 'legal-entities' ? 'active' : ''}`} onClick={() => setTab('legal-entities')}>
              Pháp nhân (Legal Entities)
            </button>
            <button type="button" role="tab" aria-selected={tab === 'branches'} className={`tab-btn ${tab === 'branches' ? 'active' : ''}`} onClick={() => setTab('branches')}>
              Chi nhánh (Branches)
            </button>
            <button type="button" role="tab" aria-selected={tab === 'units'} className={`tab-btn ${tab === 'units' ? 'active' : ''}`} onClick={() => setTab('units')}>
              Đơn vị tổ chức (Units)
            </button>
          </div>

          {tab === 'legal-entities' && (
            <DataTable
              columns={[
                { key: 'code', header: 'Mã', render: (e) => <span className="mono">{e.code}</span> },
                { key: 'legalName', header: 'Tên pháp nhân', render: (e) => e.legalName },
                { key: 'tin', header: 'Mã số thuế', render: (e) => <span className="mono">{e.taxIdentificationNumber ?? '—'}</span> },
                { key: 'status', header: 'Trạng thái', render: (e) => <Badge variant={statusToBadgeVariant(e.status)}>{e.status}</Badge> },
                { key: 'createdAt', header: 'Ngày tạo', render: (e) => formatDateTime(e.createdAt) },
              ]}
              rows={leQuery.data?.items ?? []}
              rowKey={(e) => e.id}
              isLoading={leQuery.isLoading}
              error={leQuery.error}
              onRetry={() => void leQuery.refetch()}
              pagination={leQuery.data ? { ...leQuery.data, onPageChange: setPage } : undefined}
            />
          )}

          {tab === 'branches' && (
            <DataTable
              columns={[
                { key: 'code', header: 'Mã chi nhánh', render: (b) => <span className="mono">{b.code}</span> },
                { key: 'name', header: 'Tên chi nhánh', render: (b) => b.name },
                { key: 'address', header: 'Địa chỉ', render: (b) => b.address ?? '—' },
                { key: 'status', header: 'Trạng thái', render: (b) => <Badge variant={statusToBadgeVariant(b.status)}>{b.status}</Badge> },
              ]}
              rows={branchesQuery.data?.items ?? []}
              rowKey={(b) => b.id}
              isLoading={branchesQuery.isLoading}
              error={branchesQuery.error}
              onRetry={() => void branchesQuery.refetch()}
              pagination={branchesQuery.data ? { ...branchesQuery.data, onPageChange: setPage } : undefined}
            />
          )}

          {tab === 'units' && (
            <DataTable
              columns={[
                { key: 'code', header: 'Mã đơn vị', render: (u) => <span className="mono">{u.code}</span> },
                { key: 'name', header: 'Tên đơn vị', render: (u) => u.name },
                { key: 'type', header: 'Loại', render: (u) => <Badge variant="neutral">{u.unitType}</Badge> },
                { key: 'parent', header: 'Đơn vị cha', render: (u) => <span className="mono">{u.parentId ?? 'Root'}</span> },
              ]}
              rows={unitsQuery.data ?? []}
              rowKey={(u) => u.id}
              isLoading={unitsQuery.isLoading}
              error={unitsQuery.error}
              onRetry={() => void unitsQuery.refetch()}
            />
          )}
        </>
      )}

      <Dialog
        open={createOpen}
        title={tab === 'legal-entities' ? 'Tạo Pháp nhân' : tab === 'branches' ? 'Tạo Chi nhánh' : 'Tạo Đơn vị tổ chức'}
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setCreateOpen(false)}>Hủy</button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => {
                if (tab === 'legal-entities') createLe.mutate();
                else if (tab === 'branches') createBranch.mutate();
                else createUnit.mutate();
              }}
            >
              Lưu
            </button>
          </>
        }
      >
        {tab === 'branches' && (
          <div className="form-field">
            <label className="form-label" htmlFor="br-le">Pháp nhân trực thuộc</label>
            <select id="br-le" className="select" value={legalEntityId} onChange={(e) => setLegalEntityId(e.target.value)}>
              <option value="">-- Chọn pháp nhân --</option>
              {(leQuery.data?.items ?? []).map((le) => (
                <option key={le.id} value={le.id}>{le.code} — {le.legalName}</option>
              ))}
            </select>
          </div>
        )}
        <div className="form-field">
          <label className="form-label" htmlFor="org-code">Mã</label>
          <input id="org-code" className="input mono" value={code} onChange={(e) => setCode(e.target.value)} />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="org-name">Tên</label>
          <input id="org-name" className="input" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        {tab === 'legal-entities' && (
          <div className="form-field">
            <label className="form-label" htmlFor="org-tin">Mã số thuế</label>
            <input id="org-tin" className="input mono" value={tin} onChange={(e) => setTin(e.target.value)} />
          </div>
        )}
        {tab !== 'units' && (
          <div className="form-field">
            <label className="form-label" htmlFor="org-addr">Địa chỉ</label>
            <input id="org-addr" className="input" value={address} onChange={(e) => setAddress(e.target.value)} />
          </div>
        )}
        {tab === 'units' && (
          <>
            <div className="form-field">
              <label className="form-label" htmlFor="unit-parent">Đơn vị cha (tùy chọn)</label>
              <select id="unit-parent" className="select" value={parentId} onChange={(e) => setParentId(e.target.value)}>
                <option value="">Không có (Cấp gốc)</option>
                {(unitsQuery.data ?? []).map((u) => (
                  <option key={u.id} value={u.id}>{u.code} — {u.name}</option>
                ))}
              </select>
            </div>
            <div className="form-field">
              <label className="form-label" htmlFor="unit-type">Loại đơn vị</label>
              <select id="unit-type" className="select" value={unitType} onChange={(e) => setUnitType(Number(e.target.value))}>
                <option value={1}>Division</option>
                <option value={2}>Department</option>
                <option value={3}>Team</option>
                <option value={4}>CostCenter</option>
                <option value={5}>Warehouse</option>
              </select>
            </div>
          </>
        )}
      </Dialog>
    </div>
  );
}
