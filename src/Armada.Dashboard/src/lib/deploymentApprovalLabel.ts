import type { InboxItem } from '../types/models';

/** Translator signature shared with `useLocale().t`. */
export type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;

/** Catalog keys; mirror `Armada.Core.Models.DeploymentApprovalLabel` so the server, TUI, and dashboard read the same. */
export const DEPLOYMENT_APPROVAL_TEMPLATES = {
  environmentAndTitle: 'Deploy to {{environment}}: {{title}}',
  environmentOnly: 'Deploy to {{environment}}',
  titleOnly: 'Deploy: {{title}}',
  unnamed: 'Deployment',
} as const;

function clean(value: string | null | undefined): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

/**
 * The label every surface uses for a deployment approval: environment name first, deployment title second
 * ("Deploy to production: Release 2.3 hotfix"). A missing title leaves the environment alone ("Deploy to
 * production"); a missing environment leads with the title, or the deployment id when the title is missing too.
 */
export function deploymentApprovalLabel(
  t: Translate,
  environmentName: string | null | undefined,
  title: string | null | undefined,
  id?: string | null,
): string {
  const environment = clean(environmentName);
  const name = clean(title);
  if (environment && name) return t(DEPLOYMENT_APPROVAL_TEMPLATES.environmentAndTitle, { environment, title: name });
  if (environment) return t(DEPLOYMENT_APPROVAL_TEMPLATES.environmentOnly, { environment });
  const fallback = name ?? clean(id);
  if (fallback) return t(DEPLOYMENT_APPROVAL_TEMPLATES.titleOnly, { title: fallback });
  return t(DEPLOYMENT_APPROVAL_TEMPLATES.unnamed);
}

/**
 * Display title of an inbox item. A deployment approval is rebuilt from the typed environmentName and
 * deploymentTitle fields so it is localized; an item from an older server without them uses entityName as the
 * environment (unless it is just the deployment id). Every other kind shows the server's title.
 */
export function inboxItemTitle(t: Translate, item: InboxItem): string {
  if (item.kind !== 'deployment_approval') return item.title;
  let environment = item.environmentName ?? null;
  if (!clean(environment) && !clean(item.deploymentTitle) && item.entityName && item.entityName !== item.entityId) {
    environment = item.entityName;
  }
  return deploymentApprovalLabel(t, environment, item.deploymentTitle, item.entityId);
}
