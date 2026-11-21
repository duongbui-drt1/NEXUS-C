import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';

export interface BreadcrumbItem {
  label: string;
  to?: string;
}

export function PageHeader({
  title,
  subtitle,
  breadcrumbs,
  actions,
}: {
  title: string;
  subtitle?: string;
  breadcrumbs?: BreadcrumbItem[];
  actions?: ReactNode;
}) {
  return (
    <div>
      {breadcrumbs && breadcrumbs.length > 0 && (
        <nav className="breadcrumbs" aria-label="Breadcrumb">
          {breadcrumbs.map((item, idx) => (
            <span key={item.label} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              {idx > 0 && <ChevronRight size={12} />}
              {item.to ? <Link to={item.to}>{item.label}</Link> : <span>{item.label}</span>}
            </span>
          ))}
        </nav>
      )}
      <div className="page-header">
        <div>
          <h1 className="page-title">{title}</h1>
          {subtitle && <p className="page-subtitle">{subtitle}</p>}
        </div>
        {actions && <div className="page-actions">{actions}</div>}
      </div>
    </div>
  );
}
