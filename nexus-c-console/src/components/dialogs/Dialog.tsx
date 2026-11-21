import { useEffect } from 'react';
import type { ReactNode } from 'react';
import { X } from 'lucide-react';

export function Dialog({
  open,
  title,
  onClose,
  children,
  footer,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}) {
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="dialog-backdrop" role="presentation" onClick={onClose}>
      <div
        className="dialog-box"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="dialog-header">
          <h3 style={{ margin: 0, fontSize: 15.5, fontWeight: 600 }}>{title}</h3>
          <button type="button" className="btn btn-ghost btn-sm" aria-label="Đóng" onClick={onClose}>
            <X size={16} />
          </button>
        </div>
        <div className="dialog-body">{children}</div>
        {footer && <div className="dialog-footer">{footer}</div>}
      </div>
    </div>
  );
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Xác nhận',
  danger = false,
  isPending = false,
  onConfirm,
  onCancel,
  children,
}: {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
  isPending?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  children?: ReactNode;
}) {
  return (
    <Dialog
      open={open}
      title={title}
      onClose={onCancel}
      footer={
        <>
          <button type="button" className="btn" onClick={onCancel} disabled={isPending}>
            Hủy
          </button>
          <button
            type="button"
            className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
            onClick={onConfirm}
            disabled={isPending}
          >
            {isPending ? 'Đang xử lý...' : confirmLabel}
          </button>
        </>
      }
    >
      <p style={{ marginTop: 0, color: 'var(--color-text-secondary)' }}>{message}</p>
      {children}
    </Dialog>
  );
}
