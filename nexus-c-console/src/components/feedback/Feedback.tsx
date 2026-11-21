import { Component, createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { AlertCircle, Info, Inbox, Loader2, ShieldAlert, X } from 'lucide-react';
import { getErrorMessage } from '../../services/api/errors';

export type BadgeVariant = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

export function Badge({ children, variant = 'neutral' }: { children: ReactNode; variant?: BadgeVariant }) {
  return <span className={`badge badge-${variant}`}>{children}</span>;
}

export function statusToBadgeVariant(status: string): BadgeVariant {
  const s = status.toLowerCase();
  if (['active', 'healthy', 'connected', 'succeeded', 'approved', 'delivered', 'success', 'enabled'].includes(s)) {
    return 'success';
  }
  if (['suspended', 'pending', 'degraded', 'warning', 'trial', 'retrying', 'pastdue'].includes(s)) {
    return 'warning';
  }
  if (['failed', 'rejected', 'revoked', 'disabled', 'locked', 'error', 'blocked', 'expired', 'disconnected', 'unhealthy'].includes(s)) {
    return 'danger';
  }
  return 'neutral';
}

export function LoadingState({ label = 'Đang tải dữ liệu...' }: { label?: string }) {
  return (
    <div className="state-box" role="status" aria-live="polite">
      <Loader2 size={22} style={{ margin: '0 auto', display: 'block' }} />
      <div className="state-title">{label}</div>
    </div>
  );
}

export function EmptyState({
  title = 'Chưa có dữ liệu',
  description,
  action,
}: {
  title?: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="state-box">
      <Inbox size={24} style={{ margin: '0 auto', display: 'block', opacity: 0.7 }} />
      <div className="state-title">{title}</div>
      {description && <p style={{ margin: '4px 0 12px', fontSize: 13 }}>{description}</p>}
      {action}
    </div>
  );
}

export function ErrorState({
  error,
  onRetry,
}: {
  error: unknown;
  onRetry?: () => void;
}) {
  return (
    <div className="alert alert-danger" role="alert">
      <AlertCircle size={18} style={{ flexShrink: 0, marginTop: 1 }} />
      <div style={{ flex: 1 }}>
        <strong>Không thể tải dữ liệu</strong>
        <div style={{ marginTop: 2 }}>{getErrorMessage(error)}</div>
        {onRetry && (
          <button type="button" className="btn btn-sm" style={{ marginTop: 8 }} onClick={onRetry}>
            Thử lại
          </button>
        )}
      </div>
    </div>
  );
}

export function NotAvailablePanel({
  title,
  endpoint,
  description,
}: {
  title: string;
  endpoint?: string;
  description: string;
}) {
  return (
    <div className="panel">
      <div className="panel-body">
        <div className="alert alert-warning" role="status">
          <ShieldAlert size={18} style={{ flexShrink: 0, marginTop: 2 }} />
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
              <strong>{title}</strong>
              <Badge variant="warning">Not Available</Badge>
            </div>
            <p style={{ margin: 0, color: 'var(--color-text-secondary)' }}>{description}</p>
            {endpoint && (
              <p style={{ margin: '6px 0 0', fontSize: 12, color: 'var(--color-text-muted)' }}>
                Backend contract dự kiến: <span className="mono">{endpoint}</span> (xem{' '}
                <span className="mono">docs/api-integration.md</span>).
              </p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// === Toast System (Replaces browser alert()) ===
interface ToastItem {
  id: string;
  message: string;
  variant: 'info' | 'success' | 'danger';
}

interface ToastContextValue {
  notify: (message: string, variant?: 'info' | 'success' | 'danger') => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const notify = useCallback(
    (message: string, variant: 'info' | 'success' | 'danger' = 'info') => {
      const id = `${Date.now()}-${Math.random().toString(16).slice(2, 6)}`;
      setToasts((prev) => [...prev.slice(-3), { id, message, variant }]);
      setTimeout(() => dismiss(id), 4500);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-stack" aria-live="polite" aria-atomic="true">
        {toasts.map((t) => (
          <div key={t.id} className={`toast-item ${t.variant}`} role="status">
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Info size={15} />
              <span>{t.message}</span>
            </div>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              aria-label="Đóng thông báo"
              onClick={() => dismiss(t.id)}
            >
              <X size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used within ToastProvider');
  return ctx;
}

// === React Error Boundary ===
interface ErrorBoundaryState {
  hasError: boolean;
  errorMessage: string;
}

export class AppErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false, errorMessage: '' };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, errorMessage: error.message };
  }

  componentDidCatch(_error: Error, _info: ErrorInfo): void {
    // Error captured cleanly by boundary; avoid logging sensitive state to console in production
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ maxWidth: 560, margin: '64px auto', padding: 20 }}>
          <div className="panel">
            <div className="panel-body">
              <h2 style={{ marginTop: 0 }}>Đã xảy ra lỗi hiển thị</h2>
              <p style={{ color: 'var(--color-text-muted)' }}>{this.state.errorMessage}</p>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => this.setState({ hasError: false, errorMessage: '' })}
              >
                Tải lại thành phần
              </button>
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
