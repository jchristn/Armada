import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import Captains from './Captains';
import { getUsageLatest, listCaptains } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Captain, UsageSnapshotView } from '../types/models';

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  listCaptains: vi.fn(),
  getUsageLatest: vi.fn(),
  enumerateModelEndpoints: vi.fn(() => Promise.resolve({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 100, success: true, totalMs: 0 })),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? `ago(${v})` : ''),
};
const auth = { isAdmin: false, isTenantAdmin: false, user: { id: 'usr_1' } };
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[], totalPages = 1) {
  return { success: true, pageNumber: 1, pageSize: 100, totalPages, totalRecords: objects.length, totalMs: 1, objects };
}

function captain(id: string, name: string, enabled: boolean): Captain {
  return {
    id, tenantId: 'default', name, runtime: 'ClaudeCode', model: null, state: 'Idle', allowedPersonas: null,
    createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
    usageCaptureEnabled: enabled, usageCaptureMode: 'Auto', usageCaptureIntervalSeconds: 300, usageCaptureActive: enabled,
  } as unknown as Captain;
}

const future = new Date(Date.now() + 3600_000).toISOString();
function view(captainIds: string[], status: UsageSnapshotView['status'], percent: number | null, stale = false): UsageSnapshotView {
  return {
    id: `uss_${captainIds[0]}`, tenantId: 'default', hostId: 'hbr_laptop', harborId: 'hbr_laptop', runtime: 'ClaudeCode', provider: 'anthropic',
    accountKey: 'acct_abcdef123456', captainIds, capturedUtc: '2026-11-01T10:00:00Z', status, errorCode: null,
    windows: percent === null ? [] : [{ name: 'five_hour', usedPercent: percent, used: null, limit: null, unit: 'percent', windowMinutes: 300, resetsUtc: future, model: null, scope: null, coverageNote: null }],
    stale, observedUtc: '2026-11-01T09:00:00Z', requestedIntervalSeconds: 300, effectiveIntervalSeconds: 300, capabilityReason: null,
  };
}

const captains = [
  captain('cpt_ok', 'ok-captain', true),
  captain('cpt_off', 'disabled-captain', false),
  captain('cpt_unsup', 'unsupported-captain', true),
  captain('cpt_none', 'nodata-captain', true),
  captain('cpt_fail', 'failed-captain', true),
];

function DetailProbe() {
  const location = useLocation();
  return <div data-testid="detail">{location.pathname}{location.search}</div>;
}

