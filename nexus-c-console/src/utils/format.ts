/**
 * Non-sensitive UI preference storage only.
 * NEVER store tokens, passwords, API keys, or PII here.
 */
const THEME_KEY = 'nexus_ui_theme';
const SIDEBAR_KEY = 'nexus_ui_sidebar_collapsed';

export type ThemeMode = 'light' | 'dark';

export const uiPreferences = {
  getTheme(): ThemeMode {
    try {
      const val = window.localStorage.getItem(THEME_KEY);
      return val === 'dark' ? 'dark' : 'light';
    } catch {
      return 'light';
    }
  },

  setTheme(mode: ThemeMode): void {
    try {
      window.localStorage.setItem(THEME_KEY, mode);
    } catch {
      // Ignore storage quota errors
    }
  },

  getSidebarCollapsed(): boolean {
    try {
      return window.localStorage.getItem(SIDEBAR_KEY) === '1';
    } catch {
      return false;
    }
  },

  setSidebarCollapsed(collapsed: boolean): void {
    try {
      window.localStorage.setItem(SIDEBAR_KEY, collapsed ? '1' : '0');
    } catch {
      // Ignore
    }
  },
};

export function formatDateTime(iso?: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('vi-VN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(d);
}

export function shortId(id?: string | null): string {
  if (!id) return '—';
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
