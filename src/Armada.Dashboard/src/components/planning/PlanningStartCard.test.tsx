import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlanningStartCard from './PlanningStartCard';

vi.mock('../shared/PlaybookSelector', () => ({
  default: ({ disabled }: { disabled?: boolean }) => (
    <div data-testid="playbook-selector" data-disabled={disabled ? 'true' : 'false'}>
      Playbook selector
    </div>
  ),
}));

const t = (value: string) => value;

describe('PlanningStartCard', () => {
  it('shows explicit provisioning feedback while the session is being created', () => {
    render(
      <MemoryRouter>
        <PlanningStartCard
          t={t}
          loading={false}
          captains={[
            {
              id: 'cpt_123',
              tenantId: null,
              name: 'Planner',
              runtime: 'Codex',
              supportsPlanningSessions: true,
              planningSessionSupportReason: null,
              systemInstructions: null,
              model: null,
              allowedPersonas: null,
              preferredPersona: null,
              state: 'Idle',
              processId: null,
              lastHeartbeatUtc: '2026-04-29T00:00:00Z',
              recoveryAttempts: 0,
              createdUtc: '2026-04-29T00:00:00Z',
              lastUpdateUtc: '2026-04-29T00:00:00Z',
            },
          ]}
          fleets={[
            {
              id: 'flt_123',
              name: 'Fleet',
              tenantId: null,
              description: null,
              defaultPipelineId: null,
              active: true,
              createdUtc: '2026-04-29T00:00:00Z',
              lastUpdateUtc: '2026-04-29T00:00:00Z',
            },
          ]}
          vessels={[
            {
              id: 'ves_123',
              tenantId: null,
              fleetId: 'flt_123',
              name: 'Vessel',
              repoUrl: 'https://example.com/repo.git',
              localPath: null,
              workingDirectory: null,
              defaultBranch: 'main',
              projectContext: null,
              styleGuide: null,
              enableModelContext: false,
              modelContext: null,
              landingMode: null,
              branchCleanupPolicy: null,
              requirePassingChecksToLand: false,
              defaultPipelineId: null,
              protectedBranchPatterns: [],
              releaseBranchPrefix: 'release/',
              hotfixBranchPrefix: 'hotfix/',
              requirePullRequestForProtectedBranches: false,
              requireMergeQueueForReleaseBranches: false,
              hasGitHubTokenOverride: false,
              active: true,
              createdUtc: '2026-04-29T00:00:00Z',
              lastUpdateUtc: '2026-04-29T00:00:00Z',
            },
          ]}
          pipelines={[]}
          title="Plan"
          captainId="cpt_123"
          fleetId="flt_123"
          vesselId="ves_123"
          pipelineId=""
          availableVessels={[
            {
              id: 'ves_123',
              tenantId: null,
              fleetId: 'flt_123',
              name: 'Vessel',
              repoUrl: 'https://example.com/repo.git',
              localPath: null,
              workingDirectory: null,
              defaultBranch: 'main',
              projectContext: null,
              styleGuide: null,
              enableModelContext: false,
              modelContext: null,
              landingMode: null,
              branchCleanupPolicy: null,
              requirePassingChecksToLand: false,
              defaultPipelineId: null,
              protectedBranchPatterns: [],
              releaseBranchPrefix: 'release/',
              hotfixBranchPrefix: 'hotfix/',
              requirePullRequestForProtectedBranches: false,
              requireMergeQueueForReleaseBranches: false,
              hasGitHubTokenOverride: false,
              active: true,
              createdUtc: '2026-04-29T00:00:00Z',
              lastUpdateUtc: '2026-04-29T00:00:00Z',
            },
          ]}
          selectedCaptain={{
            id: 'cpt_123',
            tenantId: null,
            name: 'Planner',
            runtime: 'Codex',
            supportsPlanningSessions: true,
            planningSessionSupportReason: null,
            systemInstructions: null,
            model: null,
            allowedPersonas: null,
            preferredPersona: null,
            state: 'Idle',
            processId: null,
            lastHeartbeatUtc: '2026-04-29T00:00:00Z',
            recoveryAttempts: 0,
            createdUtc: '2026-04-29T00:00:00Z',
            lastUpdateUtc: '2026-04-29T00:00:00Z',
          }}
          selectedPlaybooks={[]}
          creating
          canStartSession={false}
          onTitleChange={() => undefined}
          onCaptainChange={() => undefined}
          onFleetChange={() => undefined}
          onVesselChange={() => undefined}
          onPipelineChange={() => undefined}
          onSelectedPlaybooksChange={() => undefined}
          onStart={() => undefined}
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole('status')).toHaveTextContent('Starting planning session...');
    expect(screen.getByText('Armada is reserving the captain, provisioning the dock, and preparing the worktree. First-time repository setup can take a few minutes.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Starting Planning Session...' })).toBeDisabled();
    expect(screen.getByPlaceholderText('Optional planning session title')).toBeDisabled();
    expect(screen.getByRole('link', { name: 'Manage pipelines' })).toHaveAttribute('href', '/pipelines');
    expect(screen.getByTestId('playbook-selector')).toHaveAttribute('data-disabled', 'true');
  });
});
