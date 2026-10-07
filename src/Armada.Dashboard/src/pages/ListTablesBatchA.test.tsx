import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactElement } from 'react';
import Fleets from './Fleets';
import Docks from './Docks';
import Personas from './Personas';
import Pipelines from './Pipelines';
import Voyages from './Voyages';
import Captains from './Captains';
import Signals from './Signals';
import Events from './Events';
import {
  listCaptains, listDocks, listEvents, listFleets, listModelEndpoints, listPersonas, listPipelines,
  listPromptTemplates, listSignals, listVessels, listVoyages,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    listFleets: vi.fn(), listVessels: vi.fn(), listPipelines: vi.fn(), listDocks: vi.fn(), listCaptains: vi.fn(),
    listPersonas: vi.fn(), listPromptTemplates: vi.fn(), listVoyages: vi.fn(), listSignals: vi.fn(), listEvents: vi.fn(),
    listModelEndpoints: vi.fn(),
  };
});

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true, isAdmin: true, user: { user: { id: 'usr_1', tenantId: 'default' } } }) }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

const CREATED = '2026-10-01T00:00:00Z';
const LONG = 'A very long description that keeps going well past the width of any reasonable table column in the dashboard';

beforeEach(() => {
  localStorage.clear();
  vi.mocked(listFleets).mockResolvedValue(page([{ id: 'flt_1', name: 'core', description: LONG, active: true, createdUtc: CREATED }]));
  vi.mocked(listVessels).mockResolvedValue(page([]));
  vi.mocked(listPipelines).mockResolvedValue(page([{ id: 'ppl_1', name: 'Reviewed', description: LONG, stages: [], scope: 'Tenant', isBuiltIn: false, active: true, createdUtc: CREATED }]));
  vi.mocked(listDocks).mockResolvedValue(page([{ id: 'dck_1', vesselId: null, captainId: null, branchName: 'armada/feature-x', worktreePath: '/tmp/w', active: true, createdUtc: CREATED }]));
  vi.mocked(listCaptains).mockResolvedValue(page([{ id: 'cpt_1', name: 'alpha', runtime: 'ClaudeCode', state: 'Idle', currentMissionId: null, lastHeartbeatUtc: CREATED, createdUtc: CREATED }]));
  vi.mocked(listPersonas).mockResolvedValue(page([{ id: 'prs_1', name: 'Worker', description: LONG, promptTemplateName: 'persona.worker', scope: 'Tenant', isBuiltIn: true, active: true, createdUtc: CREATED }]));
  vi.mocked(listPromptTemplates).mockResolvedValue(page([]));
  vi.mocked(listVoyages).mockResolvedValue(page([
    { id: 'vyg_1', title: 'Gateway hardening', status: 'InProgress', landingMode: 'MergeAndPush', createdUtc: CREATED },
    { id: 'vyg_2', title: 'Docs sweep', status: 'InProgress', landingMode: null, createdUtc: CREATED },
  ]));
  vi.mocked(listSignals).mockResolvedValue(page([{ id: 'sig_1', type: 'Nudge', fromCaptainId: null, toCaptainId: null, read: false, payload: 'hello', createdUtc: CREATED }]));
  vi.mocked(listEvents).mockResolvedValue(page([{ id: 'evt_1', eventType: 'mission.created', entityType: 'mission', entityId: 'msn_1', captainId: null, missionId: 'msn_1', vesselId: null, voyageId: 'vyg_1', message: 'Mission created', createdUtc: CREATED }]));
  vi.mocked(listModelEndpoints).mockResolvedValue(page([]));
});

interface Case {
  name: string;
  element: ReactElement;
  rowText: string;
  locked: string[];
}

const CASES: Case[] = [
  { name: 'Fleets', element: <Fleets />, rowText: 'core', locked: ['Name', 'ID'] },
  { name: 'Docks', element: <Docks />, rowText: 'dck_1', locked: ['ID'] },
  { name: 'Personas', element: <Personas />, rowText: 'Worker', locked: ['Name', 'ID'] },
  { name: 'Pipelines', element: <Pipelines />, rowText: 'Reviewed', locked: ['Name', 'ID'] },
  { name: 'Voyages', element: <Voyages />, rowText: 'Gateway hardening', locked: ['Title', 'ID'] },
  { name: 'Captains', element: <Captains />, rowText: 'alpha', locked: ['Name', 'ID'] },
  { name: 'Signals', element: <Signals />, rowText: 'sig_1', locked: ['ID'] },
  { name: 'Events', element: <Events />, rowText: 'evt_1', locked: ['ID'] },
];

