import { BreadcrumbTrail } from './Breadcrumbs';
import { useDashboardBreadcrumbTrail } from './BreadcrumbContext';
export { DashboardBreadcrumbProvider } from './BreadcrumbContext';

/** Rendered once by Layout, never individually positioned or styled by pages. */
export default function DashboardBreadcrumbs() {
  return <BreadcrumbTrail items={useDashboardBreadcrumbTrail()} />;
}
