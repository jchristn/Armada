import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactElement } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as client from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import Deployments from './Deployments';
import Incidents from './Incidents';
import Releases from './Releases';
import Environments from './Environments';
import Endpoints from './Endpoints';
import CheckRuns from './CheckRuns';
import History from './History';
import { asPage } from '../test/asPage';

vi.mock('../api/client', () => ({
  // Deployments / Incidents / Releases / Environments
  listDeployments: vi.fn(), createDeployment: vi.fn(), updateDeployment: vi.fn(), deleteDeployment: vi.fn(),
  listIncidents: vi.fn(), createIncident: vi.fn(), deleteIncident: vi.fn(),
  listReleases: vi.fn(), createRelease: vi.fn(), updateRelease: vi.fn(), deleteRelease: vi.fn(),
  listEnvironments: vi.fn(), createEnvironment: vi.fn(), updateEnvironment: vi.fn(), deleteEnvironment: vi.fn(),
  listVessels: vi.fn(), listWorkflowProfiles: vi.fn(),
  // Endpoints
  listModelEndpoints: vi.fn(), enumerateModelEndpoints: vi.fn(), createModelEndpoint: vi.fn(), updateModelEndpoint: vi.fn(), deleteModelEndpoint: vi.fn(),
  validateModelEndpoint: vi.fn(), healthCheckModelEndpoints: vi.fn(),
  // Checks
  listCheckRuns: vi.fn(), runCheck: vi.fn(), getVesselReadiness: vi.fn(), previewWorkflowProfileForVessel: vi.fn(),
  // History
  enumerateHistoryTimeline: vi.fn(), listObjectives: vi.fn(), deleteRequestHistoryEntry: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ isAdmin: true, isTenantAdmin: true, user: { user: { id: 'usr_1', tenantId: 'ten_1' } } }),
}));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

const NOW = '2026-10-01T00:00:00Z';

beforeEach(() => {
  localStorage.clear();
  const empty = page([]);
  for (const fn of [client.listDeployments, client.listIncidents, client.listReleases, client.listEnvironments, client.listVessels,
    client.listWorkflowProfiles, client.listCheckRuns, client.listObjectives]) {
    vi.mocked(fn).mockResolvedValue(empty);
  }
  vi.mocked(client.enumerateHistoryTimeline).mockResolvedValue(page([]));
  vi.mocked(client.listModelEndpoints).mockResolvedValue([] as never);
  vi.mocked(client.enumerateModelEndpoints).mockImplementation((async () => asPage(await client.listModelEndpoints())) as never);
});

afterEach(() => vi.clearAllMocks());

async function renderPage(ui: ReactElement, rowText: string) {
  const view = render(<MemoryRouter>{ui}</MemoryRouter>);
  await screen.findAllByText(rowText);
  return view;
}

/** The shared table chrome: refresh controls live in the table toolbar (not the page header), and the identity
 *  column is locked in the column chooser. */
async function expectTableChrome(container: HTMLElement, identity: string) {
  const bar = container.querySelector('.data-table > .pagination-bar') as HTMLElement;
  expect(bar).not.toBeNull();
  expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
  const header = container.querySelector('.page-header, .view-header') as HTMLElement | null;
  if (header) expect(within(header).queryByLabelText('Auto-refresh interval')).not.toBeInTheDocument();
  const chooser = within(bar).getByRole('button', { name: /^Columns/ });
  await act(async () => { fireEvent.click(chooser); });
  const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
  const item = within(menu).getByRole('menuitemcheckbox', { name: new RegExp(`^${identity}`) });
  expect(item).toHaveAttribute('aria-disabled', 'true');
  expect(item).toHaveAttribute('aria-checked', 'true');
  await act(async () => { fireEvent.keyDown(item, { key: 'Escape' }); });
}

/** The cell holding `text` renders it on one line and carries `detail` in its tooltip instead of a second line. */
function expectOneLineWithTooltip(text: string, detail: string) {
  const strong = screen.getByText(text, { selector: 'strong' });
  expect(strong).toHaveClass('cell-one-line');
  const cell = strong.closest('td') as HTMLElement;
  expect(cell.getAttribute('title') ?? '').toContain(detail);
  expect(within(cell).queryByText(detail)).not.toBeInTheDocument();
}

