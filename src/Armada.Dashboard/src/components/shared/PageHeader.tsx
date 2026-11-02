import type { ReactNode } from 'react';
import Breadcrumbs, { type BreadcrumbItem } from './Breadcrumbs';

interface PageHeaderProps {
  title: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
  /** Optional breadcrumb rendered above the title (detail pages). */
  breadcrumb?: readonly BreadcrumbItem[];
}

/**
 * The single page/section header used across the dashboard. Replaces the three previous
 * variants (`view-header`, `page-header`, `detail-header`): title + optional subtitle on the
 * left, right-aligned actions, and an optional breadcrumb line above for detail pages.
 */
export default function PageHeader({ title, subtitle, actions, breadcrumb }: PageHeaderProps) {
  return (
    <>
      {breadcrumb && <Breadcrumbs items={breadcrumb} />}
      <div className="page-header">
        <div>
          <h2>{title}</h2>
          {subtitle && <p className="text-dim view-subtitle">{subtitle}</p>}
        </div>
        {actions && <div className="page-actions">{actions}</div>}
      </div>
    </>
  );
}