describe('batch A list tables use the shared DataTable', () => {
  for (const c of CASES) {
    it(`${c.name}: one toolbar row holds auto-refresh, refresh and the column chooser; identity columns are locked`, async () => {
      const { container } = render(<MemoryRouter>{c.element}</MemoryRouter>);
      await screen.findAllByText(c.rowText);
      const bars = container.querySelectorAll('.pagination-bar');
      expect(bars).toHaveLength(1);
      const bar = bars[0] as HTMLElement;
      expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
      expect(bar.querySelector('.refresh-btn')).not.toBeNull();
      // Nothing refresh-related is left in the page header.
      expect(container.querySelector('.page-header .refresh-btn')).toBeNull();
      expect(container.querySelector('.page-header .auto-refresh-select')).toBeNull();

      const trigger = within(bar).getByRole('button', { name: /^Columns/ });
      await act(async () => { fireEvent.click(trigger); });
      const menu = screen.getByRole('menu');
      for (const label of c.locked) {
        const item = within(menu).getByRole('menuitemcheckbox', { name: new RegExp(`^${label}`) });
        expect(item).toHaveAttribute('aria-disabled', 'true');
      }
      await act(async () => { fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' }); });
    });
  }

  it('keeps the toolbar (and refresh) when the list is empty', async () => {
    vi.mocked(listFleets).mockResolvedValue(page([]));
    const { container } = render(<MemoryRouter><Fleets /></MemoryRouter>);
    await screen.findByText('No fleets configured.');
    expect(container.querySelector('.pagination-bar .refresh-btn')).not.toBeNull();
  });
});

describe('batch A space fixes', () => {
  it('Voyages renders the landing mode on one line with the summary and description in the tooltip', async () => {
    render(<MemoryRouter><Voyages /></MemoryRouter>);
    const row = (await screen.findByText('Gateway hardening')).closest('tr')!;
    const mode = within(row).getByText('MergeAndPush');
    expect(mode).toHaveClass('cell-one-line');
    expect(mode.getAttribute('title')).toContain('local + push');
    const inherited = within(screen.getByText('Docs sweep').closest('tr')!).getByText('Default');
    expect(inherited).toHaveClass('cell-one-line');
    expect(inherited.getAttribute('title')).toContain('vessel or global default');
  });

  it.each([
    ['Fleets', <Fleets />],
    ['Personas', <Personas />],
    ['Pipelines', <Pipelines />],
  ])('%s truncates the description to one line with the full text in the title', async (_name, element) => {
    render(<MemoryRouter>{element}</MemoryRouter>);
    const cell = await screen.findByText(LONG);
    expect(cell).toHaveClass('truncate-text');
    expect(cell).toHaveAttribute('title', LONG);
  });

  it('Events hides the Mission and Voyage ID columns by default and the chooser can show them', async () => {
    render(<MemoryRouter><Events /></MemoryRouter>);
    await screen.findByText('evt_1');
    const headers = () => screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers()).not.toContain('Mission');
    expect(headers()).not.toContain('Voyage');
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /^Columns/ })); });
    fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitemcheckbox', { name: /^Voyage/ }));
    expect(headers()).toContain('Voyage');
    expect(screen.getByTitle('vyg_1')).toBeInTheDocument();
  });

  it('Captains keeps the state badge and time cells on one line', async () => {
    render(<MemoryRouter><Captains /></MemoryRouter>);
    const row = (await screen.findByText('alpha')).closest('tr')!;
    expect(row.querySelector('td[data-col="state"]')).toHaveClass('cell-nowrap');
    expect(row.querySelector('td[data-col="heartbeat"]')).toHaveClass('cell-nowrap');
  });

  it('Signals moved the pager, auto-refresh and refresh out of a separate wrapper line into the table toolbar', async () => {
    const { container } = render(<MemoryRouter><Signals /></MemoryRouter>);
    await screen.findByText('sig_1');
    const bar = container.querySelector('.data-table > .pagination-bar') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(within(bar).getByRole('button', { name: 'Next' })).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
  });
});
