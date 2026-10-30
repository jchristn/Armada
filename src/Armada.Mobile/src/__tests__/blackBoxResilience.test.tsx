import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as Clipboard from 'expo-clipboard';
import type { ReactElement } from 'react';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import { useNotifications } from '../notifications/NotificationContext';
import { BlackBoxPanel } from '../screens/blackbox/BlackBoxPanel';
import { armadaFixPrompt, changeBrief, isArmadaDefect, rankedChanges, resilienceOf, severityTone, topChange, type Resilience } from '../screens/blackbox/resilienceModel';
import { BRIEF, RESILIENCE_IN_SCOPE, RESILIENCE_OUT_OF_SCOPE, blackBoxEntry, withResilience } from '../test/blackBoxFixtures';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;
const clipboard = Clipboard as jest.Mocked<typeof Clipboard>;

function ToastProbe() {
  const { toasts } = useNotifications();
  return <>{toasts.map((toast) => <Text key={toast.id} testID={`toast-${toast.severity}`}>{toast.message}</Text>)}</>;
}

async function renderPanel(ui: ReactElement = <BlackBoxPanel source={{ kind: 'mission', id: 'msn_1' }} />) {
  const result = await renderScreen(<>{ui}<ToastProbe /></>);
  await act(async () => { await Promise.resolve(); });
  return result;
}

async function press(testID: string) {
  await act(async () => { await fireEvent.press(screen.getByTestId(testID)); });
}

const IN_SCOPE = withResilience(blackBoxEntry(), RESILIENCE_IN_SCOPE, { verdict: 'ArmadaDefect' });

beforeEach(() => {
  jest.clearAllMocks();
  setMockParams({});
  api.listVessels.mockResolvedValue(page([]));
  api.getMissionBlackBox.mockResolvedValue(IN_SCOPE);
  api.getBlackBoxBrief.mockResolvedValue(BRIEF);
});

describe('resilience model', () => {
  it('reads the section, ranks the changes and builds the Draft prompt', () => {
    const res = resilienceOf(IN_SCOPE) as Resilience;
    expect(res.inScope).toBe(true);
    expect(rankedChanges(res).map((c) => c.rank)).toEqual([1, 2]);
    expect(topChange(res)?.title).toBe('Recreate a missing dock worktree from the branch before landing');
    expect(changeBrief(rankedChanges(res)[1])).toContain('Acceptance test: Services.DockLease: reclaim refused.');
    expect(changeBrief(topChange(res)!)).toContain('Recreate the worktree before git runs.');
    expect(armadaFixPrompt(IN_SCOPE, res)).toBe('Draft an Armada fix mission for black box entry bbx_1 (mission msn_1): start from the entry\'s Resilience section (top proposed change: "Recreate a missing dock worktree from the branch before landing"), with its acceptance test, regression test and code pointers at the Armada build commit, and propose it on the Armada vessel for my approval.');
    expect(armadaFixPrompt(IN_SCOPE, null)).not.toContain('top proposed change');
    expect(resilienceOf(blackBoxEntry({ record: null }))).toBeNull();
    expect(isArmadaDefect(IN_SCOPE)).toBe(true);
    expect(isArmadaDefect(blackBoxEntry())).toBe(false);
    expect(severityTone('Critical')).toBe('failed');
    expect(severityTone('Medium')).toBe('warning');
  });
});

describe('resilience section on the black box panel', () => {
  it('shows the Armada resilience section right after the summary, with the suspected badge', async () => {
    await renderPanel();
    expect(screen.getByTestId('bbx-suspected-defect')).toHaveTextContent('Suspected Armada defect');
    const section = within(screen.getByTestId('bbx-resilience'));
    expect(section.getByText('Self-healable: Yes')).toBeTruthy();
    expect(section.getByText('Critical severity')).toBeTruthy();
    expect(screen.getByTestId('bbx-resilience-self-healing')).toHaveTextContent('Armada could have recreated the worktree from armada/msn_1 and retried the landing.');
    expect(screen.getByTestId('bbx-resilience-gap-value')).toHaveTextContent('The dock was reclaimed while the mission still had to land.');
    expect(screen.getByTestId('bbx-resilience-recurrence-value')).toHaveTextContent('5 in 30 days, 2 Armada versions');
    expect(screen.getByTestId('bbx-resilience-attempted-value')).toHaveTextContent('LandingRetry x2 (Expired)');
    expect(within(screen.getByTestId('bbx-resilience-pointers')).getByText('Code pointers (Armada 1.2.0 commit abc9999)')).toBeTruthy();
    expect(within(screen.getByTestId('bbx-resilience-change-1')).getByText('Acceptance test: Services.LandingPipeline: lands after the dock directory is deleted.')).toBeTruthy();
    expect(screen.getByTestId('bbx-resilience-regression-value')).toHaveTextContent('Services.LandingPipeline: Remove the dock directory and assert the landing recreates it.');
    // Order: the resilience section comes before How, Why and the user-facing recovery.
    const json = JSON.stringify(screen.toJSON());
    expect(json.indexOf('bbx-resilience')).toBeGreaterThan(json.indexOf('bbx-what-happened'));
    expect(json.indexOf('bbx-resilience')).toBeLessThan(json.indexOf('bbx-how'));
  });

  it('Draft Armada fix mission opens Ask with the top change prefilled', async () => {
    await renderPanel();
    await press('bbx-draft-fix');
    expect(mockRouter.push).toHaveBeenCalledWith(`/ask?prompt=${encodeURIComponent(armadaFixPrompt(IN_SCOPE, resilienceOf(IN_SCOPE)))}`);
  });

  it('copies a proposed change as a mission brief', async () => {
    await renderPanel();
    await press('bbx-resilience-copy-1');
    expect(clipboard.setStringAsync).toHaveBeenCalledWith('Recreate a missing dock worktree from the branch before landing\n\nRecreate the worktree before git runs.');
    await waitFor(() => expect(screen.getByTestId('toast-success')).toHaveTextContent('Armada fix brief copied.'));
  });

  it('outside handoff and landing scope the section is short and there is no badge or draft action', async () => {
    api.getMissionBlackBox.mockResolvedValue(withResilience(blackBoxEntry(), RESILIENCE_OUT_OF_SCOPE, { verdict: 'Transient' }));
    await renderPanel();
    expect(screen.getByTestId('bbx-resilience-none')).toHaveTextContent(/No Armada change suggested/);
    expect(screen.queryByTestId('bbx-suspected-defect')).toBeNull();
    expect(screen.queryByTestId('bbx-draft-fix')).toBeNull();
  });

  it('an entry analyzed before the resilience bias shows no section', async () => {
    api.getMissionBlackBox.mockResolvedValue(blackBoxEntry());
    await renderPanel();
    expect(screen.queryByTestId('bbx-resilience')).toBeNull();
  });
});
