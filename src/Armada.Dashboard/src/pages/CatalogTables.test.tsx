import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as client from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import Playbooks from './Playbooks';
import Skills from './Skills';
import Runbooks from './Runbooks';
import ProjectProfiles from './ProjectProfiles';
import WorkflowProfiles from './WorkflowProfiles';
import Harbors from './Harbors';
import Objectives from './Objectives';

// Shared-DataTable migrations for the catalog-style list pages (playbooks, skills, runbooks, profiles, harbors) and
// the backlog: the column chooser is present with the identity columns locked, refresh lives in the table toolbar,
// and values that used to stack under the name are one-line columns of their own.

vi.mock('../api/client', () => ({
  listPlaybooks: vi.fn(), createPlaybook: vi.fn(), updatePlaybook: vi.fn(), deletePlaybook: vi.fn(),
  listSkills: vi.fn(), createSkill: vi.fn(), updateSkill: vi.fn(), deleteSkill: vi.fn(),
  listRunbooks: vi.fn(), listRunbookExecutions: vi.fn(), listEnvironments: vi.fn(), createRunbook: vi.fn(), deleteRunbook: vi.fn(),
  listWorkflowProfiles: vi.fn(), createWorkflowProfile: vi.fn(), updateWorkflowProfile: vi.fn(), deleteWorkflowProfile: vi.fn(),
  listProjectProfiles: vi.fn(), createProjectProfile: vi.fn(), updateProjectProfile: vi.fn(), deleteProjectProfile: vi.fn(),
  listHarbors: vi.fn(), createHarbor: vi.fn(), updateHarbor: vi.fn(), deleteHarbor: vi.fn(), enableHarbor: vi.fn(), disableHarbor: vi.fn(), getHarborMetrics: vi.fn(),
  listBacklog: vi.fn(), createBacklogItem: vi.fn(), deleteBacklogItem: vi.fn(), importObjectiveFromGitHub: vi.fn(), reorderBacklog: vi.fn(),
  listFleets: vi.fn(), listVessels: vi.fn(), listUsers: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ isAdmin: true, isTenantAdmin: true, user: { user: { id: 'usr_1', tenantId: 'ten_1' } } }),
}));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

const NOW = '2026-10-01T00:00:00Z';
const LONG = 'A long description that would otherwise wrap onto a second and third line under the name of the record';

const playbook = { id: 'pbk_1', fileName: 'CSHARP_RULES.md', description: LONG, content: '# rules', active: true, scope: 'Tenant', tenantId: 'ten_1', lastUpdateUtc: NOW, createdUtc: NOW };
const skill = { id: 'skl_1', name: 'git-hygiene', category: 'engineering', description: LONG, content: 'x', active: true, scope: 'Tenant', tenantId: 'ten_1', lastUpdateUtc: NOW };
const runbook = {
  id: 'rbk_1', title: 'Roll back', fileName: 'ROLLBACK.md', description: LONG, active: true, scope: 'Tenant', tenantId: 'ten_1',
  steps: [{}, {}], parameters: [{}], workflowProfileId: null, environmentId: null, environmentName: 'staging', defaultCheckType: 'Deploy', lastUpdateUtc: NOW,
};
const projectProfile = {
  id: 'ppf_1', name: 'web-app', description: LONG, scope: 'Global', isDefault: true, ownershipScope: 'Tenant', tenantId: 'ten_1',
  personaOverrides: [], skills: [], active: true, lastUpdateUtc: NOW,
};
const workflowProfile = {
  id: 'wfp_1', name: 'dotnet', description: LONG, scope: 'Global', isDefault: true, ownershipScope: 'Tenant', tenantId: 'ten_1',
  buildCommand: 'dotnet build', environments: [], active: true, lastUpdateUtc: NOW,
};
const harbor = {
  id: 'hbr_1', name: 'laptop', enabled: false, connectionStatus: 'Connected', maxConcurrentJobs: 2, lastSeenUtc: NOW,
  capabilities: [{ name: 'git', available: true }, { name: 'claude', available: true }, { name: 'codex', available: true }],
};
const objective = {
  id: 'obj_1', title: 'Ship the thing', description: LONG, owner: 'ada', category: 'platform', targetVersion: '2.0', rank: 1,
  kind: 'Feature', priority: 'P1', effort: 'M', status: 'Draft', backlogState: 'Inbox', blockedByObjectiveIds: ['obj_0'],
  vesselIds: [], fleetIds: [], dueUtc: null, lastUpdateUtc: NOW, createdUtc: NOW, tags: [], acceptanceCriteria: [], nonGoals: [],
  rolloutConstraints: [], evidenceLinks: [], releaseIds: [], deploymentIds: [], incidentIds: [], planningSessionIds: [], voyageIds: [], missionIds: [],
  checkRunIds: [], refinementSessionIds: [],
};

beforeEach(() => {
  localStorage.clear();
  const m = vi.mocked(client);
  m.listPlaybooks.mockResolvedValue(page([playbook]) as never);
  m.listSkills.mockResolvedValue(page([skill]) as never);
  m.listRunbooks.mockResolvedValue(page([runbook]) as never);
  m.listRunbookExecutions.mockResolvedValue(page([]) as never);
  m.listEnvironments.mockResolvedValue(page([]) as never);
  m.listWorkflowProfiles.mockResolvedValue(page([workflowProfile]) as never);
  m.listProjectProfiles.mockResolvedValue(page([projectProfile]) as never);
  m.listHarbors.mockResolvedValue([harbor] as never);
  m.listBacklog.mockResolvedValue(page([objective]) as never);
  m.listFleets.mockResolvedValue(page([]) as never);
  m.listVessels.mockResolvedValue(page([]) as never);
});

