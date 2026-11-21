import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageHeader } from '../../components/layout/PageLayout';
import { Badge, ErrorState, LoadingState, useToast } from '../../components/feedback/Feedback';
import { ConfirmDialog } from '../../components/dialogs/Dialog';
import { authApi } from '../../services/api/modules/auth.api';
import { queryKeys } from '../../services/api/query-keys';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';
import { formatDateTime } from '../../utils/format';

export default function ProfileSessionsPage() {
  const auth = useAuth();
  const { notify } = useToast();
  const qc = useQueryClient();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [revokeTarget, setRevokeTarget] = useState<string | null>(null);

  const sessionsQuery = useQuery({
    queryKey: queryKeys.sessions,
    queryFn: ({ signal }) => authApi.listSessions(signal),
  });

  const revokeMutation = useMutation({
    mutationFn: (sessionId: string) => authApi.revokeSession(sessionId),
    onSuccess: () => {
      notify('Đã thu hồi phiên đăng nhập.', 'success');
      setRevokeTarget(null);
      void qc.invalidateQueries({ queryKey: queryKeys.sessions });
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  const changePwdMutation = useMutation({
    mutationFn: () => authApi.changePassword(currentPassword, newPassword),
    onSuccess: () => {
      notify('Đổi mật khẩu thành công.', 'success');
      setCurrentPassword('');
      setNewPassword('');
    },
    onError: (err) => notify(getErrorMessage(err), 'danger'),
  });

  return (
    <div>
      <PageHeader
        title="Hồ sơ & Quản lý phiên đăng nhập"
        subtitle="Kiểm soát định danh, phạm vi truy cập và thu hồi phiên đăng nhập đang hoạt động."
        breadcrumbs={[{ label: 'Overview', to: '/overview' }, { label: 'Hồ sơ & Phiên' }]}
      />

      <div className="grid-2" style={{ marginBottom: 16 }}>
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Thông tin tài khoản hiện tại</h2>
            <Badge variant={auth.isPlatformUser ? 'info' : 'neutral'}>{auth.scope}</Badge>
          </div>
          <div className="panel-body">
            <dl style={{ margin: 0, display: 'grid', gridTemplateColumns: '140px 1fr', rowGap: 8 }}>
              <dt style={{ color: 'var(--color-text-muted)' }}>Email</dt>
              <dd style={{ margin: 0 }}>{auth.email}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>User ID</dt>
              <dd className="mono" style={{ margin: 0 }}>{auth.userId}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>Tenant ID</dt>
              <dd className="mono" style={{ margin: 0 }}>{auth.tenantId ?? 'Platform (Global)'}</dd>
              <dt style={{ color: 'var(--color-text-muted)' }}>Vai trò</dt>
              <dd style={{ margin: 0, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {auth.roles.length > 0 ? auth.roles.map((r) => <Badge key={r} variant="info">{r}</Badge>) : '—'}
              </dd>
            </dl>
          </div>
        </div>

        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Đổi mật khẩu</h2>
          </div>
          <div className="panel-body">
            <form
              onSubmit={(e) => {
                e.preventDefault();
                changePwdMutation.mutate();
              }}
            >
              <div className="form-field">
                <label className="form-label" htmlFor="cur-pw">Mật khẩu hiện tại</label>
                <input
                  id="cur-pw"
                  type="password"
                  className="input"
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  required
                />
              </div>
              <div className="form-field">
                <label className="form-label" htmlFor="new-pw">Mật khẩu mới (tối thiểu 10 ký tự, hoa/thường/số/ký tự đặc biệt)</label>
                <input
                  id="new-pw"
                  type="password"
                  className="input"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                />
              </div>
              <button type="submit" className="btn btn-primary" disabled={changePwdMutation.isPending}>
                {changePwdMutation.isPending ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
              </button>
            </form>
          </div>
        </div>
      </div>

      <div className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Danh sách phiên đăng nhập</h2>
        </div>
        {sessionsQuery.isLoading ? (
          <LoadingState />
        ) : sessionsQuery.error ? (
          <div className="panel-body"><ErrorState error={sessionsQuery.error} onRetry={() => void sessionsQuery.refetch()} /></div>
        ) : (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Session ID</th>
                  <th>IP</th>
                  <th>Tạo lúc</th>
                  <th>Hoạt động cuối</th>
                  <th>Trạng thái</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {(sessionsQuery.data ?? []).map((s) => (
                  <tr key={s.id}>
                    <td className="mono">
                      {s.id} {s.id === auth.sessionId && <Badge variant="info">Hiện tại</Badge>}
                    </td>
                    <td className="mono">{s.ipAddress ?? '—'}</td>
                    <td>{formatDateTime(s.createdAt)}</td>
                    <td>{formatDateTime(s.lastSeenAt)}</td>
                    <td>
                      <Badge variant={s.isRevoked ? 'danger' : 'success'}>
                        {s.isRevoked ? 'Revoked' : 'Active'}
                      </Badge>
                    </td>
                    <td>
                      {!s.isRevoked && (
                        <button type="button" className="btn btn-sm btn-danger" onClick={() => setRevokeTarget(s.id)}>
                          Thu hồi
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <ConfirmDialog
        open={revokeTarget !== null}
        title="Thu hồi phiên đăng nhập"
        message="Phiên này sẽ bị vô hiệu hóa ngay lập tức và refresh token liên kết sẽ không thể sử dụng lại."
        confirmLabel="Thu hồi phiên"
        danger
        isPending={revokeMutation.isPending}
        onCancel={() => setRevokeTarget(null)}
        onConfirm={() => {
          if (revokeTarget) revokeMutation.mutate(revokeTarget);
        }}
      />
    </div>
  );
}
