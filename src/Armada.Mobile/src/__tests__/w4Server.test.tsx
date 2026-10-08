import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { REDACTED_SECRET } from '@dashboard/lib/serverSettings';
import MoreLayout from '../app/(app)/(more)/_layout';
import ServerRoute from '../app/(app)/(more)/server';
import * as files from '../platform/files';
import { diagnosticsSummary } from '../screens/system/DiagnosticsTab';
import { intOr, mergePush, pushPayload, remoteControlPayload, secretDraft, secretToSend } from '../screens/system/settingsModel';
import { page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
jest.mock('../platform/files', () => ({ ensureNativePlatform: jest.fn(), pickBackupFile: jest.fn(), reauthenticateForExport: jest.fn(async () => true) }));

const api = client as jest.Mocked<typeof client>;
const platform = files as jest.Mocked<typeof files>;

const HEALTH = {
  status: 'Healthy', timestamp: '2026-10-07T10:00:00Z', startUtc: '2026-10-07T09:00:00Z', uptime: '1.00:00:00', version: '1.0.0',
  ports: { admiral: 7890, mcp: 7891, webSocket: 7892 },
};

function settings(over: Record<string, unknown> = {}) {
  return {
    admiralPort: 7890, mcpPort: 7891, maxCaptains: 5, heartbeatIntervalSeconds: 30, stallThresholdMinutes: 10, idleCaptainTimeoutSeconds: 0,
    planningSessionInactivityTimeoutMinutes: 60, planningSessionAbandonmentTimeoutMinutes: 240, planningSessionRetentionDays: 30,
    landingMode: 'MergeAndPush', dataDirectory: '/data', databasePath: '/data/armada.db', logDirectory: '/data/logs', docksDirectory: '/data/docks',
    reposDirectory: '/data/repos', selfVesselId: null, rebuildSlotRetentionCount: 3,
    remoteControl: {
      enabled: false, tunnelUrl: null, instanceId: null, enrollmentToken: REDACTED_SECRET, password: REDACTED_SECRET, connectTimeoutSeconds: 15,
      heartbeatIntervalSeconds: 30, reconnectBaseDelaySeconds: 5, reconnectMaxDelaySeconds: 60, allowInvalidCertificates: false,
    },
    push: { enabled: true, expoAccessToken: REDACTED_SECRET, categories: ['AskProposal', 'MissionFailed'], maxPerUserPerMinute: 20, dedupeWindowSeconds: 300 },
    ...over,
  };
}

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/server': ServerRoute,
  '(more)/setup': () => null,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.getHealth.mockResolvedValue(HEALTH as never);
  api.getSettings.mockResolvedValue(settings() as never);
  api.updateSettings.mockImplementation(async (body) => ({ ...settings(), ...body }) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'Default' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada' }]) as never);
  api.getRebuildStatus.mockResolvedValue({ status: null } as never);
  api.getDoctor.mockResolvedValue([] as never);
});

describe('settings model (secrets are never shown; the redacted value keeps them)', () => {
  it('sends the redacted value back unless a new secret is typed or clearing is asked', () => {
    expect(secretToSend(secretDraft(REDACTED_SECRET))).toBe(REDACTED_SECRET);
    expect(secretToSend({ ...secretDraft(REDACTED_SECRET), typed: ' new-token ' })).toBe('new-token');
    expect(secretToSend({ ...secretDraft(REDACTED_SECRET), clear: true })).toBeNull();
    expect(secretToSend(secretDraft(null))).toBeNull();
  });

  it('builds the remote control and push payloads', () => {
    const rc = settings().remoteControl;
    expect(remoteControlPayload(rc, secretDraft(REDACTED_SECRET), { ...secretDraft(REDACTED_SECRET), typed: 'pw' })).toMatchObject({ enrollmentToken: REDACTED_SECRET, password: 'pw' });
    const push = mergePush(settings().push as never);
    expect(pushPayload(push, secretDraft(REDACTED_SECRET)).expoAccessToken).toBe(REDACTED_SECRET);
    expect(pushPayload(push, { ...secretDraft(REDACTED_SECRET), clear: true }).expoAccessToken).toBe('');
    expect(mergePush({ categories: ['MissionFailed', 'Bogus' as never] }).categories).toEqual(['MissionFailed']);
    expect(mergePush(null).categories).toHaveLength(8);
  });

  it('parses numbers like the dashboard (blank or 0 gives the fallback)', () => {
    expect(intOr('12', 5)).toBe(12);
    expect(intOr('', 5)).toBe(5);
    expect(intOr('0', 5)).toBe(5);
    expect(intOr('x', 0)).toBe(0);
  });

  it('summarizes diagnostics like the Doctor page', () => {
    expect(diagnosticsSummary([])).toMatchObject({ verdict: null });
    expect(diagnosticsSummary([{ name: 'a', status: 'Pass', message: '' }])).toMatchObject({ pass: 1, verdict: 'Healthy' });
    expect(diagnosticsSummary([{ name: 'a', status: 'Warn', message: '' }, { name: 'b', status: 'Pass', message: '' }])).toMatchObject({ warn: 1, verdict: 'Warnings' });
    expect(diagnosticsSummary([{ name: 'a', status: 'Fail', message: '' }])).toMatchObject({ fail: 1, verdict: 'Unhealthy' });
  });
});

