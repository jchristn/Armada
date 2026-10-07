import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Server from './Server';
import { getHealth, getSettings, updateSettings } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { onlyCallArgs } from '../test/mockCalls';

vi.mock('../api/client', () => ({
  getHealth: vi.fn(),
  getSettings: vi.fn(),
  updateSettings: vi.fn(),
  stopServer: vi.fn(),
  restartServer: vi.fn(),
  resetServer: vi.fn(),
  rebuildServer: vi.fn(),
  getRebuildStatus: vi.fn(() => Promise.resolve(null)),
  rollbackServer: vi.fn(),
  getVesselBranches: vi.fn(() => Promise.resolve({ branches: [] })),
  listVessels: vi.fn(() => Promise.resolve({ objects: [] })),
  downloadBackup: vi.fn(),
  restoreBackup: vi.fn(),
  getProxySessionContext: vi.fn(() => Promise.resolve(null)),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));
vi.mock('../context/WebSocketContext', () => ({ useWebSocket: () => ({ connected: true, reconnectCount: 0, send: vi.fn(), subscribe: () => () => {} }) }));
vi.mock('../components/settings/ImportFleetActionSettings', () => ({ default: () => null }));
vi.mock('../components/settings/RetentionSettings', () => ({ default: () => null }));
vi.mock('../components/settings/CliPermissionSettings', () => ({ default: () => null }));
vi.mock('../components/vessels/health/RepositoryHealthSettingsSection', () => ({ default: () => null }));
vi.mock('../components/shared/LogViewer', () => ({ default: () => null }));

const baseSettings = {
  admiralPort: 7890, mcpPort: 7891, maxCaptains: 4, heartbeatIntervalSeconds: 30, stallThresholdMinutes: 10,
  idleCaptainTimeoutSeconds: 0, planningSessionInactivityTimeoutMinutes: 60, planningSessionAbandonmentTimeoutMinutes: 1440,
  planningSessionRetentionDays: 30, dataDirectory: '/d', databasePath: '/d/db', logDirectory: '/d/logs',
  docksDirectory: '/d/docks', reposDirectory: '/d/repos', remoteControl: { enabled: false },
};

function renderServer() {
  render(<MemoryRouter><Server /></MemoryRouter>);
}

describe('Server agent settings landing mode', () => {
  beforeEach(() => {
    vi.mocked(getHealth).mockResolvedValue({ status: 'Healthy', uptime: '1m', version: '1.0.0' } as never);
    vi.mocked(updateSettings).mockReset();
  });

  it('replaces Auto-Create Pull Requests with a Default Landing Mode select that loads and saves LandingMode', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, landingMode: 'PullRequest' } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    renderServer();

    const select = await screen.findByLabelText('Default Landing Mode') as HTMLSelectElement;
    expect(select.value).toBe('PullRequest');
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue', 'None']);
    expect(within(select).getByRole('option', { name: 'Merge and Push -- local merge, then push to the remote' })).toBeInTheDocument();
    expect(screen.queryByText('Auto-Create Pull Requests')).toBeNull();
    expect(screen.getByText('How finished missions land when neither the vessel nor the voyage sets a landing mode (default Merge and Push).')).toBeInTheDocument();

    fireEvent.change(select, { target: { value: 'LocalMerge' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Agent Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>;
    expect(body.landingMode).toBe('LocalMerge');
    expect(body).not.toHaveProperty('autoCreatePr');
  });

  it('shows and saves Merge and Push when the server sends no landing mode', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    renderServer();

    const select = await screen.findByLabelText('Default Landing Mode') as HTMLSelectElement;
    expect(select.value).toBe('MergeAndPush');
    fireEvent.click(screen.getByRole('button', { name: 'Save Agent Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect((onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>).landingMode).toBe('MergeAndPush');
  });
});
