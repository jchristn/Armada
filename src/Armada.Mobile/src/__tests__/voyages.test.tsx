import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Mission, Voyage } from '@dashboard/types/models';
import { VoyageCreateScreen } from '../screens/operations/VoyageCreateScreen';
import { VoyageDetail, prefillQuery } from '../screens/operations/VoyageDetail';
import { VoyagesList } from '../screens/operations/VoyagesList';
import { effectiveDefaultLandingMode, globalLandingModeFrom } from '../screens/operations/voyage/landingDefault';
import { filterAndSortVoyages } from '../screens/operations/voyage/voyageListFilters';
import { page } from '../test/operationsClient';
import { mockRouter } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  listPlaybooks: jest.fn(async () => ({ success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] })),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;

function voyage(id: string, title: string, patch: Partial<Voyage> = {}): Voyage {
  return {
    id, tenantId: 'default', title, description: null, status: 'InProgress',
    createdUtc: '2026-10-04T00:00:00Z', completedUtc: null, lastUpdateUtc: '2026-10-04T00:00:00Z',
    autoPush: null, autoCreatePullRequests: null, autoMergePullRequests: null, landingMode: null, ...patch,
  };
}

function mission(id: string, status: string, patch: Partial<Mission> = {}): Mission {
  return { id, tenantId: 'default', voyageId: 'vyg_1', vesselId: 'vsl_1', captainId: null, title: `Mission ${id}`, description: null, status, priority: 100, branchName: null, ...patch } as unknown as Mission;
}

beforeEach(() => {
  jest.clearAllMocks();
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'gateway', landingMode: null }]) as never);
  api.listCaptains.mockResolvedValue(page([]) as never);
  api.listPipelines.mockResolvedValue(page([{ id: 'ppl_1', name: 'Reviewed', stages: [{ personaName: 'Worker' }, { personaName: 'Judge' }] }]) as never);
  api.getSettings.mockResolvedValue({ landingMode: 'PullRequest' });
  api.listUsers.mockResolvedValue(page([]) as never);
});

describe('voyage list helpers', () => {
  it('filters by title, id, and status and sorts newest first by default', () => {
    const rows = [
      voyage('vyg_1', 'Alpha', { createdUtc: '2026-10-01T00:00:00Z', status: 'Complete' }),
      voyage('vyg_2', 'Beta', { createdUtc: '2026-10-03T00:00:00Z' }),
      voyage('vyg_3', 'Gamma', { createdUtc: '2026-10-02T00:00:00Z' }),
    ];
    expect(filterAndSortVoyages(rows, { search: '', status: '', sort: 'createdUtc:desc' }).map((v) => v.id)).toEqual(['vyg_2', 'vyg_3', 'vyg_1']);
    expect(filterAndSortVoyages(rows, { search: 'GAM', status: '', sort: 'createdUtc:desc' }).map((v) => v.id)).toEqual(['vyg_3']);
    expect(filterAndSortVoyages(rows, { search: 'vyg_1', status: '', sort: 'title:asc' }).map((v) => v.id)).toEqual(['vyg_1']);
    expect(filterAndSortVoyages(rows, { search: '', status: 'InProgress', sort: 'title:desc' }).map((v) => v.id)).toEqual(['vyg_3', 'vyg_2']);
  });

  it('resolves the default landing mode: vessel, then server setting, then Merge and Push', () => {
    expect(effectiveDefaultLandingMode('LocalMerge', 'PullRequest')).toBe('LocalMerge');
    expect(effectiveDefaultLandingMode(null, 'PullRequest')).toBe('PullRequest');
    expect(effectiveDefaultLandingMode(null, null)).toBe('MergeAndPush');
    expect(globalLandingModeFrom({ landingMode: 'None' })).toBe('None');
    expect(globalLandingModeFrom({ landingMode: 3 })).toBeNull();
    expect(globalLandingModeFrom(null)).toBeNull();
  });

  it('builds prefill query strings', () => {
    expect(prefillQuery({ vesselId: 'vsl_1', branchName: null, label: 'a b' })).toBe('?vesselId=vsl_1&label=a%20b');
    expect(prefillQuery({})).toBe('');
  });
});