describe('Settings > Server', () => {
  it('changes the Default Landing Mode and saves the agent settings', async () => {
    const h = await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-landing-mode')).toBeTruthy());
    expect(screen.getByText('1.0.0')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('settings-landing-mode'));
    await waitFor(() => expect(screen.getByTestId('settings-landing-mode-option-LocalMerge')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-landing-mode-option-LocalMerge'));
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-agent-save')); });
    expect(api.updateSettings).toHaveBeenCalledWith({ heartbeatIntervalSeconds: 30, stallThresholdMinutes: 10, idleCaptainTimeoutSeconds: 0, landingMode: 'LocalMerge' });
    await waitFor(() => expect(h.notifications().toasts.map((x) => x.message)).toContain('Agent settings saved'));
  });

  it('never shows stored secrets and sends the redacted values back on save', async () => {
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-push-save')).toBeTruthy());
    expect(screen.queryByText(REDACTED_SECRET)).toBeNull();
    expect(screen.getByTestId('settings-push-token').props.value).toBe('');
    expect(screen.getByTestId('settings-remote-password').props.value).toBe('');
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-push-save')); });
    expect(api.updateSettings).toHaveBeenCalledWith({ push: expect.objectContaining({ expoAccessToken: REDACTED_SECRET, categories: ['AskProposal', 'MissionFailed'] }) });
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-remote-save')); });
    expect(api.updateSettings).toHaveBeenCalledWith({ remoteControl: expect.objectContaining({ enrollmentToken: REDACTED_SECRET, password: REDACTED_SECRET }) });
  });

  it('a typed token replaces the stored one; clearing removes it', async () => {
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-push-token')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('settings-push-token'), 'exp-123');
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-push-save')); });
    expect(api.updateSettings).toHaveBeenLastCalledWith({ push: expect.objectContaining({ expoAccessToken: 'exp-123' }) });
  });

  it('asks before enabling the remote tunnel', async () => {
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-remote-enabled')).toBeTruthy());
    await fireEvent(screen.getByTestId('settings-remote-enabled'), 'valueChange', true);
    await waitFor(() => expect(screen.getByText('Enabling remote tunnel will enable remote connectivity to this Armada instance. Are you sure?')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-remote-confirm-confirm'));
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-remote-save')); });
    expect(api.updateSettings).toHaveBeenCalledWith({ remoteControl: expect.objectContaining({ enabled: true }) });
  });

  it('backup warns about the secrets inside, re-authenticates the owner, then shares (F-56)', async () => {
    api.downloadBackup.mockResolvedValue(undefined);
    platform.reauthenticateForExport.mockResolvedValue(true);
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-backup')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-backup'));
    await waitFor(() => expect(screen.getByText(/receives all of that/)).toBeTruthy());
    expect(platform.reauthenticateForExport).not.toHaveBeenCalled();
    expect(api.downloadBackup).not.toHaveBeenCalled();
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-backup-confirm-confirm')); });
    expect(platform.reauthenticateForExport).toHaveBeenCalled();
    expect(platform.ensureNativePlatform).toHaveBeenCalled();
    expect(api.downloadBackup).toHaveBeenCalled();
  });

  it('backup is not exported when the owner is not verified', async () => {
    platform.reauthenticateForExport.mockResolvedValue(false);
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-backup')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-backup'));
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-backup-confirm-confirm')); });
    expect(platform.reauthenticateForExport).toHaveBeenCalled();
    expect(api.downloadBackup).not.toHaveBeenCalled();
  });

  it('restore names the server, needs `restore` typed, and deletes the picked copy (F-56)', async () => {
    const file = { name: 'armada-backup.zip', arrayBuffer: async () => new ArrayBuffer(4) };
    const dispose = jest.fn();
    platform.pickBackupFile.mockResolvedValue({ file, dispose });
    api.restoreBackup.mockResolvedValue({} as never);
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-restore')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-restore')); });
    await waitFor(() => expect(screen.getByText('Restore the database of Test (http://h:1) from "armada-backup.zip"? The current data is replaced by the backup.')).toBeTruthy());
    expect(screen.getByTestId('settings-restore-confirm-confirm')).toBeDisabled();
    await fireEvent.changeText(screen.getByTestId('settings-restore-confirm-typed'), 'restore');
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-restore-confirm-confirm')); });
    expect(api.restoreBackup).toHaveBeenCalledWith(file);
    expect(dispose).toHaveBeenCalled();
  });

  it('a cancelled restore uploads nothing and still deletes the picked copy', async () => {
    const dispose = jest.fn();
    platform.pickBackupFile.mockResolvedValue({ file: { name: 'b.zip', arrayBuffer: async () => new ArrayBuffer(1) }, dispose });
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-restore')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-restore')); });
    await waitFor(() => expect(screen.getByTestId('settings-restore-confirm-cancel')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-restore-confirm-cancel'));
    expect(api.restoreBackup).not.toHaveBeenCalled();
    expect(dispose).toHaveBeenCalled();
  });

  it('rebuilds the chosen ref and factory-resets only after confirmation', async () => {
    api.rebuildServer.mockResolvedValue({ status: 'Building', slot: 'slot-2', log: 'building' } as never);
    api.resetServer.mockResolvedValue(undefined);
    await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-rebuild-ref')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('settings-rebuild-ref'), 'v1.0.0');
    await fireEvent.press(screen.getByTestId('settings-rebuild'));
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-action-confirm-confirm')); });
    expect(api.rebuildServer).toHaveBeenCalledWith({ Ref: 'v1.0.0' });
    await waitFor(() => expect(screen.getByText('building')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-rebuild-log-close'));
    await fireEvent.press(screen.getByTestId('settings-factory-reset'));
    expect(api.resetServer).not.toHaveBeenCalled();
    await act(async () => { await fireEvent.press(screen.getByTestId('settings-action-confirm-confirm')); });
    expect(api.resetServer).toHaveBeenCalled();
  });

  it('tenant admins get no backup, server actions, or CLI permission defaults', async () => {
    await renderW4Routes(ROUTES, '/server', 'tenantAdmin');
    await waitFor(() => expect(screen.getByTestId('settings-agent-save')).toBeTruthy());
    expect(screen.queryByTestId('settings-backup')).toBeNull();
    expect(screen.queryByTestId('settings-actions')).toBeNull();
    expect(screen.queryByTestId('settings-cli-ask-default')).toBeNull();
  });

  it('offers the setup wizard when the deployment has no captain', async () => {
    api.listCaptains.mockResolvedValue(page([]) as never);
    const h = await renderW4Routes(ROUTES, '/server');
    await waitFor(() => expect(screen.getByTestId('settings-setup-banner')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('settings-open-setup'));
    await waitFor(() => expect(h.getPathname()).toBe('/setup'));
  });
});

describe('Settings > Diagnostics', () => {
  it('runs the checks on open and shows the verdict', async () => {
    api.getDoctor.mockResolvedValue([
      { name: 'Database', status: 'Pass', message: 'ok' },
      { name: 'Git', status: 'Warn', message: 'old git' },
    ] as never);
    await renderW4Routes(ROUTES, '/server?tab=diagnostics');
    await waitFor(() => expect(screen.getByText('old git')).toBeTruthy());
    expect(screen.getByLabelText('Passed: 1')).toBeTruthy();
    expect(screen.getByLabelText('Warnings: 1')).toBeTruthy();
    expect(api.getDoctor).toHaveBeenCalledTimes(1);
    await act(async () => { await fireEvent.press(screen.getByTestId('diagnostics-run')); });
    expect(api.getDoctor).toHaveBeenCalledTimes(2);
  });
});