function renderPage(element: ReactElement) {
  return render(<MemoryRouter>{element}</MemoryRouter>);
}

function headers(): string[] {
  return screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
}

async function expectChooser(locked: string[], refreshTitle: string) {
  const bar = document.querySelector('.data-table .pagination-bar') as HTMLElement;
  expect(within(bar).getByTitle(refreshTitle)).toBeInTheDocument();
  expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
  fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ }));
  const menu = await screen.findByRole('menu', { name: 'Choose visible columns' });
  for (const name of locked) {
    expect(within(menu).getByRole('menuitemcheckbox', { name: new RegExp(`^${name}`) })).toHaveAttribute('aria-disabled', 'true');
  }
  // The page header no longer carries the auto-refresh control.
  expect(screen.getAllByLabelText('Auto-refresh interval')).toHaveLength(1);
}

function expectOneLine(text: string, cellKey: string) {
  const el = screen.getByTitle(text);
  expect(el.closest('td')).toHaveAttribute('data-col', cellKey);
  expect(el.className).toMatch(/truncate-text|cell-one-line|id-value/);
}

describe('catalog list tables on the shared DataTable', () => {
  it('Playbooks: File and ID locked; the ID is its own column instead of a line under the file name', async () => {
    renderPage(<Playbooks />);
    await screen.findByText('CSHARP_RULES.md');
    expect(headers()).toEqual(['File', 'ID', 'Description', 'Visibility', 'Status', 'Content', 'Last Updated', 'Actions']);
    expect(screen.getByText('CSHARP_RULES.md').closest('td')).not.toHaveTextContent('pbk_1');
    expectOneLine('pbk_1', 'id');
    await expectChooser(['File', 'ID'], 'Refresh playbooks');
  });

  it('Skills: the ID and description are their own one-line columns', async () => {
    renderPage(<Skills />);
    await screen.findByText('git-hygiene');
    expect(screen.getByText('git-hygiene').closest('td')).not.toHaveTextContent(LONG);
    expectOneLine(LONG, 'description');
    expectOneLine('skl_1', 'id');
    await expectChooser(['Skill', 'ID'], 'Refresh skills');
  });

  it('Runbooks: the four-line runbook cell and three-line binding cell are split into columns', async () => {
    renderPage(<Runbooks />);
    await screen.findByText('Roll back');
    const shown = headers();
    expect(shown).toEqual(expect.arrayContaining(['Runbook', 'ID', 'Status', 'Workflow Profile', 'Environment', 'Steps', 'Executions']));
    // Less essential values start hidden (chooser turns them on).
    expect(shown).not.toContain('File Name');
    expect(shown).not.toContain('Default Check Type');
    expect(screen.getByText('Roll back').closest('td')).toHaveTextContent(/^Roll back$/);
    expectOneLine('staging', 'environment');
    await expectChooser(['Runbook', 'ID'], 'Refresh runbooks');
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: /^File Name/ }));
    expect(headers()).toContain('File Name');
  });

  it('Project profiles: "Default" sits beside the scope badge, not under it', async () => {
    renderPage(<ProjectProfiles />);
    await screen.findByText('web-app');
    const scopeCell = document.querySelector('tbody td[data-col="scope"]') as HTMLElement;
    expect(scopeCell).toHaveClass('cell-nowrap');
    expect(scopeCell.querySelector('div')).toBeNull();
    expect(scopeCell).toHaveTextContent('Default');
    expect(headers()).not.toContain('Description');
    await expectChooser(['Profile', 'ID'], 'Refresh project profiles');
  });

  it('Workflow profiles: one-line scope and a separate ID column', async () => {
    renderPage(<WorkflowProfiles />);
    await screen.findByText('dotnet');
    const scopeCell = document.querySelector('tbody td[data-col="scope"]') as HTMLElement;
    expect(scopeCell.querySelector('div')).toBeNull();
    expectOneLine('wfp_1', 'id');
    await expectChooser(['Profile', 'ID'], 'Refresh workflow profiles');
  });

  it('Harbors: capabilities truncate on one line with the full list in the title', async () => {
    renderPage(<Harbors />);
    // The name is also an option of the page's chart picker, so read it from the table cell.
    await screen.findByText('laptop', { selector: 'td strong' });
    expectOneLine('git, claude, codex', 'capabilities');
    expect(screen.getByText('laptop', { selector: 'td strong' }).closest('td')).not.toHaveTextContent('hbr_1');
    await expectChooser(['Harbor', 'ID'], 'Refresh harbors');
  });

  it('Backlog: item, scope and due cells are one line each; refresh moved into the table toolbar', async () => {
    renderPage(<Objectives />);
    await screen.findByText('Ship the thing');
    const shown = headers();
    expect(shown).toEqual(expect.arrayContaining(['Rank', 'Backlog Item', 'ID', 'Owner', 'Shape', 'State', 'Vessels', 'Fleets', 'Due', 'Last Updated']));
    expect(shown).not.toContain('Description');
    expect(screen.getByText('Ship the thing').closest('td')).toHaveTextContent(/^Ship the thing$/);
    // The planning note is the tooltip of the empty vessel cell instead of a third line.
    expectOneLine('Needs a vessel before planning, dispatch, or release drafting can start.', 'vessels');
    // "Blocked by" stays on the state row.
    expect((document.querySelector('tbody td[data-col="state"]') as HTMLElement)).toHaveClass('cell-nowrap');
    await expectChooser(['Backlog Item', 'ID'], 'Refresh backlog');
  });

  it('keeps the toolbar and refresh when a list is empty', async () => {
    vi.mocked(client.listHarbors).mockResolvedValue([] as never);
    renderPage(<Harbors />);
    expect(await screen.findByText('No harbors match the current filters.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByTitle('Refresh harbors')).toBeInTheDocument();
  });
});
