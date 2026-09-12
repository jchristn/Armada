import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CheckRuns from './CheckRuns';
import {
  getVesselReadiness,
  listCheckRuns,
  listVessels,
  listWorkflowProfiles,
  previewWorkflowProfileForVessel,
  runCheck,
} from '../api/client';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};

vi.mock('../api/client', () => ({
  listCheckRuns: vi.fn(),
  listVessels: vi.fn(),
  listWorkflowProfiles: vi.fn(),
  previewWorkflowProfileForVessel: vi.fn(),
  getVesselReadiness: vi.fn(),
  runCheck: vi.fn(),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
    formatDateTime: (value: string | null | undefined) => value ?? '',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({
    pushToast: vi.fn(),
  }),
}));

describe('CheckRuns', () => {
  beforeEach(() => {
    vi.mocked(listCheckRuns).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 9999,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'chk_123',
          tenantId: 'ten_123',
          userId: 'usr_123',
          workflowProfileId: 'wfp_123',
          vesselId: 'vsl_123',
          missionId: null,
          voyageId: null,
          deploymentId: null,
          label: 'Nightly Build',
          type: 'Build',
          source: 'Armada',
          status: 'Passed',
          providerName: null,
          externalId: null,
          externalUrl: null,
          environmentName: null,
          command: 'dotnet build',
          workingDirectory: 'C:/repo',
          branchName: 'main',
          commitHash: 'abc123',
          exitCode: 0,
          output: 'Build succeeded.',
          summary: 'Nightly build passed.',
          testSummary: null,
          coverageSummary: null,
          artifacts: [],
          durationMs: 1250,
          startedUtc: '2026-05-03T00:00:00Z',
          completedUtc: '2026-05-03T00:00:01Z',
          createdUtc: '2026-05-03T00:00:00Z',
          lastUpdateUtc: '2026-05-03T00:00:01Z',
        },
      ],
    });

    vi.mocked(listVessels).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 9999,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'vsl_123',
          name: 'Check Vessel',
        } as never,
      ],
    });

    vi.mocked(listWorkflowProfiles).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 9999,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'wfp_123',
          name: 'Default Workflow',
        } as never,
      ],
    });

    vi.mocked(previewWorkflowProfileForVessel).mockResolvedValue({
      resolvedProfile: {
        id: 'wfp_123',
        tenantId: 'ten_123',
        userId: 'usr_123',
        ownershipScope: 'TenantWide',
        name: 'Default Workflow',
        description: null,
        scope: 'Vessel',
        fleetId: null,
        vesselId: 'vsl_123',
        isDefault: true,
        active: true,
        languageHints: [],
        lintCommand: null,
        buildCommand: 'dotnet build',
        unitTestCommand: 'dotnet test',
        integrationTestCommand: null,
        e2eTestCommand: null,
        migrationCommand: null,
        securityScanCommand: null,
        performanceCommand: null,
        packageCommand: null,
        deploymentVerificationCommand: null,
        rollbackVerificationCommand: null,
        publishArtifactCommand: null,
        releaseVersioningCommand: null,
        changelogGenerationCommand: null,
        requiredSecrets: [],
        requiredInputs: [],
        expectedArtifacts: [],
        environments: [],
        createdUtc: '2026-05-03T00:00:00Z',
        lastUpdateUtc: '2026-05-03T00:00:00Z',
      },
      resolutionMode: 'Vessel',
      availableCheckTypes: ['Build', 'UnitTest'],
      commandPreviews: [
        {
          checkType: 'Build',
          environmentName: null,
          command: 'dotnet build',
        },
      ],
    });

    vi.mocked(getVesselReadiness).mockResolvedValue({
      vesselId: 'vsl_123',
      workflowProfileId: 'wfp_123',
      checkType: 'Build',
      environmentName: null,
      isReady: true,
      errorCount: 0,
      warningCount: 0,
      issues: [],
      setupChecklist: [],
      toolchainProbes: [],
      deploymentMetadata: null,
    } as never);

    vi.mocked(runCheck).mockResolvedValue({
      id: 'chk_456',
      vesselId: 'vsl_123',
      workflowProfileId: 'wfp_123',
      missionId: null,
      voyageId: null,
      label: 'Manual Build',
      type: 'Build',
      source: 'Armada',
      status: 'Passed',
      command: 'dotnet build',
      workingDirectory: 'C:/repo',
      artifacts: [],
      createdUtc: '2026-05-03T00:00:00Z',
      lastUpdateUtc: '2026-05-03T00:00:00Z',
    } as never);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders check runs, filters them, and opens the run modal', async () => {
    render(
      <MemoryRouter initialEntries={['/checks']}>
        <Routes>
          <Route path="/checks" element={<CheckRuns />} />
          <Route path="/checks/:id" element={<div>Check Detail Route</div>} />
          <Route path="/releases/new" element={<div>Create Release Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { name: 'Checks' })).toBeInTheDocument();
    expect(await screen.findByText('Nightly Build')).toBeInTheDocument();
    expect(screen.getAllByText('Check Vessel').length).toBeGreaterThan(0);

    fireEvent.change(screen.getByDisplayValue('All sources'), {
      target: { value: 'External' },
    });
    expect(await screen.findByText('No check runs match the current filters.')).toBeInTheDocument();

    fireEvent.change(screen.getByDisplayValue('External'), {
      target: { value: 'all' },
    });
    expect(await screen.findByText('Nightly Build')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Run Check/ }));
    expect(screen.getByRole('heading', { name: 'Run Check' })).toBeInTheDocument();
  });
});
