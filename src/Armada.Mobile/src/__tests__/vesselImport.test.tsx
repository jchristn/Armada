import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { VesselImportBatch, VesselImportItem } from '@dashboard/types/models';
import VesselImportRoute from '../app/(app)/(work)/vessels/import';
import { BuildProviders, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

const api = client as jest.Mocked<typeof client>;

function batch(over: Partial<VesselImportBatch> = {}): VesselImportBatch {
  return {
    id: 'vib_1', tenantId: null, userId: null, status: 'Discovered', harborId: null, fleetId: null, jobId: null, requestedPathCount: 1,
    candidateCount: 2, createdCount: 0, skippedCount: 0, failedCount: 0, createdUtc: '2026-10-07T10:00:00Z', lastUpdateUtc: '2026-10-07T10:00:00Z',
    completedUtc: null, categorizationStatus: 'None', ...over,
  };
}

function item(over: Partial<VesselImportItem> = {}): VesselImportItem {
  return {
    id: 'vii_1', tenantId: null, batchId: 'vib_1', path: '/code/api', proposedName: 'api', remoteUrl: 'git@x:api.git', defaultBranch: 'main',
    candidateStatus: 'New', existingVesselId: null, outcome: 'Pending', outcomeReason: null, outcomeMessage: null, vesselId: null,
    createdUtc: '', lastUpdateUtc: '', ...over,
  };
}

const CANDIDATES = [item(), item({ id: 'vii_2', path: '/code/old', proposedName: 'old', candidateStatus: 'AlreadyOnboarded', existingVesselId: 'vsl_9' })];

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/vessels/import': VesselImportRoute,
  '(work)/vessels/index': () => <Text>Vessels screen</Text>,
  '(work)/vessels/[id]': () => <Text>Vessel screen</Text>,
};