describe('Batch C tables on the shared DataTable', () => {
  it('Deployments: one-line title, ID and source ref in their own columns, summary in the tooltip', async () => {
    vi.mocked(client.listDeployments).mockResolvedValue(page([{
      id: 'dpl_1', title: 'Ship it', summary: 'Long deployment summary', sourceRef: 'release/1.0', approvalRequired: true,
      status: 'Running', verificationStatus: 'NotRun', vesselId: null, environmentId: null, environmentName: 'prod',
      releaseId: null, checkRunIds: [], lastUpdateUtc: NOW,
    }]));
    const { container } = await renderPage(<Deployments />, 'Ship it');
    expectOneLineWithTooltip('Ship it', 'Long deployment summary');
    expect(screen.getByTitle('dpl_1')).toHaveTextContent('dpl_1');
    expect(screen.getByText('release/1.0')).toHaveClass('cell-one-line');
    await expectTableChrome(container, 'Deployment');
  });

  it('Incidents: one-line title with the summary in the tooltip', async () => {
    vi.mocked(client.listIncidents).mockResolvedValue(page([{
      id: 'inc_1', title: 'Outage', summary: 'Database fell over', impact: null, status: 'Open', severity: 'High',
      environmentId: null, environmentName: null, deploymentId: null, releaseId: null, lastUpdateUtc: NOW,
    }]));
    const { container } = await renderPage(<Incidents />, 'Outage');
    expectOneLineWithTooltip('Outage', 'Database fell over');
    expect(screen.getByTitle('inc_1')).toBeInTheDocument();
    await expectTableChrome(container, 'Incident');
  });

  it('Releases: one-line title; version and linked work on one line each', async () => {
    // The title must not equal a status name: 'Candidate' also appears in the status filter, so waiting for it raced the load.
    vi.mocked(client.listReleases).mockResolvedValue(page([{
      id: 'rel_1', title: 'Spring launch', version: '1.2.3', tagName: 'v1.2.3', summary: 'Release notes summary', status: 'Draft',
      vesselId: null, workflowProfileId: null, voyageIds: ['a'], missionIds: [], checkRunIds: [], artifacts: [],
      publishedUtc: null, lastUpdateUtc: NOW,
    }]));
    const { container } = await renderPage(<Releases />, 'Spring launch');
    expectOneLineWithTooltip('Spring launch', 'Release notes summary');
    const version = screen.getByText('1.2.3');
    expect(version).toHaveClass('cell-one-line');
    expect(version.closest('td')).toHaveAttribute('title', 'v1.2.3');
    expect(screen.getByText('1 voyages, 0 missions, 0 checks, 0 artifacts')).toHaveClass('cell-one-line');
    await expectTableChrome(container, 'Release');
  });

  it('Environments: one-line name; state on one line in its own column', async () => {
    vi.mocked(client.listEnvironments).mockResolvedValue(page([{
      id: 'env_1', name: 'production', description: 'Customer facing', kind: 'Production', vesselId: null,
      baseUrl: 'https://prod.example.com/very/long/path', healthEndpoint: null, requiresApproval: true,
      isDefault: true, active: true, lastUpdateUtc: NOW,
    }]));
    const { container } = await renderPage(<Environments />, 'production');
    expectOneLineWithTooltip('production', 'Customer facing');
    expect(screen.getByText('Active \u2022 Default target').closest('td')).toHaveClass('cell-nowrap');
    await expectTableChrome(container, 'Environment');
  });

  it('Endpoints: base URL moved from a third line under the name into its own one-line column', async () => {
    vi.mocked(client.listModelEndpoints).mockResolvedValue(([{
      id: 'mep_1', name: 'embedder', enabled: true, kind: 'Embedding', provider: 'Ollama', model: 'nomic',
      baseUrl: 'http://localhost:11434/a/very/long/base/url', scope: 'Tenant', tenantId: 'ten_1', userId: 'usr_1',
      healthStatus: 'Healthy', healthHistory: [], lastHealthCheckUtc: null,
    }]) as never);
    const { container } = await renderPage(<Endpoints />, 'embedder');
    const nameCell = screen.getByText('embedder', { selector: 'strong' }).closest('td') as HTMLElement;
    expect(within(nameCell).queryByText('http://localhost:11434/a/very/long/base/url')).not.toBeInTheDocument();
    const urlCell = screen.getByText('http://localhost:11434/a/very/long/base/url').closest('td') as HTMLElement;
    expect(urlCell).toHaveAttribute('data-col', 'baseUrl');
    expect(urlCell).toHaveAttribute('title', 'http://localhost:11434/a/very/long/base/url');
    await expectTableChrome(container, 'Endpoint');
  });

  it('Checks: one-line check label; type and parsed results in their own columns', async () => {
    vi.mocked(client.listCheckRuns).mockResolvedValue(page([{
      id: 'chk_1', label: 'Nightly', type: 'UnitTest', source: 'Armada', status: 'Passed', vesselId: null,
      environmentName: null, durationMs: 12, createdUtc: NOW, lastUpdateUtc: NOW, artifacts: [],
      testSummary: { passed: 10, failed: 0, skipped: 0 },
    }]));
    const { container } = await renderPage(<CheckRuns />, 'Nightly');
    expectOneLineWithTooltip('Nightly', 'UnitTest');
    expect(screen.getByText('10 passed, 0 failed')).toHaveClass('cell-one-line');
    await expectTableChrome(container, 'Check');
  });

  it('History: one-line title with the description in the tooltip', async () => {
    vi.mocked(client.enumerateHistoryTimeline).mockResolvedValue(page([{
      id: 'h_1', title: 'Deploy finished', description: 'Took a long while to finish', sourceType: 'Deployment',
      status: 'Succeeded', severity: 'Info', actorDisplay: 'joel', vesselId: null, route: null, metadataJson: null,
      occurredUtc: NOW,
    }]));
    const { container } = await renderPage(<History />, 'Deploy finished');
    expectOneLineWithTooltip('Deploy finished', 'Took a long while to finish');
    await expectTableChrome(container, 'Title');
  });


  it('deployment copies remain unexecuted drafts until reviewed and saved', async () => {
    vi.mocked(client.listDeployments).mockResolvedValue(page([{ id: 'dpl_copy_source', title: 'Production rollout', sourceRef: 'release/1', status: 'Succeeded', verificationStatus: 'NotRun', approvalRequired: true, environmentName: 'prod', lastUpdateUtc: NOW, checkRunIds: [] }]));
    await renderPage(<Deployments />, 'Production rollout');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    expect(screen.getByLabelText('Title')).toHaveValue('Production rollout (Copy)');
    expect(client.createDeployment).not.toHaveBeenCalled();
    const autoExecute = screen.getByRole('checkbox', { name: /execute/i });
    expect(autoExecute).not.toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(client.createDeployment).not.toHaveBeenCalled();
  });

  it('release copies reset publication identity and status', async () => {
    vi.mocked(client.listReleases).mockResolvedValue(page([{ id: 'rel_source', title: 'Published release', version: '1.0.0', tagName: 'v1.0.0', status: 'Published', voyageIds: ['vyg_1'], missionIds: [], checkRunIds: [], artifacts: [], lastUpdateUtc: NOW }]));
    await renderPage(<Releases />, 'Published release');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    expect(screen.getByLabelText('Title')).toHaveValue('Published release (Copy)');
    expect(screen.getByLabelText('Version')).toHaveValue('');
    expect(screen.getByLabelText('Tag Name')).toHaveValue('');
    expect(screen.getAllByLabelText('Status').some(el => (el as HTMLSelectElement).value === 'Draft')).toBe(true);
    expect(client.createRelease).not.toHaveBeenCalled();
  });

  it('incident copies start open and preserve the selected context', async () => {
    vi.mocked(client.listIncidents).mockResolvedValue(page([{ id: 'inc_source', title: 'Old outage', status: 'Resolved', severity: 'High', summary: 'Root symptom', impact: 'Service unavailable', lastUpdateUtc: NOW }]));
    await renderPage(<Incidents />, 'Old outage');
    fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    expect(screen.getByLabelText('Title')).toHaveValue('Old outage (Copy)');
    expect(screen.getByLabelText('Summary')).toHaveValue('Root symptom');
    expect(screen.getAllByLabelText('Status').some(el => (el as HTMLSelectElement).value === 'Open')).toBe(true);
    expect(client.createIncident).not.toHaveBeenCalled();
  });
});
