import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Shield } from 'lucide-react';
import { useAuth } from '../../services/auth/AuthContext';
import { getErrorMessage } from '../../services/api/errors';

const loginSchema = z.object({
  email: z.string().email('Email không hợp lệ'),
  password: z.string().min(1, 'Vui lòng nhập mật khẩu'),
  tenantId: z.string().optional(),
});

type LoginFormValues = z.infer<typeof loginSchema>;

export default function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', tenantId: '' },
  });

  const onSubmit = async (values: LoginFormValues) => {
    setServerError(null);
    try {
      await auth.login({
        email: values.email.trim(),
        password: values.password,
        tenantId: values.tenantId?.trim() ? values.tenantId.trim() : null,
      });
      navigate('/overview', { replace: true });
    } catch (err) {
      setServerError(getErrorMessage(err));
    }
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 20,
        background: 'var(--color-bg)',
      }}
    >
      <div className="panel" style={{ width: '100%', maxWidth: 420 }}>
        <div className="panel-body" style={{ padding: 28 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 18 }}>
            <div
              style={{
                width: 34,
                height: 34,
                borderRadius: 6,
                background: 'var(--color-navy)',
                color: '#fff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <Shield size={18} />
            </div>
            <div>
              <div style={{ fontWeight: 700, fontSize: 16 }}>
                {import.meta.env.VITE_APP_NAME || 'NEXUS C Console'}
              </div>
              <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                Enterprise Platform Control Center
              </div>
            </div>
          </div>

          {serverError && (
            <div className="alert alert-danger" role="alert" style={{ marginBottom: 14 }}>
              {serverError}
            </div>
          )}

          <form onSubmit={handleSubmit(onSubmit)} noValidate>
            <div className="form-field">
              <label className="form-label" htmlFor="login-email">
                Email quản trị
              </label>
              <input
                id="login-email"
                type="email"
                className="input"
                autoComplete="username"
                placeholder="admin@nexus.local"
                {...register('email')}
              />
              {errors.email && <span className="form-error">{errors.email.message}</span>}
            </div>

            <div className="form-field">
              <label className="form-label" htmlFor="login-password">
                Mật khẩu
              </label>
              <input
                id="login-password"
                type="password"
                className="input"
                autoComplete="current-password"
                {...register('password')}
              />
              {errors.password && <span className="form-error">{errors.password.message}</span>}
            </div>

            <div className="form-field">
              <label className="form-label" htmlFor="login-tenant">
                Tenant ID (tùy chọn — để trống cho Platform Scope)
              </label>
              <input
                id="login-tenant"
                type="text"
                className="input mono"
                placeholder="UUID của tenant nếu đăng nhập phạm vi tenant"
                {...register('tenantId')}
              />
            </div>

            <button
              type="submit"
              className="btn btn-primary"
              style={{ width: '100%', height: 38 }}
              disabled={isSubmitting || auth.isLoading}
            >
              {isSubmitting || auth.isLoading ? 'Đang xác thực...' : 'Đăng nhập'}
            </button>
          </form>

          <p className="form-hint" style={{ marginTop: 16, marginBottom: 0 }}>
            Phiên đăng nhập được giữ trong bộ nhớ ứng dụng (không lưu token vào localStorage) và xác thực trực tiếp qua NEXUS C Identity.
          </p>
        </div>
      </div>
    </div>
  );
}
