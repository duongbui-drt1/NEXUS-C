import { useEffect, useMemo, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  Activity,
  Bell,
  Building2,
  CreditCard,
  FileSearch,
  KeyRound,
  Layers,
  LayoutDashboard,
  LogOut,
  Menu,
  Moon,
  Network,
  Plug,
  Search,
  Settings,
  Shield,
  Sun,
  Users,
  Webhook,
} from 'lucide-react';
import { useAuth } from '../../services/auth/AuthContext';
import { uiPreferences } from '../../utils/format';
import type { ThemeMode } from '../../utils/format';
import { Badge, useToast } from '../feedback/Feedback';

interface NavEntry {
  to: string;
  label: string;
  section: string;
  icon: typeof LayoutDashboard;
  permission?: string;
  platformOnly?: boolean;
}

const NAV_ITEMS: NavEntry[] = [
  { to: '/overview', label: 'Overview', section: 'Platform', icon: LayoutDashboard },
  { to: '/core/tenants', label: 'Tenants', section: 'Core Management', icon: Building2, permission: 'platform.tenants.read', platformOnly: true },
  { to: '/core/organizations', label: 'Organizations', section: 'Core Management', icon: Network, permission: 'platform.organizations.read' },
  { to: '/core/users', label: 'Users', section: 'Core Management', icon: Users, permission: 'platform.users.read' },
  { to: '/core/access-control', label: 'Access Control', section: 'Core Management', icon: Shield, permission: 'platform.roles.read' },
  { to: '/applications', label: 'Applications (F/E/M)', section: 'Ecosystem', icon: Layers, permission: 'platform.applications.read' },
  { to: '/api-management', label: 'API Management', section: 'Ecosystem', icon: Webhook, permission: 'platform.api.read' },
  { to: '/integrations', label: 'Integration Hub', section: 'Ecosystem', icon: Plug, permission: 'platform.integrations.read' },
  { to: '/operations', label: 'Events & Jobs', section: 'Operations', icon: Activity, permission: 'platform.operations.read', platformOnly: true },
  { to: '/audit', label: 'Audit Center', section: 'Operations', icon: FileSearch, permission: 'platform.audit.read' },
  { to: '/subscriptions', label: 'Subscriptions', section: 'Governance', icon: CreditCard, permission: 'platform.subscriptions.read' },
  { to: '/settings', label: 'System Settings', section: 'Governance', icon: Settings, permission: 'platform.configuration.read' },
];

