import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { EnumerationResult, Pipeline, Vessel, Voyage, VoyageCreateRequest } from '../types/models';

const {
  createVoyageMock,
  listPipelinesMock,
  listVesselsMock,
  navigateMock,
} = vi.hoisted(() => ({
  createVoyageMock: vi.fn(),
  listPipelinesMock: vi.fn(),
  listVesselsMock: vi.fn(),
  navigateMock: vi.fn(),
}));

vi.mock('../api/client', () => ({
  createVoyage: createVoyageMock,
  listPipelines: listPipelinesMock,
  listVessels: listVesselsMock,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

import Dispatch from './Dispatch';

function makeEnumerationResult<T>(objects: T[]): EnumerationResult<T> {
  return {
    success: true,
    pageNumber: 1,
    pageSize: objects.length || 1,
    totalPages: 1,
    totalRecords: objects.length,
    objects,
    totalMs: 1,
  };
}

function makeVessel(overrides: Partial<Vessel> = {}): Vessel {
  return {
    id: 'vessel-1',
    tenantId: null,
    fleetId: null,
    name: 'Test Vessel',
    repoUrl: null,
    localPath: null,
    workingDirectory: null,
    defaultBranch: 'main',
    projectContext: null,
    styleGuide: null,
    enableModelContext: false,
    modelContext: null,
    landingMode: null,
    branchCleanupPolicy: null,
    allowConcurrentMissions: false,
    defaultPipelineId: null,
    active: true,
    createdUtc: '2026-04-02T00:00:00Z',
    lastUpdateUtc: '2026-04-02T00:00:00Z',
    ...overrides,
  };
}

function makePipeline(stages: string[]): Pipeline {
  return {
    id: 'pipeline-1',
    tenantId: null,
    name: 'Review Pipeline',
    description: null,
    stages: stages.map((personaName, index) => ({
      id: `stage-${index + 1}`,
      pipelineId: 'pipeline-1',
      order: index + 1,
      personaName,
      isOptional: false,
      description: null,
    })),
    isBuiltIn: false,
    active: true,
    createdUtc: '2026-04-02T00:00:00Z',
    lastUpdateUtc: '2026-04-02T00:00:00Z',
  };
}

function makeVoyage(id = 'voyage-1'): Voyage {
  return {
    id,
    tenantId: null,
    title: 'Voyage',
    description: null,
    status: 'Queued',
    createdUtc: '2026-04-02T00:00:00Z',
    completedUtc: null,
    lastUpdateUtc: '2026-04-02T00:00:00Z',
    autoPush: null,
    autoCreatePullRequests: null,
    autoMergePullRequests: null,
    landingMode: null,
  };
}

function renderDispatch() {
  render(
    <MemoryRouter>
      <Dispatch />
    </MemoryRouter>,
  );
}

function getSelects() {
  const [vesselSelect, pipelineSelect] = screen.getAllByRole('combobox');
  return { vesselSelect, pipelineSelect };
}

function getPayload(): VoyageCreateRequest {
  expect(createVoyageMock).toHaveBeenCalledTimes(1);
  return createVoyageMock.mock.calls[0][0] as VoyageCreateRequest;
}

beforeEach(() => {
  listVesselsMock.mockResolvedValue(makeEnumerationResult([makeVessel()]));
  listPipelinesMock.mockResolvedValue(makeEnumerationResult([]));
  createVoyageMock.mockResolvedValue(makeVoyage());
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('Dispatch', () => {
  it('dispatches semicolon-delimited prompts as separate missions', async () => {
    renderDispatch();

    await screen.findByRole('option', { name: 'Test Vessel' });

    const { vesselSelect } = getSelects();
    fireEvent.change(vesselSelect, { target: { value: 'vessel-1' } });
    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'Add auth; Create dashboard; Write unit tests' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Dispatch' }));

    await screen.findByText('Dispatched voyage with 3 missions');

    const payload = getPayload();
    expect(payload.title).toBe('Multi-task voyage');
    expect(payload.vesselId).toBe('vessel-1');
    expect(payload.missions).toEqual([
      { vesselId: 'vessel-1', title: 'Add auth', description: 'Add auth', priority: 100 },
      { vesselId: 'vessel-1', title: 'Create dashboard', description: 'Create dashboard', priority: 100 },
      { vesselId: 'vessel-1', title: 'Write unit tests', description: 'Write unit tests', priority: 100 },
    ]);

    await waitFor(
      () => expect(navigateMock).toHaveBeenCalledWith('/voyages/voyage-1'),
      { timeout: 2000 },
    );
  });

  it('dispatches numbered-list prompts as separate missions at submit time', async () => {
    renderDispatch();

    await screen.findByRole('option', { name: 'Test Vessel' });

    const { vesselSelect } = getSelects();
    fireEvent.change(vesselSelect, { target: { value: 'vessel-1' } });
    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '1. Add auth\n2. Create dashboard\n3. Write unit tests' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Dispatch' }));

    await screen.findByText('Dispatched voyage with 3 missions');

    expect(getPayload().missions).toEqual([
      { vesselId: 'vessel-1', title: 'Add auth', description: 'Add auth', priority: 100 },
      { vesselId: 'vessel-1', title: 'Create dashboard', description: 'Create dashboard', priority: 100 },
      { vesselId: 'vessel-1', title: 'Write unit tests', description: 'Write unit tests', priority: 100 },
    ]);

    await waitFor(
      () => expect(navigateMock).toHaveBeenCalledWith('/voyages/voyage-1'),
      { timeout: 2000 },
    );
  });

  it('keeps multi-stage pipeline prompts as a single mission even when they look splitable', async () => {
    listPipelinesMock.mockResolvedValue(
      makeEnumerationResult([makePipeline(['Implementer', 'Reviewer'])]),
    );
    createVoyageMock.mockResolvedValue(makeVoyage('voyage-2'));

    renderDispatch();

    await screen.findByRole('option', { name: 'Test Vessel' });
    await screen.findByRole('option', { name: /Review Pipeline/ });

    const { vesselSelect, pipelineSelect } = getSelects();
    fireEvent.change(vesselSelect, { target: { value: 'vessel-1' } });
    fireEvent.change(pipelineSelect, { target: { value: 'Review Pipeline' } });

    const prompt = '1. Add auth\n2. Create dashboard';
    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: prompt },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Dispatch' }));

    await screen.findByText('Dispatched voyage with 2 pipeline stages');

    const payload = getPayload();
    expect(payload.pipeline).toBe('Review Pipeline');
    expect(payload.title).toBe(prompt);
    expect(payload.missions).toEqual([
      {
        vesselId: 'vessel-1',
        title: prompt,
        description: prompt,
        priority: 100,
      },
    ]);

    await waitFor(
      () => expect(navigateMock).toHaveBeenCalledWith('/voyages/voyage-2'),
      { timeout: 2000 },
    );
  });
});
