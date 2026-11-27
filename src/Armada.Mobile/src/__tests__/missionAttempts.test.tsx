import { act, fireEvent, screen } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { MissionAttempt, MissionAttemptHistory, ModelProfile, StageSubmission } from '@dashboard/types/models';
import catalog from '../../../Armada.Server/wwwroot/i18n/armada.json';
import { CaptainsHubScreen } from '../screens/captains/CaptainsHubScreen';
import { ModelProfileDetailScreen, recoveryValue, timeoutValue } from '../screens/captains/ModelProfileDetailScreen';
import { filterModelProfiles, modelProfileName } from '../screens/captains/ModelProfilesTab';
import { MissionAttemptsSection, MissionFindingsSection } from '../screens/operations/mission/MissionAttemptsSection';
import {
  attemptFacts,
  attemptsNewestFirst,
  decidingSubmissions,
  dispositionTone,
  findingLocation,
  formatAttemptDuration,
  severityTone,
} from '../screens/operations/mission/attemptsModel';
import { parsePushData } from '../push/payload';
import { page } from '../test/operationsClient';
import { setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;
const t = (key: string, vars?: Record<string, string | number>) =>
  key.replace(/\{\{(\w+)\}\}/g, (_m, name: string) => String(vars?.[name] ?? ''));

function attempt(over: Partial<MissionAttempt> = {}): MissionAttempt {
  return {
    outcomeId: 'sto_1', attempt: 1, reworkRound: 0, captainId: 'cpt_1', captainName: 'qwen-1', runtime: 'OpenCode', model: 'qwen3.8',
    modelProvider: 'ollama', modelEndpointId: null, disposition: 'Completed', failureKind: null, failureReason: null, runtimeFailureKind: null,
    contractRepairKind: null, isRepair: false, escalated: false, nextCaptainId: null, judgeVerdict: null, suspectedCause: 'None',
    isEvaluation: false, startedUtc: '2026-11-26T10:00:00Z', endedUtc: '2026-11-26T10:03:05Z', durationMs: 185000, ...over,
  };
}

function history(attempts: MissionAttempt[], over: Partial<MissionAttemptHistory> = {}): MissionAttemptHistory {
  return {
    missionId: 'msn_1', voyageId: null, persona: 'Worker', mode: 'Implementation', status: 'InProgress', captainId: 'cpt_2', tier: 'Standard',
    escalationCount: 1, escalatedFromCaptainId: 'cpt_1', escalatedFromModel: 'qwen3.8', reworkEscalated: false, redispatchAttempts: 0,
    outputRepairAttempts: 1, noOpRedispatchAttempts: 0, stallRecoveryAttempts: 0, attempts, ...over,
  };
}

function submission(attemptId: string, over: Partial<StageSubmission> = {}): StageSubmission {
  return {
    tenantId: null, missionId: 'msn_1', attemptId, persona: 'Judge', kind: 'Verdict', source: 'Mcp', createdUtc: '2026-11-26T10:05:00Z',
    submission: {
      kind: 'Verdict',
      verdict: {
        verdict: 'FAIL', correctness: 'Wrong', blastRadius: 'Low', sourceFidelity: 'Ok', summary: 'Off by one', specConflict: null,
        findings: [{ file: 'src/a.ts', line: 12, severity: 'High', scenario: 'Loop skips the last item', requiredFix: 'Use <= length - 1' }],
      },
    },
    ...over,
  };
}

function profile(over: Partial<ModelProfile> = {}): ModelProfile {
  return {
    id: 'mpf_1', tenantId: null,
    identity: { runtime: 'OpenCode', provider: 'ollama', model: 'qwen3.8', servedContextTokens: 32768, servedContextSource: 'Discovered' },
    capabilities: { toolsAvailable: null, mcpAvailable: false, probedToolsAvailable: true, probedMcpAvailable: null },
    rendering: { template: 'Compact', repeatContractAtEnd: true, maxFilesPerMission: 4 },
    recovery: { maxResumes: 3, unclassifiedResumes: null, transientGraceMinutes: null, judgeReworkRounds: null, outputRepairAttempts: null, escalateAfterResumes: null },
    tiering: { tier: 'Economy', suggestedTier: 'Standard', suggestedReason: 'High success rate', suggestedSamples: 24, suggestedUtc: '2026-11-25T00:00:00Z' },
    effectiveRecovery: { modelProfileId: 'mpf_1', maxResumes: 3, unclassifiedResumes: 1, transientGraceMinutes: 10, judgeReworkRounds: 2, outputRepairAttempts: 1, escalateAfterResumes: 3 },
    timeouts: { stallThresholdMinutes: 20, toolWaitHardLimitMinutes: null, maxRuntimeMinutes: null, firstOutputMinutes: null },
    speed: { tokensPerSecond: 15, firstOutputMs: 900, measuredUtc: '2026-11-25T00:00:00Z' },
    effectiveTimeouts: {
      modelProfileId: 'mpf_1', speedScale: 2, stallThresholdMinutes: 20, stallThresholdSource: 'Profile', toolWaitHardLimitMinutes: 60,
      toolWaitHardLimitSource: 'Global', maxRuntimeMinutes: 240, maxRuntimeSource: 'Measured', firstOutputMinutes: 10, firstOutputSource: 'Measured',
    },
    notes: null, createdUtc: '2026-11-20T00:00:00Z', lastUpdateUtc: '2026-11-25T00:00:00Z', ...over,
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  setMockParams({});
});

describe('attempt helpers', () => {
  it('maps dispositions and severities to tones', () => {
    expect(dispositionTone('Completed')).toBe('success');
    expect(dispositionTone('Failed')).toBe('failed');
    expect(dispositionTone('Escalated')).toBe('warning');
    expect(dispositionTone('Pending')).toBe('running');
    expect(dispositionTone('Cancelled')).toBe('cancelled');
    expect(severityTone('Critical')).toBe('failed');
    expect(severityTone('medium')).toBe('warning');
    expect(severityTone('Low')).toBe('info');
    expect(severityTone(null)).toBe('cancelled');
  });

  it('formats durations', () => {
    expect(formatAttemptDuration(null)).toBeNull();
    expect(formatAttemptDuration(-1)).toBeNull();
    expect(formatAttemptDuration(850)).toBe('850 ms');
    expect(formatAttemptDuration(42000)).toBe('42 s');
    expect(formatAttemptDuration(185000)).toBe('3 m 05 s');
    expect(formatAttemptDuration(3720000)).toBe('1 h 02 m');
  });

  it('lists the facts of an attempt and orders attempts newest first', () => {
    expect(attemptFacts(attempt())).toEqual([]);
    expect(attemptFacts(attempt({ failureKind: 'Unclassified', runtimeFailureKind: 'None', contractRepairKind: 'FormatOnly', judgeVerdict: 'FAIL' }))).toEqual([
      { label: 'Failure kind', value: 'Unclassified' },
      { label: 'Repair', value: 'FormatOnly' },
      { label: 'Judge verdict', value: 'FAIL' },
    ]);
    expect(attemptFacts(attempt({ isRepair: true, contractRepairKind: 'None' }))).toEqual([{ label: 'Repair', value: 'Rerun' }]);
    const ordered = attemptsNewestFirst([attempt({ attempt: 1 }), attempt({ attempt: 2 }), attempt({ attempt: 2, reworkRound: 1 })]);
    expect(ordered.map(a => `${a.attempt}.${a.reworkRound}`)).toEqual(['2.1', '2.0', '1.0']);
  });

  it('keeps the deciding submission of each attempt and formats finding locations', () => {
    const views = decidingSubmissions([
      submission('att_2'),
      submission('att_2', { createdUtc: '2026-11-26T10:04:00Z' }),
      submission('att_1', { kind: 'Result', submission: { kind: 'Result', result: { status: 'Completed', summary: 'Done', findings: [] } } }),
    ]);
    expect(views.map(v => [v.attemptId, v.outcome])).toEqual([['att_2', 'FAIL'], ['att_1', 'Completed']]);
    expect(views[0].findings).toHaveLength(1);
    expect(findingLocation({ file: 'a.ts', line: 3, severity: 'Low', scenario: 's', requiredFix: null })).toBe('a.ts:3');
    expect(findingLocation({ file: 'a.ts', line: null, severity: 'Low', scenario: 's', requiredFix: null })).toBe('a.ts');
    expect(findingLocation({ file: null, line: 3, severity: 'Low', scenario: 's', requiredFix: null })).toBeNull();
  });
});

describe('mission attempts and findings sections', () => {
  const mission = { id: 'msn_1', lastUpdateUtc: '2026-11-26T10:05:00Z' };

  it('shows the attempts timeline with escalation, repair, and counters', async () => {
    api.getMissionAttempts.mockResolvedValue(history([
      attempt({ attempt: 1, disposition: 'Escalated', escalated: true, failureKind: 'ToolCallInvalid', durationMs: 42000 }),
      attempt({ outcomeId: 'sto_2', attempt: 2, captainName: 'opus-1', model: 'claude-opus-5-5', contractRepairKind: 'FormatOnly' }),
    ]));
    await renderScreen(<MissionAttemptsSection mission={mission} />);
    expect(await screen.findByTestId('mission-attempts')).toBeTruthy();
    expect(screen.getByText('Attempts (2)')).toBeTruthy();
    expect(screen.getByTestId('mission-attempt-1-0').props.accessibilityLabel).toContain('Failure kind: ToolCallInvalid');
    expect(screen.getByTestId('mission-attempt-1-0').props.accessibilityLabel).toContain('Escalated');
    expect(screen.getByText('Repair: FormatOnly')).toBeTruthy();
    expect(screen.getByText(/Escalated from qwen3\.8/)).toBeTruthy();
    expect(screen.getByText(/Output repairs: 1/)).toBeTruthy();
  });

  it('is hidden without attempts and reloads on attempt events', async () => {
    api.getMissionAttempts.mockResolvedValue(history([]));
    const { socket } = await renderScreen(<MissionAttemptsSection mission={mission} />);
    expect(screen.queryByTestId('mission-attempts')).toBeNull();
    const calls = api.getMissionAttempts.mock.calls.length;
    api.getMissionAttempts.mockResolvedValue(history([attempt()]));
    await act(async () => {
      socket.message({ type: 'mission.escalated', data: { missionId: 'msn_1' } });
      await new Promise(r => setTimeout(r, 400));
    });
    expect(api.getMissionAttempts.mock.calls.length).toBeGreaterThan(calls);
    expect(await screen.findByTestId('mission-attempts')).toBeTruthy();
  });

  it('shows submitted verdicts with findings by severity', async () => {
    api.listMissionSubmissions.mockResolvedValue([submission('att_1')]);
    await renderScreen(<MissionFindingsSection mission={mission} />);
    expect(await screen.findByTestId('mission-submissions')).toBeTruthy();
    expect(screen.getByText('Off by one')).toBeTruthy();
    expect(screen.getByText('src/a.ts:12')).toBeTruthy();
    expect(screen.getByText('Required fix: Use <= length - 1')).toBeTruthy();
    expect(screen.getByTestId('mission-finding-0-0').props.accessibilityLabel).toContain('Severity High');
  });

  it('stays hidden when the routes fail', async () => {
    api.getMissionAttempts.mockRejectedValue(new Error('404'));
    api.listMissionSubmissions.mockRejectedValue(new Error('404'));
    await renderScreen(<><MissionAttemptsSection mission={mission} /><MissionFindingsSection mission={mission} /></>);
    await act(async () => { await Promise.resolve(); });
    expect(screen.queryByTestId('mission-attempts')).toBeNull();
    expect(screen.queryByTestId('mission-submissions')).toBeNull();
  });
});

describe('model profiles', () => {
  it('names and filters profiles', () => {
    expect(modelProfileName(profile())).toBe('qwen3.8 (ollama, OpenCode)');
    expect(modelProfileName(profile({ identity: { runtime: '', provider: null, model: null, servedContextTokens: null } }))).toBe('mpf_1');
    const rows = [profile(), profile({ id: 'mpf_2', identity: { runtime: 'ClaudeCode', provider: 'anthropic', model: 'claude-opus-5-5', servedContextTokens: null } })];
    expect(filterModelProfiles(rows, 'OPUS').map(p => p.id)).toEqual(['mpf_2']);
    expect(filterModelProfiles(rows, '').map(p => p.id)).toEqual(['mpf_1', 'mpf_2']);
  });

  it('describes recovery and timeouts in effect with their source', () => {
    expect(recoveryValue(profile(), 'maxResumes', t)).toBe('3 (override)');
    expect(recoveryValue(profile(), 'judgeReworkRounds', t)).toBe('2');
    expect(recoveryValue(profile({ effectiveRecovery: null }), 'judgeReworkRounds', t)).toBe('Global setting');
    expect(timeoutValue(profile(), 'stallThresholdMinutes', t)).toBe('20 (Profile)');
    expect(timeoutValue(profile(), 'maxRuntimeMinutes', t)).toBe('240 (Measured)');
    expect(timeoutValue(profile({ effectiveTimeouts: null }), 'stallThresholdMinutes', t)).toBe('20 (override)');
    expect(timeoutValue(profile({ effectiveTimeouts: null }), 'maxRuntimeMinutes', t)).toBe('Global setting');
  });

  it('shows a profile read-only: context with source, probed capabilities, tier suggestion, recovery, timeouts', async () => {
    api.getModelProfile.mockResolvedValue(profile());
    await renderScreen(<ModelProfileDetailScreen id="mpf_1" />);
    expect(await screen.findByTestId('model-profile-detail')).toBeTruthy();
    expect(screen.getByTestId('model-profile-context-value')).toHaveTextContent('32,768 tokens (Discovered)');
    expect(screen.getByText('Yes (Probed)')).toBeTruthy();
    expect(screen.getByText('No (Set)')).toBeTruthy();
    expect(screen.getByTestId('model-profile-suggested-tier-value')).toHaveTextContent('Standard from 24 attempts');
    expect(screen.getByTestId('model-profile-recovery-maxResumes-value')).toHaveTextContent('3 (override)');
    expect(screen.getByTestId('model-profile-timeout-firstOutputMinutes-value')).toHaveTextContent('10 (Measured)');
    expect(screen.queryByText('Save')).toBeNull();
  });

  it('the Captains hub has a Model Profiles tab', async () => {
    setMockParams({ tab: 'profiles' });
    api.listModelProfiles.mockResolvedValue(page([profile()]));
    await renderScreen(<CaptainsHubScreen />);
    expect(await screen.findByTestId('model-profile-row-mpf_1')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('model-profile-search'), 'nothing');
    expect(await screen.findByText('No model profiles match the current filters.')).toBeTruthy();
  });
});

describe('escalation and recovery pushes', () => {
  it('accepts escalated and recovery-exhausted pushes and opens the mission', () => {
    const escalated = parsePushData({ kind: 'escalated', url: '/missions/msn_1', entityId: 'msn_1', category: 'MissionEscalated' });
    expect(escalated?.kind).toBe('escalated');
    expect(escalated?.path).toBe('/missions/msn_1');
    expect(escalated?.category).toBe('MissionEscalated');
    expect(parsePushData({ kind: 'recovery_exhausted', url: '/missions/msn_1', entityId: 'msn_1' })?.path).toBe('/missions/msn_1');
  });
});

describe('localization', () => {
  it('every new string has a translation in every shipped locale', () => {
    const keys = [
      'Attempts ({{count}})', 'Attempt {{attempt}}, rework {{round}}', 'Escalated', 'Escalated from {{model}}', 'Escalations: {{count}}',
      'Redispatches: {{count}}', 'Output repairs: {{count}}', 'Stall recoveries: {{count}}', 'Failure kind', 'Repair', 'Judge verdict',
      'Submitted Results', 'Required fix: {{fix}}', 'Severity {{severity}}', 'No findings.', 'Model Profiles', 'Model Profile',
      'Recovery Policy', 'Timeouts', 'Search models', '{{value}} (override)', 'Global setting', 'Suggested tier',
    ];
    const locales = (catalog as { locales: Record<string, { terms?: Record<string, string>; phrases?: Record<string, string>; sections?: Record<string, string> }> }).locales;
    const missing: string[] = [];
    for (const [code, pack] of Object.entries(locales)) {
      for (const key of keys) {
        if (!pack.terms?.[key] && !pack.phrases?.[key] && !pack.sections?.[key]) missing.push(`${code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });
});