export function AppShell() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { notify } = useToast();

  const [collapsed, setCollapsed] = useState(() => uiPreferences.getSidebarCollapsed());
  const [theme, setTheme] = useState<ThemeMode>(() => uiPreferences.getTheme());
  const [quickSearch, setQuickSearch] = useState('');

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    uiPreferences.setTheme(theme);
  }, [theme]);

  const toggleSidebar = () => {
    setCollapsed((prev) => {
      const next = !prev;
      uiPreferences.setSidebarCollapsed(next);
      return next;
    });
  };

  const visibleNav = useMemo(() => {
    return NAV_ITEMS.filter((item) => {
      if (item.platformOnly && !auth.isPlatformUser) return false;
      if (item.permission && !auth.hasPermission(item.permission)) return false;
      return true;
    });
  }, [auth]);

  const groupedSections = useMemo(() => {
    const map = new Map<string, NavEntry[]>();
    for (const item of visibleNav) {
      const list = map.get(item.section) ?? [];
      list.push(item);
      map.set(item.section, list);
    }
    return Array.from(map.entries());
  }, [visibleNav]);

  const filteredQuickMatches = useMemo(() => {
    const q = quickSearch.trim().toLowerCase();
    if (!q) return [];
    return visibleNav.filter((i) => i.label.toLowerCase().includes(q) || i.to.toLowerCase().includes(q));
  }, [quickSearch, visibleNav]);

  const handleTenantSwitch = async (targetTenantId: string) => {
    const normalized = targetTenantId.trim() || null;
    if (normalized === (auth.tenantId ?? null)) return;
    try {
      await auth.switchTenant(normalized);
      notify(normalized ? 'Đã chuyển ngữ cảnh Tenant thành công.' : 'Đã chuyển về ngữ cảnh Platform (Global).', 'success');
    } catch {
      notify('Không thể chuyển ngữ cảnh Tenant.', 'danger');
    }
  };

  const handleLogout = async () => {
    await auth.logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app-shell">
      <aside className={`app-sidebar ${collapsed ? 'collapsed' : ''}`} aria-label="Điều hướng chính">
        <div className="sidebar-brand">
          {!collapsed && <span>NEXUS C</span>}
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            style={{ color: '#CBD5E1' }}
            onClick={toggleSidebar}
            aria-label={collapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
          >
            <Menu size={16} />
          </button>
        </div>

        <nav className="sidebar-nav">
          {groupedSections.map(([section, items]) => (
            <div key={section}>
              {!collapsed && <div className="sidebar-section-label">{section}</div>}
              {items.map((item) => {
                const Icon = item.icon;
                return (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    className={({ isActive }) => `sidebar-link ${isActive ? 'active' : ''}`}
                    title={item.label}
                  >
                    <Icon size={16} style={{ flexShrink: 0 }} />
                    {!collapsed && <span>{item.label}</span>}
                  </NavLink>
                );
              })}
            </div>
          ))}
        </nav>
      </aside>

      <div className={`app-main ${collapsed ? 'sidebar-collapsed' : ''}`}>
        <header className="app-header">
          <div className="header-left" style={{ position: 'relative', flex: '0 1 320px' }}>
            <Search size={15} style={{ color: 'var(--color-text-muted)', position: 'absolute', left: 10 }} />
            <input
              type="search"
              className="input"
              style={{ paddingLeft: 32 }}
              placeholder="Đi tới nhanh (Tenants, API, Audit...)"
              aria-label="Tìm nhanh trang quản trị"
              value={quickSearch}
              onChange={(e) => setQuickSearch(e.target.value)}
            />
            {filteredQuickMatches.length > 0 && (
              <div
                className="panel"
                style={{
                  position: 'absolute',
                  top: 38,
                  left: 0,
                  right: 0,
                  boxShadow: 'var(--shadow-md)',
                  zIndex: 40,
                }}
              >
                {filteredQuickMatches.map((m) => (
                  <button
                    key={m.to}
                    type="button"
                    className="btn btn-ghost"
                    style={{ width: '100%', justifyContent: 'flex-start', borderRadius: 0 }}
                    onClick={() => {
                      setQuickSearch('');
                      navigate(m.to);
                    }}
                  >
                    {m.label}
                    <span className="mono" style={{ marginLeft: 'auto', color: 'var(--color-text-muted)' }}>
                      {m.to}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="header-right">
            <Badge variant={auth.tenantId ? 'success' : auth.isPlatformUser ? 'info' : 'neutral'}>
              {auth.tenantId ? 'Tenant Active' : auth.isPlatformUser ? 'Platform Scope' : 'Tenant Scope'}
            </Badge>

            {(auth.isPlatformUser || auth.accessibleTenants.length > 0) && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <Building2 size={15} style={{ color: 'var(--color-text-muted)' }} />
                <select
                  className="select"
                  style={{ width: 'auto', minWidth: 200, fontWeight: 500 }}
                  aria-label="Chọn ngữ cảnh tenant"
                  value={auth.tenantId ?? ''}
                  onChange={(e) => void handleTenantSwitch(e.target.value)}
                >
                  {auth.isPlatformUser && <option value="">🌐 Platform (Global — Chọn Tenant...)</option>}
                  {auth.accessibleTenants.map((t) => (
                    <option key={t.id} value={t.id}>
                      🏢 {t.code} — {t.name}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <button
              type="button"
              className="btn btn-ghost btn-sm"
              aria-label="Thông báo"
              title="Trung tâm thông báo"
              onClick={() => navigate('/settings?tab=notifications')}
            >
              <Bell size={16} />
            </button>

            <button
              type="button"
              className="btn btn-ghost btn-sm"
              aria-label="Đổi giao diện Sáng / Tối"
              title="Sáng / Tối"
              onClick={() => setTheme((t) => (t === 'light' ? 'dark' : 'light'))}
            >
              {theme === 'light' ? <Moon size={16} /> : <Sun size={16} />}
            </button>

            <button
              type="button"
              className="btn btn-ghost btn-sm"
              title="Phiên đăng nhập & Hồ sơ"
              onClick={() => navigate('/profile')}
            >
              <KeyRound size={15} />
              <span style={{ maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                {auth.email ?? 'Admin'}
              </span>
            </button>

            <button
              type="button"
              className="btn btn-sm"
              onClick={() => void handleLogout()}
              aria-label="Đăng xuất"
            >
              <LogOut size={14} /> Đăng xuất
            </button>
          </div>
        </header>

        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
