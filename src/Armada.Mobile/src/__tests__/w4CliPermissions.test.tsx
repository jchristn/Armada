import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { CliPermissionRequest, CliPermissionRule } from '@dashboard/types/models';
import MoreLayout from '../app/(app)/(more)/_layout';
import CliRoute from '../app/(app)/(more)/cli-permissions';
import { ruleDraftError, ruleTarget } from '../screens/system/CliPermissionsHub';
import { emit, page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;
const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(params?.[k] ?? ''));

function request(over: Partial<CliPermissionRequest> = {}): CliPermissionRequest {
  return {
    id: 'cpr_1', toolName: 'Bash', summaryText: 'git push', inputText: '{"command":"git push"}', status: 'Pending', canDecide: true, canRemember: true,
    createdUtc: '2026-10-07T00:00:00Z', lastUpdateUtc: '2026-10-07T00:00:00Z', expiresUtc: '2099-10-07T00:00:00Z', ...over,
  } as CliPermissionRequest;
}
const RULE: CliPermissionRule = { id: 'cpl_1', tenantId: 'ten_1', scope: 'Vessel', vesselId: 'vsl_1', pattern: 'Bash(git status:*)', action: 'Allow', description: 'status', createdUtc: '2026-10-01T00:00:00Z' } as CliPermissionRule;

const ROUTES = { '(more)/_layout': MoreLayout, '(more)/more': () => null, '(more)/cli-permissions': CliRoute };

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listCliPermissionRequests.mockResolvedValue([request()]);
  api.listCliPermissionRules.mockResolvedValue([RULE]);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada' }]) as never);
});

describe('rule helpers', () => {
  it('validates drafts and names targets like the dashboard', () => {
    expect(ruleDraftError(t, { pattern: ' ', scope: 'Global' })).toBe('Enter a rule pattern.');
    expect(ruleDraftError(t, { pattern: 'Bash(ls)', scope: 'Vessel', vesselId: '' })).toBe('Choose a vessel.');
    expect(ruleDraftError(t, { pattern: 'Bash(ls)', scope: 'Captain', captainId: '' })).toBe('Choose a captain.');
    expect(ruleDraftError(t, { pattern: 'Bash(ls)', scope: 'Global' })).toBeNull();
    expect(ruleTarget(t, RULE, new Map([['vsl_1', 'armada']]), new Map())).toBe('Vessel armada');
    expect(ruleTarget(t, { ...RULE, scope: 'Global' }, new Map(), new Map())).toBe('Global');
  });
});

describe('CLI Tool Permissions', () => {
  it('lists pending requests and reloads on CLI permission events', async () => {
    const h = await renderW4Routes(ROUTES, '/cli-permissions');
    await waitFor(() => expect(screen.getByTestId('cli-permission-cpr_1')).toBeTruthy());
    expect(api.listCliPermissionRequests).toHaveBeenCalledWith({ status: 'Pending', limit: 200 });
    api.listCliPermissionRequests.mockResolvedValue([]);
    await emit(h, { type: 'cli_permission.resolved', data: {} });
    await waitFor(() => expect(screen.getByText('No CLI tool requests are waiting.')).toBeTruthy());
  });

  it('a ?request= link shows that request first even when the filter hides it', async () => {
    api.getCliPermissionRequest.mockResolvedValue(request({ id: 'cpr_9', status: 'Allowed', summaryText: 'npm test' }));
    await renderW4Routes(ROUTES, '/cli-permissions?request=cpr_9');
    await waitFor(() => expect(screen.getByTestId('cli-request-highlighted')).toBeTruthy());
    expect(api.getCliPermissionRequest).toHaveBeenCalledWith('cpr_9');
    expect(screen.getByTestId('cli-permission-cpr_9')).toBeTruthy();
  });

  it('creates a global rule (tenant admin)', async () => {
    api.createCliPermissionRule.mockResolvedValue(RULE);
    await renderW4Routes(ROUTES, '/cli-permissions?tab=rules', 'tenantAdmin');
    await waitFor(() => expect(screen.getByTestId('cli-rule-row-cpl_1')).toBeTruthy());
    expect(screen.getByText(/Vessel armada/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('cli-rules-create'));
    await waitFor(() => expect(screen.getByTestId('cli-rule-form-pattern')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('cli-rule-form-pattern'), 'Bash(npm test)');
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-rule-form-submit')); });
    expect(api.createCliPermissionRule).toHaveBeenCalledWith({ pattern: 'Bash(npm test)', action: 'Allow', scope: 'Global', vesselId: null, captainId: null, description: null });
  });

  it('opens a rule, edits it, and deletes it after confirmation', async () => {
    api.getCliPermissionRule.mockResolvedValue({ ...RULE, description: 'fresh' });
    api.updateCliPermissionRule.mockResolvedValue(RULE);
    api.deleteCliPermissionRule.mockResolvedValue(undefined as never);
    await renderW4Routes(ROUTES, '/cli-permissions?tab=rules');
    await waitFor(() => expect(screen.getByTestId('cli-rule-row-cpl_1')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('cli-rule-row-cpl_1'));
    await waitFor(() => expect(screen.getByText('fresh')).toBeTruthy());
    expect(api.getCliPermissionRule).toHaveBeenCalledWith('cpl_1');
    await fireEvent.press(screen.getByTestId('cli-rule-edit'));
    await waitFor(() => expect(screen.getByTestId('cli-rule-edit-form-pattern')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('cli-rule-edit-form-description'), 'changed');
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-rule-edit-form-submit')); });
    expect(api.updateCliPermissionRule).toHaveBeenCalledWith('cpl_1', { pattern: 'Bash(git status:*)', action: 'Allow', description: 'changed' });
    await fireEvent(screen.getByTestId('cli-rule-row-cpl_1-swipe'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    await waitFor(() => expect(screen.getByTestId('cli-rule-confirm-confirm')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('cli-rule-confirm-confirm')); });
    expect(api.deleteCliPermissionRule).toHaveBeenCalledWith('cpl_1');
  });

  it('regular users cannot create or edit rules', async () => {
    await renderW4Routes(ROUTES, '/cli-permissions?tab=rules', 'user');
    await waitFor(() => expect(screen.getByTestId('cli-rule-row-cpl_1')).toBeTruthy());
    expect(screen.queryByTestId('cli-rules-create')).toBeNull();
    expect(screen.queryByTestId('cli-rule-row-cpl_1-swipe')).toBeNull();
  });
});
