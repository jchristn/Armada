import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import { translateTemplate, type I18nCatalog } from '../i18n/runtime';
import type { InboxItem } from '../types/models';
import { DEPLOYMENT_APPROVAL_TEMPLATES, deploymentApprovalLabel, inboxItemTitle } from './deploymentApprovalLabel';

const catalog = JSON.parse(catalogSource) as I18nCatalog;
const en = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params);

function inbox(overrides: Partial<InboxItem>): InboxItem {
  return {
    kind: 'deployment_approval',
    severity: 'Warning',
    title: 'server title',
    detail: 'detail',
    entityType: 'deployment',
    entityId: 'dpl_1',
    href: '/deployments/dpl_1',
    ...overrides,
  };
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('deployment approval label', () => {
  it('leads with the environment name, then the deployment title', () => {
    expect(deploymentApprovalLabel(en, 'production', 'Release 2.3 hotfix', 'dpl_1')).toBe('Deploy to production: Release 2.3 hotfix');
  });

  it('falls back gracefully when the environment or title is missing', () => {
    expect(deploymentApprovalLabel(en, null, 'Release 2.3 hotfix', 'dpl_1')).toBe('Deploy: Release 2.3 hotfix');
    expect(deploymentApprovalLabel(en, '  ', 'Release 2.3 hotfix', 'dpl_1')).toBe('Deploy: Release 2.3 hotfix');
    expect(deploymentApprovalLabel(en, 'production', '', 'dpl_1')).toBe('Deploy to production');
    expect(deploymentApprovalLabel(en, 'production', null, null)).toBe('Deploy to production');
    expect(deploymentApprovalLabel(en, null, null, 'dpl_1')).toBe('Deploy: dpl_1');
    expect(deploymentApprovalLabel(en, null, null, null)).toBe('Deployment');
  });

  it('renders through the catalog in every locale with the environment and title inserted verbatim', () => {
    for (const locale of Object.keys(catalog.locales)) {
      const t = (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate(locale, text, catalog, params);
      const label = deploymentApprovalLabel(t, 'production', 'Release 2.3 hotfix', 'dpl_1');
      expect(label, locale).not.toBe('Deploy to production: Release 2.3 hotfix');
      expect(label.indexOf('production'), locale).toBeGreaterThanOrEqual(0);
      expect(label.indexOf('production'), locale).toBeLessThan(label.indexOf('Release 2.3 hotfix'));
    }
  });

  it('has a translation with the same placeholders for every template in every locale', () => {
    const keys = Object.values(DEPLOYMENT_APPROVAL_TEMPLATES);
    for (const [locale, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        expect(value, `${locale}: ${key}`).toBeTruthy();
        expect(placeholders(value as string), `${locale}: ${key}`).toBe(placeholders(key));
      }
    }
  });
});

describe('inbox item title', () => {
  it('builds a deployment approval title from the typed fields instead of the server title', () => {
    const item = inbox({ title: 'Deployment awaiting approval: production', entityName: 'production', environmentName: 'production', deploymentTitle: 'Release 2.3 hotfix' });
    expect(inboxItemTitle(en, item)).toBe('Deploy to production: Release 2.3 hotfix');
  });

  it('uses entityName as the environment for an item from an older server, but never the bare id', () => {
    expect(inboxItemTitle(en, inbox({ entityName: 'Staging' }))).toBe('Deploy to Staging');
    expect(inboxItemTitle(en, inbox({ entityName: 'dpl_1' }))).toBe('Deploy: dpl_1');
  });

  it('leaves other kinds alone', () => {
    expect(inboxItemTitle(en, inbox({ kind: 'deployment_failed', title: 'Deployment failed: production', environmentName: 'production', deploymentTitle: 'x' })))
      .toBe('Deployment failed: production');
  });
});
