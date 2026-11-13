import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AskWorkCard from './AskWorkCard';
import { AskBlackBoxActionsContext, type AskBlackBoxActionsValue } from './AskBlackBoxActions';
import { translateTemplate } from '../../i18n/runtime';
import type { AskTrackedWork, AskWorkSnapshot } from '../../types/models';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
    locale: 'en',
    formatRelativeTime: () => 'recently',
  }),
}));
vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>();
  return { ...actual, getBlackBoxBrief: vi.fn().mockResolvedValue('# What went wrong') };
});

const work: AskTrackedWork = { id: 'atw_bb', threadId: 'ath_1', entityType: 'BlackBox', entityId: 'bbx_1', title: 'Add retry', status: 'LandingFailed', state: 'Failed' };
const snapshot: AskWorkSnapshot = {
  entityType: 'BlackBox', entityId: 'bbx_1', title: 'Add retry', status: 'LandingFailed', state: 'Failed',
  blackBox: {
    digest: {
      entryId: 'bbx_1', missionId: 'msn_1', phase: 'Landing', step: 'TargetIntegration', triage: 'UserConfig', triageConfidence: 'High',
      kind: 'LandingConflict', captureMode: 'Sweeper', resolution: 'Open', statusBefore: 'WorkProduced', statusAfter: 'LandingFailed',
      sourceFile: 'src/Armada.Core/Services/MissionLandingHandler.cs', method: 'IntegrateAsync', reason: 'merge conflict in src/Sync/Runner.cs',
      why: [{ ruleId: 'bbx-land-003', statement: 'Two missions edited the same file.', confidence: 'High' }],
      recoveryPaths: [
        { id: 'bbx-land-003.pull-request', rank: 2, title: 'Land as a pull request' },
        { id: 'bbx-land-003.rebase-retry', rank: 1, title: 'Rebase and retry', dataLossRisk: false },
      ],
      conflictedFileTotal: 12,
    },
    conflictingMissions: [{ missionId: 'msn_2', title: 'Rename Sync.Run', commitHash: '9b8e7d6c5a4f3e2d1c0b', overlappingFiles: ['src/Sync/Runner.cs'], overlappingFileTotal: 1 }],
    conflictedFiles: ['src/Sync/Runner.cs', 'src/Sync/RunnerTests.cs'],
    recommendations: [{ id: 'bbx-dep-001.depends-on', setting: 'dependsOnMissionId', currentValue: null, suggestedValue: 'msn_2', canApply: true }],
    followUps: [
      { name: 'fix', label: 'Fix it', toolName: 'run_blackbox_recovery', argumentsText: '{"entryId":"bbx_1","pathId":"bbx-land-003.rebase-retry"}' },
      { name: 'compare', label: 'Compare with similar failures', prompt: 'Compare black box entry bbx_1 with similar failures' },
    ],
  },
};

function renderCard(actions: AskBlackBoxActionsValue | null) {
  return render(
    <MemoryRouter>
      <AskBlackBoxActionsContext.Provider value={actions}>
        <AskWorkCard work={work} snapshot={snapshot} />
      </AskBlackBoxActionsContext.Provider>
    </MemoryRouter>,
  );
}

describe('AskWorkCard for a black box entry', () => {
  it('renders the chips, where and why, conflicting missions and files, linking to the entry', () => {
    renderCard(null);
    expect(screen.getByText('Black box')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Add retry' })).toHaveAttribute('href', '/blackbox/bbx_1');
    const chips = screen.getByRole('list', { name: 'Black box summary' });
    expect(within(chips).getByText('UserConfig (High)')).toBeInTheDocument();
    expect(within(chips).getByText('Sweeper')).toBeInTheDocument();
    expect(screen.getByText(/MissionLandingHandler\.cs/)).toBeInTheDocument();
    expect(screen.getByText('bbx-land-003')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Rename Sync.Run' })).toHaveAttribute('href', '/missions/msn_2');
    expect(screen.getByText('9b8e7d6c5a')).toBeInTheDocument();
    expect(screen.getByText('Showing 2 of 12 rows.')).toBeInTheDocument();
    const paths = within(screen.getByRole('region', { name: 'Recovery paths' })).getAllByRole('listitem');
    expect(paths[0]).toHaveTextContent('Rebase and retry');
    expect(screen.queryByRole('button', { name: 'Propose' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Fix it' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Markdown' })).toBeInTheDocument();
  });

  it('proposes recovery paths, recommendations and Fix it, and asks the captain for message follow-ups', () => {
    const actions = { propose: vi.fn().mockResolvedValue(undefined), ask: vi.fn(), canAsk: true, busy: false };
    renderCard(actions);
    const paths = screen.getByRole('region', { name: 'Recovery paths' });
    fireEvent.click(within(paths).getAllByRole('button', { name: 'Propose' })[0]);
    expect(actions.propose).toHaveBeenLastCalledWith('run_blackbox_recovery', { entryId: 'bbx_1', pathId: 'bbx-land-003.rebase-retry' });
    fireEvent.click(within(screen.getByRole('region', { name: 'Configuration recommendations' })).getByRole('button', { name: 'Propose' }));
    expect(actions.propose).toHaveBeenLastCalledWith('apply_blackbox_recommendation', { entryId: 'bbx_1', recommendationId: 'bbx-dep-001.depends-on' });
    const followUps = screen.getByRole('group', { name: 'Follow-ups' });
    fireEvent.click(within(followUps).getByRole('button', { name: 'Fix it' }));
    expect(actions.propose).toHaveBeenLastCalledWith('run_blackbox_recovery', { entryId: 'bbx_1', pathId: 'bbx-land-003.rebase-retry' });
    fireEvent.click(within(followUps).getByRole('button', { name: 'Compare with similar failures' }));
    expect(actions.ask).toHaveBeenCalledWith('Compare black box entry bbx_1 with similar failures');
  });

  it('hides message follow-ups when no captain can answer', () => {
    renderCard({ propose: vi.fn(), ask: vi.fn(), canAsk: false, busy: false });
    const followUps = screen.getByRole('group', { name: 'Follow-ups' });
    expect(within(followUps).getByRole('button', { name: 'Fix it' })).toBeInTheDocument();
    expect(within(followUps).queryByRole('button', { name: 'Compare with similar failures' })).not.toBeInTheDocument();
  });
});