describe('vessel import wizard', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'Core' } as never]));
    api.listPipelines.mockResolvedValue(page([]));
  });
  afterEach(() => jest.useRealTimers());

  it('discovers pasted paths, reviews candidates with defaults, and imports', async () => {
    api.discoverVesselImport.mockResolvedValue({ batchId: 'vib_1', runsInBackground: false, batch: batch(), candidates: CANDIDATES, truncated: false, hints: [] });
    api.importVessels.mockResolvedValue({
      batchId: 'vib_1', jobId: null, runsInBackground: false,
      batch: batch({ status: 'Completed', createdCount: 1, skippedCount: 1 }),
      items: [item({ outcome: 'Created', vesselId: 'vsl_1' }), CANDIDATES[1]],
    });
    await renderRouter(ROUTES, { initialUrl: '/vessels/import' });
    expect(screen.getByTestId('import-discover')).toBeDisabled();
    await act(async () => { await fireEvent.changeText(screen.getByTestId('import-paste'), '/code\n/code\n"/other"'); });
    expect(screen.getByText(/2 paths/)).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('import-discover')); });
    expect(api.discoverVesselImport).toHaveBeenCalledWith({ Directories: ['/code', '/other'], RunInBackground: true });

    await waitFor(() => expect(screen.getByTestId('import-review')).toBeTruthy());
    expect(screen.getByTestId('import-candidate-api')).toBeChecked();
    expect(screen.getByTestId('import-candidate-old')).toBeDisabled();
    await act(async () => { await fireEvent.press(screen.getByTestId('import-default-landing')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-default-landing-option-PullRequest')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-run')); });
    expect(api.importVessels).toHaveBeenCalledWith({
      BatchId: 'vib_1', Paths: ['/code/api'], FleetId: null, Defaults: { DefaultPipelineId: null, LandingMode: 'PullRequest' },
    });
    await waitFor(() => expect(screen.getByTestId('import-results')).toBeTruthy());
    expect(screen.getByTestId('import-result-api')).toHaveTextContent(/Created/);
  });

  it('requires a captain when fleet recommendations are on', async () => {
    api.discoverVesselImport.mockResolvedValue({ batchId: 'vib_1', runsInBackground: false, batch: batch(), candidates: CANDIDATES, truncated: false, hints: [] });
    api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', state: 'Idle', tenantId: null } as never]));
    api.getFleetCategorizationDefaultPrompt.mockResolvedValue({ prompt: 'Group them', timeoutMinutes: 10 } as never);
    await renderRouter(ROUTES, { initialUrl: '/vessels/import' });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('import-paste'), '/code'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-discover')); });
    await waitFor(() => expect(screen.getByTestId('import-review')).toBeTruthy());
    await act(async () => { await fireEvent(screen.getByTestId('import-categorize'), 'valueChange', true); });
    await waitFor(() => expect(api.getFleetCategorizationDefaultPrompt).toHaveBeenCalled());
    await act(async () => { await fireEvent.press(screen.getByTestId('import-run')); });
    expect(api.importVessels).not.toHaveBeenCalled();
    expect(screen.getByText('Choose the captain that will recommend fleets.')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('import-categorize-captain')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-categorize-captain-option-cpt_1')); });
    api.importVessels.mockResolvedValue({ batchId: 'vib_1', jobId: 'job_1', runsInBackground: true, batch: batch({ status: 'Importing' }), items: [] });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-run')); });
    expect(api.importVessels).toHaveBeenCalledWith(expect.objectContaining({
      Categorization: { Enabled: true, CaptainId: 'cpt_1', Prompt: null, ApplyAutomatically: false },
    }));
  });

  it('polls a background discovery until candidates arrive', async () => {
    jest.useFakeTimers();
    api.discoverVesselImport.mockResolvedValue({ batchId: 'vib_1', runsInBackground: true, batch: batch({ status: 'Discovering' }), candidates: [], truncated: false, hints: [] });
    api.getVesselImportBatch
      .mockResolvedValueOnce({ batch: batch({ status: 'Discovering' }), items: [], hints: [], fleetRecommendations: [] } as never)
      .mockResolvedValue({ batch: batch(), items: CANDIDATES, hints: [], fleetRecommendations: [] } as never);
    await renderRouter(ROUTES, { initialUrl: '/vessels/import' });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('import-paste'), '/code'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('import-discover')); });
    expect(screen.getByTestId('import-discovering')).toBeTruthy();
    await act(async () => { await jest.advanceTimersByTimeAsync(2000); });
    expect(api.getVesselImportBatch).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('import-discovering')).toBeTruthy();
    await act(async () => { await jest.advanceTimersByTimeAsync(2000); });
    expect(api.getVesselImportBatch).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('import-review')).toBeTruthy();
  });

  it('opens a batch from the route and applies edited fleet recommendations', async () => {
    api.getVesselImportBatch.mockResolvedValue({
      batch: batch({ status: 'Completed', createdCount: 2, categorizationStatus: 'Completed' }),
      items: [item({ outcome: 'Created', vesselId: 'vsl_1' }), item({ id: 'vii_3', path: '/code/web', proposedName: 'web', outcome: 'Created', vesselId: 'vsl_2' })],
      hints: [],
      fleetRecommendations: [
        { id: 'rec_1', tenantId: null, batchId: 'vib_1', name: 'Backend', description: null, rationale: 'APIs', sortOrder: 0, appliedFleetId: null, vesselIds: ['vsl_1', 'vsl_2'], createdUtc: '', lastUpdateUtc: '' },
      ],
    } as never);
    api.applyFleetRecommendations.mockResolvedValue({ fleets: [], assignments: [{}, {}] } as never);
    await renderRouter(ROUTES, { initialUrl: '/vessels/import?batch=vib_1' });
    await waitFor(() => expect(screen.getByTestId('fleet-recommendations')).toBeTruthy());
    expect(api.getVesselImportBatch).toHaveBeenCalledWith('vib_1');
    await act(async () => { await fireEvent.changeText(screen.getByTestId('fleet-rec-name-0'), 'Services'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('fleet-recs-apply')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('fleet-recs-confirm-confirm')); });
    expect(api.applyFleetRecommendations).toHaveBeenCalledWith('vib_1', { Fleets: [{ Name: 'Services', Description: null, VesselIds: ['vsl_1', 'vsl_2'] }] });
  });

  it('lists the import history and opens a batch', async () => {
    api.enumerateVesselImportBatches.mockResolvedValue(page([batch({ id: 'vib_7', status: 'Discovered' })]));
    api.getVesselImportBatch.mockResolvedValue({ batch: batch({ id: 'vib_7' }), items: CANDIDATES, hints: [], fleetRecommendations: [] } as never);
    await renderRouter(ROUTES, { initialUrl: '/vessels/import?view=history' });
    await waitFor(() => expect(screen.getByTestId('import-batch-vib_7')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('import-batch-vib_7')); });
    await waitFor(() => expect(screen.getByTestId('import-review')).toBeTruthy());
    expect(api.getVesselImportBatch).toHaveBeenCalledWith('vib_7');
  });
});
