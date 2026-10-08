/**
 * Accessibility sweep over every screen: each route file under src/app/(app) is mounted at its URL (signed in as an
 * admin) and audited with src/test/a11y.ts twice: with the server answering lists with an empty page (and single
 * items with 404), and with the server unreachable. Screens with data are audited too, by the global audit after
 * every other screen test (jest.a11y.js); this sweep makes sure no screen is left out, including its empty, loading,
 * and error chrome (headers, toolbars, filters, forms). Redirect-only routes are skipped (they render nothing).
 */
import fs from 'fs';
import path from 'path';
import { act, screen, waitFor } from '@testing-library/react-native';
import type { ComponentType } from 'react';
import * as client from '@dashboard/api/client';
import { DASHBOARD_ROUTES } from '../navigation/dashboardRoutes.generated';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { PushProvider } from '../push/PushContext';
import { auditAccessibility, formatIssues } from '../test/a11y';
import { renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const SYNC = new Set(['configureClient', 'setAuthToken', 'setOnUnauthorized']);

type Mode = 'empty' | 'offline';

/** Answers every server call the way `mode` describes (whoami is set by the harness when it signs in). */
function serve(mode: Mode): void {
  // An empty array that also looks like an empty page, so both list shapes (arrays and paged results) render empty.
  const empty = () => Object.assign([], { objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 25, success: true });
  for (const [name, value] of Object.entries(client as unknown as Record<string, unknown>)) {
    if (!jest.isMockFunction(value) || SYNC.has(name) || name === 'whoami') continue;
    const fn = value as jest.Mock;
    if (mode === 'offline') fn.mockImplementation(async () => { throw new client.NetworkError('Network request failed', null); });
    else if (/^(enumerate|list)/.test(name)) fn.mockImplementation(async () => empty());
    else fn.mockImplementation(async () => { throw new client.ApiError('Not found', 404, null); });
  }
}

const APP_DIR = path.join(__dirname, '..', 'app', '(app)');

/** Placeholder values for dynamic segments. */
const PARAMS: Record<string, string> = {
  id: 'x_1',
  name: 'x',
  threadId: 'thr_1',
  vesselId: 'vsl_1',
  panel: 'branches',
  operationId: 'GetHealth',
};

function routeFiles(dir: string): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...routeFiles(full));
    else if (entry.name.endsWith('.tsx') && entry.name !== '_layout.tsx') out.push(full);
  }
  return out;
}

interface SweepRoute {
  /** Route key for renderRouter, e.g. '(more)/personas/[name]'. */
  key: string;
  group: string;
  url: string;
  file: string;
}

const REDIRECTS = new Set(DASHBOARD_ROUTES.filter((r) => r.redirect).map((r) => r.file.replace(/^\(app\)\//, '').replace(/\.tsx$/, '')));

const ALL_ROUTES: SweepRoute[] = routeFiles(APP_DIR).sort().map((file) => {
  const key = path.relative(APP_DIR, file).replace(/\.tsx$/, '').split(path.sep).join('/');
  const group = key.split('/')[0];
  const url = '/' + key.split('/').slice(1)
    .filter((segment) => segment !== 'index')
    .map((segment) => segment.replace(/^\[(\w+)\]$/, (_, name: string) => PARAMS[name] ?? 'x'))
    .join('/');
  return { key, group, url, file };
});

const ROUTES = ALL_ROUTES.filter((r) => !REDIRECTS.has(r.key));

/** The providers the app shell adds around every signed-in screen, beyond the W4 harness's. */
function inShell(Screen: ComponentType): ComponentType {
  return function Shell() {
    return (
      <ApprovalsProvider enabled={false}>
        <PushProvider><Screen /></PushProvider>
      </ApprovalsProvider>
    );
  };
}

beforeEach(async () => {
  await resetW4();
});

it('finds every route file', () => {
  expect(ALL_ROUTES.length).toBeGreaterThanOrEqual(90);
  expect(ROUTES.length).toBeGreaterThan(50);
});

const CASES = ROUTES.flatMap((r) => (['empty', 'offline'] as Mode[]).map((mode) => [r.key, mode, r] as const));

it.each(CASES)('%s (%s) has no accessibility issues', async (_key, mode, route) => {
  serve(mode);
  // Route modules are loaded from the file list.
  const Screen = (require(route.file) as { default: ComponentType }).default;
  const Layout = (require(path.join(APP_DIR, route.group, '_layout.tsx')) as { default: ComponentType }).default;
  await renderW4Routes({ [`${route.group}/_layout`]: Layout, [route.key]: inShell(Screen) }, route.url);
  // Settle the initial loads (each mocked call resolves on the next tick).
  await waitFor(() => expect(screen.root).toBeTruthy());
  await act(async () => { await Promise.resolve(); });
  const issues = auditAccessibility(screen.root);
  if (issues.length > 0) throw new Error(`${route.url}:\n${formatIssues(issues)}`);
});
