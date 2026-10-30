import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import BlackBoxEntriesTab from './BlackBoxEntriesTab';
import {
  deleteBlackBox,
  enumerateBlackBox,
  getBlackBox,
  getBlackBoxBrief,
  pinBlackBox,
  reanalyzeBlackBox,
  resolveBlackBox,
  unpinBlackBox,
} from '../../api/client';
import { browserPlatform } from '../../platform/browserPlatform';
import { translateTemplate } from '../../i18n/runtime';
import type { BlackBoxEntrySummary } from '../../types/blackbox';
import type { WebSocketMessage } from '../../types/models';
import { blackBoxAttemptsFixture, blackBoxEntryFixture, page } from '../../test/fixtures/blackbox';

vi.mock('../../api/client', () => ({
  deleteBlackBox: vi.fn(),
  enumerateBlackBox: vi.fn(),
  getBlackBox: vi.fn(),
  getBlackBoxBrief: vi.fn(),
  pinBlackBox: vi.fn(),
  reanalyzeBlackBox: vi.fn(),
  resolveBlackBox: vi.fn(),
  searchBlackBox: vi.fn(),
  unpinBlackBox: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
const auth = { isAdmin: true, isTenantAdmin: true };
const pushToast = vi.fn();
const socket = { connected: true, reconnectCount: 0, send: vi.fn(), subscribe: (_fn: (m: WebSocketMessage) => void) => () => {} };
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));
vi.mock('../../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../../context/WebSocketContext', () => ({ useWebSocket: () => socket }));

const ALL_ACTIONS = ['View', 'Download Markdown', 'View JSON', 'Ask Armada', 'Resolve', 'Pin', 'Reanalyze', 'Delete'];

function WhereAmI() {
  const location = useLocation();
  return <div data-testid="where">{location.pathname}{location.search}|{JSON.stringify(location.state)}</div>;
}

function renderTab() {
  return render(
    <MemoryRouter initialEntries={['/missions?tab=blackbox']}>
      <Routes>
        <Route path="/missions" element={<BlackBoxEntriesTab vessels={[]} />} />
        <Route path="/blackbox/:id" element={<WhereAmI />} />
        <Route path="/ask" element={<WhereAmI />} />
      </Routes>
    </MemoryRouter>,
  );
}

function rows(): BlackBoxEntrySummary[] {
  const [first, second] = blackBoxAttemptsFixture();
  return [first, { ...second, pinned: true }, { ...second, id: 'bbx_v', subjectType: 'Voyage', missionId: null, voyageId: 'vyg_9', missionTitle: null, pinned: false }];
}

/** Open the row menu of an entry and return its items. */
async function openMenu(id: string): Promise<HTMLElement[]> {
  const link = await screen.findByRole('link', { name: id });
  const row = link.closest('tr') as HTMLElement;
  fireEvent.click(within(row).getByRole('button', { name: 'Actions' }));
  const menu = await screen.findByRole('menu');
  return within(menu).getAllByRole('menuitem');
}

async function choose(id: string, label: string) {
  const items = await openMenu(id);
  const item = items.find((i) => i.textContent === label);
  if (!item) throw new Error(`no ${label} item`);
  fireEvent.click(item);
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  auth.isAdmin = true;
  auth.isTenantAdmin = true;
  vi.mocked(enumerateBlackBox).mockResolvedValue(page(rows()) as never);
  vi.mocked(getBlackBox).mockResolvedValue(blackBoxEntryFixture({ id: 'bbx_1', resolutionNote: 'old note' }));
});