function renderCaptains() {
  render(
    <MemoryRouter initialEntries={['/captains']}>
      <Routes>
        <Route path="/captains" element={<Captains />} />
        <Route path="/captains/:id" element={<DetailProbe />} />
        <Route path="/activity" element={<DetailProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

function row(name: string): HTMLElement {
  return screen.getByText(name).closest('tr') as HTMLElement;
}

describe('Captains usage column and View Usage action', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listCaptains).mockReset().mockResolvedValue(page(captains) as never);
    vi.mocked(getUsageLatest).mockReset().mockResolvedValue(page([
      view(['cpt_ok'], 'Ok', 82),
      view(['cpt_unsup'], 'Unsupported', null),
      view(['cpt_fail'], 'Failed', 40, true),
    ]) as never);
  });

  it('hydrates the visible page with one batch latest request, not one request per row', async () => {
    renderCaptains();
    await screen.findByText('ok-captain');
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(1));
    const filter = vi.mocked(getUsageLatest).mock.calls[0][0] ?? {};
    expect((filter.captainIds ?? '').split(',').sort()).toEqual(['cpt_fail', 'cpt_none', 'cpt_off', 'cpt_ok', 'cpt_unsup']);
    expect(filter.pageNumber).toBe(1);
    expect(filter.captainId).toBeUndefined();
  });

  it('keeps each state distinct in the usage column and never shows zero for missing data', async () => {
    renderCaptains();
    await screen.findByText('ok-captain');
    await waitFor(() => expect(within(row('ok-captain')).getByText('82% used')).toBeInTheDocument());
    expect(within(row('ok-captain')).getByText('82% used').closest('a')?.getAttribute('title')).toContain('(Warning)');
    expect(within(row('disabled-captain')).getByText('Capture disabled')).toBeInTheDocument();
    expect(within(row('unsupported-captain')).getByText('Unsupported')).toBeInTheDocument();
    expect(within(row('nodata-captain')).getByText('No usage observed yet')).toBeInTheDocument();
    expect(within(row('failed-captain')).getByText('Capture failed')).toBeInTheDocument();
    for (const name of ['disabled-captain', 'unsupported-captain', 'nodata-captain']) {
      expect(within(row(name)).queryByText('0%')).toBeNull();
    }
  });

  it('renders readable quota labels with explicit word spacing inside the real table', async () => {
    const weekly = view(['cpt_ok'], 'Ok', 96);
    weekly.windows[0].name = 'seven_day';
    weekly.windows[0].windowMinutes = 10080;
    const primary = view(['cpt_fail'], 'Ok', 6);
    primary.windows[0].name = 'codex:primary';
    vi.mocked(getUsageLatest).mockResolvedValue(page([weekly, primary]) as never);
    renderCaptains();
    const week = await screen.findByRole('link', { name: '96% used · Weekly' });
    expect(week).toHaveClass('usage-level-danger');
    expect(week.textContent).toBe('96% used · Weekly');
    expect(week.title).toContain('Weekly at 96%');
    const codex = screen.getByRole('link', { name: '6% used · 5-hour limit' });
    expect(codex.textContent).toBe('6% used · 5-hour limit');
    expect(codex.title).not.toContain('codex:primary');
    expect(week.textContent).not.toContain('seven_day');
  });

  it('shows a retryable error instead of zero usage when the batch read fails', async () => {
    vi.mocked(getUsageLatest).mockReset().mockRejectedValueOnce(new Error('boom')).mockResolvedValue(page([view(['cpt_ok'], 'Ok', 50)]) as never);
    renderCaptains();
    await screen.findByText('ok-captain');
    const retry = await screen.findByRole('button', { name: 'Retry' });
    await userEvent.click(retry);
    await waitFor(() => expect(within(row('ok-captain')).getByText('50% used')).toBeInTheDocument());
    expect(getUsageLatest).toHaveBeenCalledTimes(2);
  });

  for (const [name] of [['ok-captain', 'cpt_ok'], ['disabled-captain', 'cpt_off'], ['unsupported-captain', 'cpt_unsup'], ['nodata-captain', 'cpt_none'], ['failed-captain', 'cpt_fail']] as const) {
    it(`View Usage opens Captain Usage from ${name}`, async () => {
      renderCaptains();
      await screen.findByText(name);
      await userEvent.click(within(row(name)).getByRole('button', { name: 'Actions' }));
      await userEvent.click(await screen.findByRole('menuitem', { name: 'View Usage' }));
      expect(await screen.findByTestId('detail')).toHaveTextContent('/activity?source=usage-overview');
    });
  }

  it('View Usage is reachable with the keyboard only and opens Captain Usage', async () => {
    const user = userEvent.setup();
    renderCaptains();
    await screen.findByText('unsupported-captain');
    const trigger = within(row('unsupported-captain')).getByRole('button', { name: 'Actions' });
    trigger.focus();
    await user.keyboard('{Enter}');
    const first = await screen.findByRole('menuitem', { name: 'View Detail' });
    await waitFor(() => expect(first).toHaveFocus());
    await user.keyboard('{ArrowDown}');
    expect(screen.getByRole('menuitem', { name: 'View Usage' })).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(await screen.findByTestId('detail')).toHaveTextContent('/activity?source=usage-overview');
  });

  it('labels the action exactly View Usage in English', async () => {
    renderCaptains();
    await screen.findByText('ok-captain');
    await userEvent.click(within(row('ok-captain')).getByRole('button', { name: 'Actions' }));
    const item = await screen.findByRole('menuitem', { name: 'View Usage' });
    expect(item.textContent).toBe('View Usage');
  });
});
