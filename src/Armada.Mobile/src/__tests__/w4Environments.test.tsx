import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { DeploymentEnvironment } from '@dashboard/types/models';
import WorkLayout from '../app/(app)/(work)/_layout';
import DeliveryRoute from '../app/(app)/(work)/delivery';
import EnvironmentRoute from '../app/(app)/(work)/environments/[id]';
import { resolveHubTab } from '../components/resource/Hub';
import { prefillQuery } from '../resource/links';
import { environmentPayload, newEnvironmentValues } from '../screens/delivery/environmentForm';
import { page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

function env(over: Partial<DeploymentEnvironment> = {}): DeploymentEnvironment {
  return {
    id: 'env_1', tenantId: 'ten_1', userId: 'usr_1', vesselId: 'vsl_1', name: 'production', description: 'Prod', kind: 'Production',
    configurationSource: null, baseUrl: 'https://prod.example.com', healthEndpoint: '/health', accessNotes: null, deploymentRules: null,
    verificationDefinitions: [], rolloutMonitoringWindowMinutes: 60, rolloutMonitoringIntervalSeconds: 300, alertOnRegression: true,
    requiresApproval: true, isDefault: true, active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

const ROUTES = {
  '(work)/_layout': WorkLayout,
  '(work)/home': () => null,
  '(work)/delivery': DeliveryRoute,
  '(work)/environments/[id]': EnvironmentRoute,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  api.listEnvironments.mockResolvedValue(page([env(), env({ id: 'env_2', name: 'staging', kind: 'Staging', requiresApproval: false, isDefault: false, active: false })]) as never);
  api.listDeployments.mockResolvedValue(page([]) as never);
});

describe('hub tabs', () => {
  it('resolves the URL tab, ignoring hidden and unknown tabs', () => {
    const tabs = [{ key: 'a', label: 'A', render: () => null }, { key: 'b', label: 'B', hidden: true, render: () => null }, { key: 'c', label: 'C', render: () => null }];
    expect(resolveHubTab(tabs, 'c', 'a')).toBe('c');
    expect(resolveHubTab(tabs, 'b', 'a')).toBe('a');
    expect(resolveHubTab(tabs, ['c', 'a'], 'a')).toBe('c');
    expect(resolveHubTab(tabs, undefined, 'b')).toBe('a');
  });

  it('builds prefill links without blanks', () => {
    expect(prefillQuery({ a: 'x y', b: null, c: '', d: 'z' })).toBe('?a=x%20y&d=z');
    expect(prefillQuery({})).toBe('');
  });
});

describe('environment form', () => {
  it('builds the dashboard payload: blanks to null and monitoring clamped', () => {
    const values = { ...newEnvironmentValues({ vesselId: 'vsl_1', kind: 'Staging' }), baseUrl: '  ', rolloutMonitoringWindowMinutes: '-5', rolloutMonitoringIntervalSeconds: '10' };
    const payload = environmentPayload(values, []);
    expect(payload).toMatchObject({ vesselId: 'vsl_1', name: 'Environment', kind: 'Staging', baseUrl: null, rolloutMonitoringWindowMinutes: 0, rolloutMonitoringIntervalSeconds: 30, active: true });
    expect(newEnvironmentValues({ kind: 'Bogus' }).kind).toBe('Development');
  });
});

describe('Delivery > Environments', () => {
  it('lists environments with counts, filters by search, and opens one', async () => {
    const h = await renderW4Routes(ROUTES, '/delivery?tab=environments');
    await waitFor(() => expect(screen.getByText('production')).toBeTruthy());
    expect(screen.getByText('staging')).toBeTruthy();
    expect(screen.getByLabelText('Total Environments: 2')).toBeTruthy();
    expect(screen.getByLabelText('Require Approval: 1')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('environments-search'), 'stag');
    await waitFor(() => expect(screen.queryByText('production')).toBeNull());

    // Phones open the environment's own route (the same URL as the dashboard).
    await fireEvent.press(screen.getByTestId('environment-row-env_2'));
    await waitFor(() => expect(h.getPathname()).toBe('/environments/env_2'));
  });

  it('creates an environment from the form (tenant admin)', async () => {
    api.createEnvironment.mockResolvedValue(env({ id: 'env_9', name: 'qa' }));
    await renderW4Routes(ROUTES, '/delivery?tab=environments', 'tenantAdmin');
    await waitFor(() => expect(screen.getByText('production')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('environments-create'));
    await waitFor(() => expect(screen.getByTestId('environment-form-name')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('environment-form-name'), 'qa');
    await act(async () => { await fireEvent.press(screen.getByTestId('environment-form-submit')); });
    await waitFor(() => expect(api.createEnvironment).toHaveBeenCalledWith(expect.objectContaining({ name: 'qa', kind: 'Development', verificationDefinitions: [] })));
    expect(api.listEnvironments).toHaveBeenCalledTimes(2);
  });

  it('hides create and row actions from regular users', async () => {
    await renderW4Routes(ROUTES, '/delivery?tab=environments', 'user');
    await waitFor(() => expect(screen.getByText('production')).toBeTruthy());
    expect(screen.queryByTestId('environments-create')).toBeNull();
    expect(screen.queryByTestId('environment-row-env_1-swipe')).toBeNull();
  });

  it('deletes from the row action after confirmation', async () => {
    api.deleteEnvironment.mockResolvedValue(undefined as never);
    await renderW4Routes(ROUTES, '/delivery?tab=environments');
    await waitFor(() => expect(screen.getByText('production')).toBeTruthy());
    await fireEvent(screen.getByTestId('environment-row-env_1-swipe'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    await waitFor(() => expect(screen.getByText('Delete "production"? This removes only the environment record.')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('environment-confirm-confirm')); });
    expect(api.deleteEnvironment).toHaveBeenCalledWith('env_1');
  });
});

describe('environment detail', () => {
  it('shows the environment and adds a verification definition', async () => {
    api.getEnvironment.mockResolvedValue(env());
    api.updateEnvironment.mockImplementation(async (_id, body) => env({ verificationDefinitions: body.verificationDefinitions ?? [] }));
    await renderW4Routes(ROUTES, '/environments/env_1');
    await waitFor(() => expect(screen.getByTestId('environment-title')).toHaveTextContent('production'));
    expect(screen.getByText('https://prod.example.com')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('environment-add-verification'));
    await waitFor(() => expect(screen.getByTestId('verification-form-path')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('verification-form-path'), '/ready');
    await act(async () => { await fireEvent.press(screen.getByTestId('verification-form-submit')); });
    await waitFor(() => expect(api.updateEnvironment).toHaveBeenCalledWith('env_1', expect.objectContaining({
      verificationDefinitions: [expect.objectContaining({ method: 'GET', path: '/ready', expectedStatusCode: 200, active: true })],
    })));
    await waitFor(() => expect(screen.getByText('GET /ready → 200')).toBeTruthy());
  });

  it('new opens the create form prefilled from the link', async () => {
    api.createEnvironment.mockResolvedValue(env({ id: 'env_5', name: 'dev' }));
    api.getEnvironment.mockResolvedValue(env({ id: 'env_5', name: 'dev' }));
    const h = await renderW4Routes(ROUTES, '/environments/new?vesselId=vsl_1&kind=Test&name=dev');
    await waitFor(() => expect(screen.getByTestId('environment-form-name')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('environment-form-submit')); });
    expect(api.createEnvironment).toHaveBeenCalledWith(expect.objectContaining({ vesselId: 'vsl_1', kind: 'Test', name: 'dev' }));
    await waitFor(() => expect(h.getPathname()).toBe('/environments/env_5'));
  });
});