describe('Black box entries row menu', () => {
  it('is the last column and lists every action in order for a tenant admin', async () => {
    renderTab();
    await screen.findByRole('link', { name: 'bbx_1' });
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
    expect(headers[headers.length - 1]).toContain('Actions');
    const items = await openMenu('bbx_1');
    expect(items.map((i) => i.textContent)).toEqual(ALL_ACTIONS);
    expect(items.every((i) => !(i as HTMLButtonElement).disabled)).toBe(true);
    expect(items[7]).toHaveClass('danger');
    expect(items[0]).toHaveFocus();
  });

  it('labels Pin as Unpin on a pinned entry', async () => {
    renderTab();
    const items = await openMenu('bbx_2');
    expect(items.map((i) => i.textContent)).toContain('Unpin');
    expect(items.map((i) => i.textContent)).not.toContain('Pin');
  });

  it('hides Reanalyze and disables Delete with a reason for other users', async () => {
    auth.isAdmin = false;
    auth.isTenantAdmin = false;
    renderTab();
    const items = await openMenu('bbx_1');
    expect(items.map((i) => i.textContent)).toEqual(['View', 'Download Markdown', 'View JSON', 'Ask Armada', 'Resolve', 'Pin', 'Delete']);
    const del = items[6] as HTMLButtonElement;
    expect(del).toBeDisabled();
    expect(del.title).toMatch(/Tenant admins only/);
    fireEvent.click(del);
    expect(screen.queryByText(/Delete black box entry bbx_1\?/)).not.toBeInTheDocument();
    expect(deleteBlackBox).not.toHaveBeenCalled();
  });

  it('View opens the entry page', async () => {
    renderTab();
    await choose('bbx_1', 'View');
    expect(await screen.findByTestId('where')).toHaveTextContent('/blackbox/bbx_1');
  });

  it('Download Markdown saves the brief as blackbox-{id}.md', async () => {
    const save = vi.spyOn(browserPlatform, 'saveFile').mockResolvedValue(undefined);
    vi.mocked(getBlackBoxBrief).mockResolvedValue('# FIX THIS');
    renderTab();
    await choose('bbx_1', 'Download Markdown');
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(getBlackBoxBrief).toHaveBeenCalledWith('bbx_1');
    expect(save.mock.calls[0][0]).toMatchObject({ filename: 'blackbox-bbx_1.md', contentType: 'text/markdown' });
    save.mockRestore();
  });

  it('Download Markdown shows an error toast when the brief cannot be read', async () => {
    const save = vi.spyOn(browserPlatform, 'saveFile').mockResolvedValue(undefined);
    vi.mocked(getBlackBoxBrief).mockRejectedValue(new Error('boom'));
    renderTab();
    await choose('bbx_1', 'Download Markdown');
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', 'The download failed: boom'));
    expect(save).not.toHaveBeenCalled();
    save.mockRestore();
  });

  it('View JSON loads the entry and opens the JSON viewer', async () => {
    renderTab();
    await choose('bbx_1', 'View JSON');
    expect(await screen.findByText('Black box entry')).toBeInTheDocument();
    expect(getBlackBox).toHaveBeenCalledWith('bbx_1');
  });

  it('View JSON shows an error toast when the entry cannot be loaded', async () => {
    vi.mocked(getBlackBox).mockRejectedValue(new Error('gone'));
    renderTab();
    await choose('bbx_1', 'View JSON');
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', 'Failed: gone'));
    expect(screen.queryByText('Black box entry')).not.toBeInTheDocument();
  });

  it('Ask Armada opens Ask with the mission prompt in the URL and router state', async () => {
    renderTab();
    await choose('bbx_1', 'Ask Armada');
    const prompt = 'Show me the pertinent details from the black box for mission msn_1 and give me your opinion on why it failed and what could have been done differently';
    const where = await screen.findByTestId('where');
    expect(where.textContent).toContain(`/ask?prompt=${encodeURIComponent(prompt)}`);
    expect(where.textContent).toContain(JSON.stringify({ draft: prompt, prompt }));
  });

  it('Ask Armada uses the voyage wording for a voyage entry', async () => {
    renderTab();
    await choose('bbx_v', 'Ask Armada');
    expect(await screen.findByTestId('where')).toHaveTextContent('black box for voyage vyg_9 and give me your opinion');
  });

  it('Resolve opens the resolve dialog on the full entry and refreshes the table after saving', async () => {
    vi.mocked(resolveBlackBox).mockResolvedValue(blackBoxEntryFixture({ id: 'bbx_1', resolution: 'Resolved' }));
    renderTab();
    await screen.findByRole('link', { name: 'bbx_1' });
    expect(enumerateBlackBox).toHaveBeenCalledTimes(1);
    await choose('bbx_1', 'Resolve');
    expect(await screen.findByText('Resolve black box entry')).toBeInTheDocument();
    expect(getBlackBox).toHaveBeenCalledWith('bbx_1', false);
    expect(screen.getByLabelText('Note')).toHaveValue('old note');
    fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'fixed in mrg_2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save resolution' }));
    await waitFor(() => expect(resolveBlackBox).toHaveBeenCalledWith('bbx_1', { resolution: 'Resolved', note: 'fixed in mrg_2', link: null }));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', 'Resolution saved.'));
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Resolve black box entry')).not.toBeInTheDocument();
  });

  it('Resolve keeps the dialog open with the error when saving fails', async () => {
    vi.mocked(resolveBlackBox).mockRejectedValue(new Error('denied'));
    renderTab();
    await choose('bbx_1', 'Resolve');
    fireEvent.click(await screen.findByRole('button', { name: 'Save resolution' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The resolution could not be saved: denied');
    expect(enumerateBlackBox).toHaveBeenCalledTimes(1);
  });

  it('Pin and Unpin call the right route, toast and refresh', async () => {
    vi.mocked(pinBlackBox).mockResolvedValue(blackBoxEntryFixture({ id: 'bbx_1', pinned: true }));
    vi.mocked(unpinBlackBox).mockResolvedValue(blackBoxEntryFixture({ id: 'bbx_2', pinned: false }));
    renderTab();
    await choose('bbx_1', 'Pin');
    await waitFor(() => expect(pinBlackBox).toHaveBeenCalledWith('bbx_1'));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', 'Entry pinned: garbage collection keeps it.'));
    await choose('bbx_2', 'Unpin');
    await waitFor(() => expect(unpinBlackBox).toHaveBeenCalledWith('bbx_2'));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', 'Entry unpinned: it expires with the retention policy.'));
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenCalledTimes(3));
  });

  it('a failed pin shows an error toast and does not refresh', async () => {
    vi.mocked(pinBlackBox).mockRejectedValue(new Error('nope'));
    renderTab();
    await choose('bbx_1', 'Pin');
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', 'Failed: nope'));
    expect(enumerateBlackBox).toHaveBeenCalledTimes(1);
  });

  it('Reanalyze starts a reanalysis, toasts and refreshes; a failure toasts the error', async () => {
    vi.mocked(reanalyzeBlackBox).mockResolvedValueOnce(blackBoxEntryFixture({ id: 'bbx_1', state: 'Captured' }));
    renderTab();
    await choose('bbx_1', 'Reanalyze');
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', 'Reanalysis started. The table updates when it finishes.'));
    expect(reanalyzeBlackBox).toHaveBeenCalledWith('bbx_1');
    await waitFor(() => expect(enumerateBlackBox).toHaveBeenCalledTimes(2));
    vi.mocked(reanalyzeBlackBox).mockRejectedValueOnce(new Error('busy'));
    await choose('bbx_1', 'Reanalyze');
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', 'Failed: busy'));
    expect(enumerateBlackBox).toHaveBeenCalledTimes(2);
  });

  it('Delete asks first; Cancel deletes nothing', async () => {
    renderTab();
    await choose('bbx_1', 'Delete');
    expect(await screen.findByText(/Delete black box entry bbx_1\?/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByText(/Delete black box entry bbx_1\?/)).not.toBeInTheDocument());
    expect(deleteBlackBox).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'bbx_1' })).toBeInTheDocument();
  });

  it('Delete deletes after confirming, toasts and refreshes the table', async () => {
    vi.mocked(deleteBlackBox).mockResolvedValue({ requested: 1, deleted: 1, pending: 0, bytesFreed: 4096 });
    renderTab();
    await choose('bbx_1', 'Delete');
    await screen.findByText(/Delete black box entry bbx_1\?/);
    vi.mocked(enumerateBlackBox).mockResolvedValue(page(rows().slice(1)) as never);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteBlackBox).toHaveBeenCalledWith('bbx_1'));
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('warning', 'Black box entry bbx_1 deleted.'));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'bbx_1' })).not.toBeInTheDocument());
  });

  it('a failed delete shows the error in the progress modal and leaves the row', async () => {
    vi.mocked(deleteBlackBox).mockRejectedValue(new Error('store unavailable'));
    renderTab();
    await choose('bbx_1', 'Delete');
    await screen.findByText(/Delete black box entry bbx_1\?/);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('store unavailable')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    await waitFor(() => expect(screen.queryByText('store unavailable')).not.toBeInTheDocument());
    expect(pushToast).not.toHaveBeenCalledWith('warning', expect.anything());
    expect(screen.getByRole('link', { name: 'bbx_1' })).toBeInTheDocument();
    expect(enumerateBlackBox).toHaveBeenCalledTimes(1);
  });

  it('opening the menu does not open the entry (row click)', async () => {
    renderTab();
    await openMenu('bbx_1');
    expect(screen.queryByTestId('where')).not.toBeInTheDocument();
  });
});
