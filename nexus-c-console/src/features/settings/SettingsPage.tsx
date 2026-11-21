import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { PageHeader } from '../../components/layout/PageLayout';
import { DataTable } from '../../components/tables/DataTable';
import { Badge, useToast } from '../../components/feedback/Feedback';
import { configurationApi, notificationsApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getApiBaseUrl } from '../../services/api/client';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

type SettingsTab = 'config' | 'flags' | 'notifications' | 'environment';

export default function SettingsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();

  const initialTab = (searchParams.get('tab') as SettingsTab | null) ?? 'config';
  const [tab, setTab] = useState<SettingsTab>(['config', 'flags', 'notifications', 'environment'].includes(initialTab) ? initialTab : 'config');

  const [settingKey, setSettingKey] = useState('');
  const [settingVal, setSettingVal] = useState('');
  const [settingCat, setSettingCat] = useState('General');

  const [flagKey, setFlagKey] = useState('');
  const [flagEnabled, setFlagEnabled] = useState(true);
  const [flagDesc, setFlagDesc] = useState('');

  const globalSettingsQuery = useQuery({
    queryKey: queryKeys.globalSettings,
    queryFn: ({ signal }) => configurationApi.listGlobal(signal),
    enabled: tab === 'config',
  });

  const tenantSettingsQuery = useQuery({
    queryKey: queryKeys.tenantSettings(),
    queryFn: ({ signal }) => configurationApi.listTenant(undefined, signal),
    enabled: tab === 'config' && Boolean(auth.tenantId),
  });

  const flagsQuery = useQuery({
    queryKey: queryKeys.featureFlags,
    queryFn: ({ signal }) => configurationApi.listFlags(signal),
    enabled: tab === 'flags',
  });

  const notificationsQuery = useQuery({
    queryKey: queryKeys.notifications({ page: 1, pageSize: 20 }),
    queryFn: ({ signal }) => notificationsApi.listMine({ page: 1, pageSize: 20 }, signal),
    enabled: tab === 'notifications' && Boolean(auth.tenantId),
  });

  const upsertSetting = useMutation({
    mutationFn: () =>
      auth.tenantId
        ? configurationApi.upsertTenant({ key: settingKey, value: settingVal, category: settingCat })
        : configurationApi.upsertGlobal({ key: settingKey, value: settingVal, category: settingCat }),
    onSuccess: (s) => {
      notify(`Đã lưu cấu hình ${s.key} (v${s.version}).`, 'success');
      setSettingKey('');
      setSettingVal('');
      void qc.invalidateQueries({ queryKey: ['configuration'] });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const upsertFlag = useMutation({
    mutationFn: () =>
      auth.tenantId
        ? configurationApi.upsertTenantFlag({ key: flagKey, isEnabled: flagEnabled, description: flagDesc })
        : configurationApi.upsertGlobalFlag({ key: flagKey, isEnabled: flagEnabled, description: flagDesc }),
    onSuccess: (f) => {
      notify(`Đã cập nhật Feature Flag ${f.key}.`, 'success');
      setFlagKey('');
      setFlagDesc('');
      void qc.invalidateQueries({ queryKey: queryKeys.featureFlags });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['notifications'] }),
  });

  const selectTab = (next: SettingsTab) => {
    setTab(next);
    setSearchParams({ tab: next });
  };

  return (
    <div>
      <PageHeader
        title="Cấu hình Hệ thống & Feature Flags"
        subtitle="Cấu hình có phiên bản cho Platform/Tenant, Feature Flags và Trung tâm Thông báo. Hệ thống từ chối lưu secret dạng plaintext."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'System Settings' }]}
      />

      <div className="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'config'} className={`tab-btn ${tab === 'config' ? 'active' : ''}`} onClick={() => selectTab('config')}>
          Configuration
        </button>
        <button type="button" role="tab" aria-selected={tab === 'flags'} className={`tab-btn ${tab === 'flags' ? 'active' : ''}`} onClick={() => selectTab('flags')}>
          Feature Flags
        </button>
        <button type="button" role="tab" aria-selected={tab === 'notifications'} className={`tab-btn ${tab === 'notifications' ? 'active' : ''}`} onClick={() => selectTab('notifications')}>
          Notifications
        </button>
        <button type="button" role="tab" aria-selected={tab === 'environment'} className={`tab-btn ${tab === 'environment' ? 'active' : ''}`} onClick={() => selectTab('environment')}>
          Environment Info
        </button>
      </div>

      {tab === 'config' && (
        <div className="grid-2">
          <DataTable
            columns={[
              { key: 'key', header: 'Key', render: (s) => <span className="mono">{s.key}</span> },
              { key: 'val', header: 'Value', render: (s) => s.value },
              { key: 'cat', header: 'Nhóm', render: (s) => s.category },
              { key: 'ver', header: 'Ver', render: (s) => `v${s.version}` },
            ]}
            rows={auth.tenantId ? (tenantSettingsQuery.data ?? []) : (globalSettingsQuery.data ?? [])}
            rowKey={(s) => s.key}
            isLoading={auth.tenantId ? tenantSettingsQuery.isLoading : globalSettingsQuery.isLoading}
            error={auth.tenantId ? tenantSettingsQuery.error : globalSettingsQuery.error}
          />

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">
                Thêm / Cập nhật ({auth.tenantId ? 'Tenant Setting' : 'Global Platform Setting'})
              </h2>
            </div>
            <div className="panel-body">
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  upsertSetting.mutate();
                }}
              >
                <div className="form-field">
                  <label className="form-label" htmlFor="cfg-k">Key</label>
                  <input id="cfg-k" className="input mono" placeholder="localization.date_format" value={settingKey} onChange={(e) => setSettingKey(e.target.value)} required />
                  <span className="form-hint">Các key chứa password/secret/token/apikey sẽ bị backend chặn tự động.</span>
                </div>
                <div className="form-field">
                  <label className="form-label" htmlFor="cfg-v">Value</label>
                  <input id="cfg-v" className="input" value={settingVal} onChange={(e) => setSettingVal(e.target.value)} required />
                </div>
                <div className="form-field">
                  <label className="form-label" htmlFor="cfg-c">Category</label>
                  <input id="cfg-c" className="input" value={settingCat} onChange={(e) => setSettingCat(e.target.value)} />
                </div>
                <button type="submit" className="btn btn-primary" disabled={upsertSetting.isPending}>
                  Lưu cấu hình
                </button>
              </form>
            </div>
          </div>
        </div>
      )}

      {tab === 'flags' && (
        <div className="grid-2">
          <DataTable
            columns={[
              { key: 'key', header: 'Flag Key', render: (f) => <span className="mono">{f.key}</span> },
              { key: 'enabled', header: 'Trạng thái', render: (f) => <Badge variant={f.isEnabled ? 'success' : 'neutral'}>{f.isEnabled ? 'Enabled' : 'Disabled'}</Badge> },
              { key: 'scope', header: 'Phạm vi', render: (f) => <Badge variant={f.isTenantOverride ? 'info' : 'neutral'}>{f.isTenantOverride ? 'Tenant Override' : 'Global Default'}</Badge> },
              { key: 'ver', header: 'Ver', render: (f) => `v${f.version}` },
            ]}
            rows={flagsQuery.data ?? []}
            rowKey={(f) => f.key}
            isLoading={flagsQuery.isLoading}
            error={flagsQuery.error}
          />

          <div className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Cập nhật Feature Flag</h2>
            </div>
            <div className="panel-body">
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  upsertFlag.mutate();
                }}
              >
                <div className="form-field">
                  <label className="form-label" htmlFor="fl-key">Flag Key</label>
                  <input id="fl-key" className="input mono" placeholder="ai_assistant_beta" value={flagKey} onChange={(e) => setFlagKey(e.target.value)} required />
                </div>
                <div className="form-field">
                  <label className="form-label" htmlFor="fl-desc">Mô tả</label>
                  <input id="fl-desc" className="input" value={flagDesc} onChange={(e) => setFlagDesc(e.target.value)} />
                </div>
                <div className="form-field">
                  <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <input type="checkbox" checked={flagEnabled} onChange={(e) => setFlagEnabled(e.target.checked)} />
                    Bật tính năng (Enabled)
                  </label>
                </div>
                <button type="submit" className="btn btn-primary" disabled={upsertFlag.isPending}>
                  Lưu Feature Flag
                </button>
              </form>
            </div>
          </div>
        </div>
      )}

      {tab === 'notifications' && (
        !auth.tenantId ? (
          <div className="alert alert-info">Hộp thư thông báo hoạt động trong phạm vi Tenant. Chọn một Tenant để xem thông báo.</div>
        ) : (
          <DataTable
            columns={[
              { key: 'subj', header: 'Tiêu đề', render: (n) => <strong>{n.subject}</strong> },
              { key: 'chan', header: 'Kênh', render: (n) => <Badge variant="neutral">{n.channel}</Badge> },
              { key: 'read', header: 'Đọc', render: (n) => <Badge variant={n.isRead ? 'neutral' : 'info'}>{n.isRead ? 'Đã đọc' : 'Mới'}</Badge> },
              { key: 'at', header: 'Thời gian', render: (n) => formatDateTime(n.createdAt) },
              {
                key: 'act',
                header: 'Thao tác',
                render: (n) =>
                  !n.isRead ? (
                    <button type="button" className="btn btn-sm" onClick={() => markRead.mutate(n.id)}>
                      Đánh dấu đã đọc
                    </button>
                  ) : null,
              },
            ]}
            rows={notificationsQuery.data?.items ?? []}
            rowKey={(n) => n.id}
            isLoading={notificationsQuery.isLoading}
            error={notificationsQuery.error}
          />
        )
      )}

      {tab === 'environment' && (
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Thông tin Môi trường Console (Chỉ cấu hình công khai)</h2>
          </div>
          <div className="panel-body">
            <dl style={{ margin: 0, display: 'grid', gridTemplateColumns: '200px 1fr', rowGap: 8 }}>
              <dt style={{ color: 'var(--color-text-muted)' }}>App Name</dt>
              <dd style={{ margin: 0 }}>{import.meta.env.VITE_APP_NAME || 'NEXUS C Console'}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>Environment</dt>
              <dd className="mono" style={{ margin: 0 }}>{import.meta.env.VITE_APP_ENV || import.meta.env.MODE}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>API Base URL</dt>
              <dd className="mono" style={{ margin: 0 }}>{getApiBaseUrl() || '(Same-origin / Vite proxy)'}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>Token Storage Policy</dt>
              <dd style={{ margin: 0 }}>In-memory only (zero localStorage/sessionStorage token persistence)</dd>
            </dl>
          </div>
        </div>
      )}
    </div>
  );
}