describe('VoyagesList', () => {
  beforeEach(() => {
    api.listVoyages.mockResolvedValue(page([voyage('vyg_1', 'Gateway hardening', { landingMode: 'MergeAndPush' }), voyage('vyg_2', 'Docs sweep', { status: 'Complete' })]) as never);
  });

  it('lists voyages with their landing mode and opens one', async () => {
    const onSelect = jest.fn();
    await renderScreen(<VoyagesList onSelect={onSelect} />);
    expect(await screen.findByText('Gateway hardening')).toBeTruthy();
    expect(screen.getByText(/Landing Mode: MergeAndPush \(local \+ push\)/)).toBeTruthy();
    expect(screen.getByText(/Landing Mode: Default \(vessel or global default\)/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('voyage-row-vyg_2'));
    expect(onSelect).toHaveBeenCalledWith('vyg_2');
    await fireEvent.press(screen.getByTestId('voyages-create'));
    expect(mockRouter.push).toHaveBeenCalledWith('/voyages/create');
  });

  it('filters by status in the filter sheet and by search', async () => {
    await renderScreen(<VoyagesList onSelect={jest.fn()} />);
    await screen.findByText('Docs sweep');
    await fireEvent.press(screen.getByTestId('voyages-filters'));
    await fireEvent.press(screen.getByTestId('voyages-filter-status'));
    await fireEvent.press(screen.getByTestId('voyages-filter-status-option-Complete'));
    expect(screen.queryByText('Gateway hardening')).toBeNull();
    expect(screen.getByText('Docs sweep')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('voyages-filters-clear'));
    await fireEvent.changeText(screen.getByTestId('voyages-search'), 'gate');
    expect(screen.getByText('Gateway hardening')).toBeTruthy();
    expect(screen.queryByText('Docs sweep')).toBeNull();
  });

  it('cancels from the row menu after confirming, and bulk-cancels a long-press selection', async () => {
    api.cancelVoyage.mockResolvedValue(undefined);
    await renderScreen(<VoyagesList onSelect={jest.fn()} />);
    await screen.findByText('Gateway hardening');
    await fireEvent.press(screen.getByTestId('voyage-row-menu-vyg_1'));
    await fireEvent.press(screen.getByTestId('voyage-row-actions-cancel'));
    expect(api.cancelVoyage).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('voyages-confirm-confirm'));
    expect(api.cancelVoyage).toHaveBeenCalledWith('vyg_1');

    await fireEvent(screen.getByTestId('voyage-row-vyg_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('voyage-row-vyg_2'));
    await fireEvent.press(screen.getByTestId('voyages-bulk-cancel'));
    await fireEvent.press(screen.getByTestId('voyages-confirm-confirm'));
    expect(api.cancelVoyage).toHaveBeenCalledWith('vyg_2');
    expect(api.cancelVoyage).toHaveBeenCalledTimes(3);
  });

  it('purges after confirming and shows the status JSON', async () => {
    api.purgeVoyage.mockResolvedValue(undefined);
    api.getVoyageStatus.mockResolvedValue({ voyageId: 'vyg_2', complete: 1 });
    await renderScreen(<VoyagesList onSelect={jest.fn()} />);
    await screen.findByText('Docs sweep');
    await fireEvent.press(screen.getByTestId('voyage-row-menu-vyg_2'));
    await fireEvent.press(screen.getByTestId('voyage-row-actions-purge'));
    await fireEvent.press(screen.getByTestId('voyages-confirm-confirm'));
    expect(api.purgeVoyage).toHaveBeenCalledWith('vyg_2');
    await fireEvent.press(screen.getByTestId('voyage-row-menu-vyg_2'));
    await fireEvent.press(screen.getByTestId('voyage-row-actions-status'));
    expect(await screen.findByText(/"complete": 1/)).toBeTruthy();
  });

  it('reloads on voyage and mission events', async () => {
    const { socket } = await renderScreen(<VoyagesList onSelect={jest.fn()} />);
    await screen.findByText('Docs sweep');
    expect(api.listVoyages).toHaveBeenCalledTimes(1);
    await act(async () => { socket.message({ type: 'captain.changed', data: {} }); });
    await act(async () => { socket.message({ type: 'voyage.changed', data: { id: 'vyg_1' } }); });
    await waitFor(() => expect(api.listVoyages).toHaveBeenCalledTimes(2));
  });
});

describe('VoyageCreateScreen', () => {
  async function fill() {
    await renderScreen(<VoyageCreateScreen />);
    await waitFor(() => expect(api.getSettings).toHaveBeenCalled());
    await fireEvent.changeText(screen.getByTestId('voyage-title'), 'Docs sweep');
    await fireEvent.press(screen.getByTestId('voyage-vessel'));
    await fireEvent.press(await screen.findByTestId('voyage-vessel-option-vsl_1'));
    await fireEvent.changeText(screen.getByTestId('voyage-mission-title-0'), 'Update README');
  }

  it('validates like the dashboard', async () => {
    await renderScreen(<VoyageCreateScreen />);
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    expect(await screen.findByText('Voyage title is required.')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('voyage-title'), 'Docs sweep');
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    expect(await screen.findByText('Please select a vessel.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('voyage-vessel'));
    await fireEvent.press(await screen.findByTestId('voyage-vessel-option-vsl_1'));
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    expect(await screen.findByText('At least one mission with a title is required.')).toBeTruthy();
    expect(api.createVoyage).not.toHaveBeenCalled();
  });

  it('defaults to inheriting the landing mode, names the server default, and sends no landingMode', async () => {
    api.createVoyage.mockResolvedValue({ id: 'vyg_9' } as Voyage);
    await fill();
    expect(screen.getByLabelText(/Landing Mode, Default \(use vessel or global setting\)/)).toBeTruthy();
    expect(await screen.findByText(/Currently: Pull Request -- push and open a PR/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    expect(api.createVoyage).toHaveBeenCalledTimes(1);
    const payload = api.createVoyage.mock.calls[0][0];
    expect(payload).not.toHaveProperty('landingMode');
    expect(payload).toEqual({
      title: 'Docs sweep',
      description: undefined,
      vesselId: 'vsl_1',
      missions: [{ title: 'Update README', description: 'Update README', vesselId: 'vsl_1', priority: 100 }],
    });
    expect(mockRouter.replace).toHaveBeenCalledWith('/voyages/vyg_9');
  });

  it.each(['LocalMerge', 'PullRequest', 'MergeQueue', 'None', 'MergeAndPush'])('sends landingMode %s when chosen', async (mode) => {
    api.createVoyage.mockResolvedValue({ id: 'vyg_9' } as Voyage);
    await fill();
    await fireEvent.press(screen.getByTestId('voyage-landing-mode'));
    await fireEvent.press(await screen.findByTestId(`voyage-landing-mode-option-${mode}`));
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    expect(api.createVoyage.mock.calls[0][0].landingMode).toBe(mode);
  });

  it('adds and removes missions, picks a pipeline, and reports server errors', async () => {
    api.createVoyage.mockRejectedValue(new Error('Vessel is archived'));
    await fill();
    await fireEvent.press(screen.getByTestId('voyage-add-mission'));
    await fireEvent.changeText(screen.getByTestId('voyage-mission-title-1'), 'Second');
    await fireEvent.changeText(screen.getByTestId('voyage-mission-description-1'), 'Do the second thing');
    await fireEvent.changeText(screen.getByTestId('voyage-mission-priority-1'), '7');
    await fireEvent.press(screen.getByTestId('voyage-add-mission'));
    await fireEvent.press(screen.getByTestId('voyage-mission-remove-2'));
    await fireEvent.press(screen.getByTestId('voyage-pipeline'));
    await fireEvent.press(await screen.findByTestId('voyage-pipeline-option-Reviewed'));
    await fireEvent.press(screen.getByTestId('voyage-create-submit'));
    const payload = api.createVoyage.mock.calls[0][0];
    expect(payload.pipeline).toBe('Reviewed');
    expect(payload.missions).toEqual([
      { title: 'Update README', description: 'Update README', vesselId: 'vsl_1', priority: 100 },
      { title: 'Second', description: 'Do the second thing', vesselId: 'vsl_1', priority: 7 },
    ]);
    expect(await screen.findByText('Vessel is archived')).toBeTruthy();
    expect(mockRouter.replace).not.toHaveBeenCalled();
  });
});

describe('VoyageDetail', () => {
  it('shows the voyage, progress, landing mode, and missions, and links to them', async () => {
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'Gateway hardening', { landingMode: 'PullRequest', captainOverridesJson: '[{"persona":"Judge","fallbackTier":"Premium"}]' }), missions: [mission('msn_1', 'Complete'), mission('msn_2', 'Failed')] } as never);
    await renderScreen(<VoyageDetail id="vyg_1" />);
    expect(await screen.findByTestId('voyage-detail-title')).toHaveTextContent('Gateway hardening');
    expect(screen.getByTestId('voyage-progress-text')).toHaveTextContent('1/2 complete, 1 failed');
    expect(screen.getByText('PullRequest (opens a PR)')).toBeTruthy();
    expect(screen.getByText('Judge')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('voyage-mission-row-msn_1'));
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1');
    expect(screen.getByTestId('voyage-cancel')).toBeTruthy();
    expect(screen.queryByTestId('voyage-delete')).toBeNull();
  });

  it('loads missions separately when getVoyage returns the bare voyage', async () => {
    api.getVoyage.mockResolvedValue(voyage('vyg_1', 'Bare', { status: 'Complete' }) as never);
    api.listMissions.mockResolvedValue(page([mission('msn_5', 'Complete')]) as never);
    await renderScreen(<VoyageDetail id="vyg_1" />);
    expect(await screen.findByText('Mission msn_5')).toBeTruthy();
    expect(api.listMissions).toHaveBeenCalledWith({ pageSize: 1000, filters: { voyageId: 'vyg_1' } });
    expect(screen.getByTestId('voyage-delete')).toBeTruthy();
  });

  it('cancels, retries failed missions, and deletes after confirming', async () => {
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'G'), missions: [mission('msn_2', 'Failed', { title: 'Broken', priority: 5 })] } as never);
    api.cancelVoyage.mockResolvedValue(undefined);
    api.createMission.mockResolvedValue({} as Mission);
    const { socket } = await renderScreen(<VoyageDetail id="vyg_1" />);
    await screen.findByTestId('voyage-detail-title');
    await fireEvent.press(screen.getByTestId('voyage-cancel'));
    await fireEvent.press(screen.getByTestId('voyage-confirm-confirm'));
    expect(api.cancelVoyage).toHaveBeenCalledWith('vyg_1');
    await fireEvent.press(screen.getByTestId('voyage-retry-failed'));
    await fireEvent.press(screen.getByTestId('voyage-confirm-confirm'));
    expect(api.createMission).toHaveBeenCalledWith({ title: 'Broken', description: undefined, vesselId: 'vsl_1', voyageId: 'vyg_1', priority: 5 });

    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'G', { status: 'Cancelled' }), missions: [] } as never);
    api.purgeVoyage.mockResolvedValue(undefined);
    await act(async () => { socket.message({ type: 'voyage.changed', data: { id: 'vyg_1', status: 'Cancelled' } }); });
    await waitFor(() => expect(screen.getByTestId('voyage-delete')).toBeTruthy());
    expect(screen.queryByTestId('voyage-cancel')).toBeNull();
    await fireEvent.press(screen.getByTestId('voyage-delete'));
    await fireEvent.press(screen.getByTestId('voyage-confirm-confirm'));
    expect(api.purgeVoyage).toHaveBeenCalledWith('vyg_1');
    expect(mockRouter.back).toHaveBeenCalled();
  });

  it('shows a deleted notice in the split pane instead of navigating', async () => {
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'Old', { status: 'Complete' }), missions: [] } as never);
    api.purgeVoyage.mockResolvedValue(undefined);
    await renderScreen(<VoyageDetail id="vyg_1" embedded />);
    await fireEvent.press(await screen.findByTestId('voyage-delete'));
    await fireEvent.press(screen.getByTestId('voyage-confirm-confirm'));
    expect(screen.getByText('Voyage "Old" deleted.')).toBeTruthy();
    expect(mockRouter.back).not.toHaveBeenCalled();
  });

  it('follows mission and voyage events live', async () => {
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'Live'), missions: [mission('msn_1', 'InProgress')] } as never);
    const { socket } = await renderScreen(<VoyageDetail id="vyg_1" />);
    await screen.findByTestId('voyage-detail-title');
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'Live'), missions: [mission('msn_1', 'Complete')] } as never);
    await act(async () => { socket.message({ type: 'mission.changed', data: { id: 'msn_1', status: 'Complete' } }); });
    await waitFor(() => expect(screen.getByTestId('voyage-progress-text')).toHaveTextContent('1/1 complete, 0 failed'));
    expect(api.getVoyage).toHaveBeenCalledTimes(2);
  });

  it('opens a mission diff from the row menu', async () => {
    api.getVoyage.mockResolvedValue({ voyage: voyage('vyg_1', 'G'), missions: [mission('msn_1', 'Complete')] } as never);
    api.getMissionDiff.mockResolvedValue({ diff: 'No changes' } as never);
    await renderScreen(<VoyageDetail id="vyg_1" />);
    await fireEvent.press(await screen.findByTestId('voyage-mission-menu-msn_1'));
    await fireEvent.press(screen.getByTestId('voyage-mission-actions-diff'));
    expect(api.getMissionDiff).toHaveBeenCalledWith('msn_1');
    expect(await screen.findByText('No changes')).toBeTruthy();
  });
});
