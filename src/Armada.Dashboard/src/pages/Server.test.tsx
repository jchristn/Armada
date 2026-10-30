import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Server from './Server';
import { getHealth, getSettings, updateSettings } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { getDefaultRemoteControlSettings, REDACTED_SECRET } from '../lib/serverSettings';
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
    expect(within(select).getByRole('option', { name: 'Merge and Push -- verify and push to the remote' })).toBeInTheDocument();
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

describe('Server agent settings Harbor requirement', () => {
  beforeEach(() => {
    vi.mocked(getHealth).mockResolvedValue({ status: 'Healthy', uptime: '1m', version: '1.0.0' } as never);
    vi.mocked(updateSettings).mockReset();
  });

  it('loads and saves Require Harbor for Launch with an explaining tooltip', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, requireHarborForLaunch: false } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    renderServer();

    const box = await screen.findByLabelText('Require Harbor for Launch') as HTMLInputElement;
    expect(box.checked).toBe(false);
    expect(box.title).toContain('In Docker the Admiral has no claude, codex or mux installed');
    fireEvent.click(box);
    fireEvent.click(screen.getByRole('button', { name: 'Save Agent Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect((onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>).requireHarborForLaunch).toBe(true);
  });

  it('hides the switch and leaves the value out for a server that does not report it', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    renderServer();

    await screen.findByLabelText('Default Landing Mode');
    expect(screen.queryByLabelText('Require Harbor for Launch')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Save Agent Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect(onlyCallArgs(vi.mocked(updateSettings))[0]).not.toHaveProperty('requireHarborForLaunch');
  });
});

const PLANNING_TIMEOUT = 'Minutes before an idle planning session is automatically ended (0 = disabled)';
const ADMIRAL_PORT = 'REST API port (1-65535)';
const MAX_CAPTAINS = 'Maximum captains (0 = unlimited)';
const TUNNEL_URL = 'Proxy base URL or tunnel endpoint. http/https will be normalized to ws/wss and /tunnel will be added automatically when needed.';
const PROXY_PASSWORD = 'Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.';
const RELOAD_SETTINGS = 'Reload health, settings, and tunnel status from the Admiral. Unsaved edits on this page are replaced.';
const AUTO_REFRESH_KEY = 'armada_autorefresh_server';

function serverSettings(overrides: Record<string, unknown> = {}) {
  return {
    ...baseSettings,
    remoteControl: {
      ...getDefaultRemoteControlSettings(),
      enabled: false,
      password: REDACTED_SECRET,
      enrollmentToken: REDACTED_SECRET,
    },
    ...overrides,
  };
}

function installAutoRefreshStorage() {
  const values = new Map<string, string>();
  const storage = {
    getItem: (key: string) => (values.has(key) ? values.get(key)! : null),
    setItem: (key: string, value: string) => { values.set(key, String(value)); },
    removeItem: (key: string) => { values.delete(key); },
    clear: () => { values.clear(); },
    key: (index: number) => Array.from(values.keys())[index] ?? null,
    get length() { return values.size; },
  };
  vi.stubGlobal('localStorage', storage);
  storage.setItem(AUTO_REFRESH_KEY, '15');
}

describe('Server page unsaved settings', () => {
  beforeEach(() => {
    installAutoRefreshStorage();
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(getHealth).mockResolvedValue({ status: 'Healthy', uptime: '1m', version: '1.0.0' } as never);
    vi.mocked(getSettings).mockReset();
    vi.mocked(updateSettings).mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  async function shown(label: string): Promise<HTMLInputElement> {
    return await screen.findByLabelText(label) as HTMLInputElement;
  }

  async function advanceRefresh() {
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  }

  it('keeps an edited planning timeout when auto-refresh returns the stored value', async () => {
    vi.mocked(getSettings).mockResolvedValue(serverSettings({ planningSessionInactivityTimeoutMinutes: 60 }) as never);
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    expect(planning.value).toBe('60');
    fireEvent.change(planning, { target: { value: '90' } });
    expect(planning.value).toBe('90');

    await advanceRefresh();
    await waitFor(() => expect(getSettings).toHaveBeenCalledTimes(2));
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('90');
  });

  it('keeps an unsaved planning timeout when the port section is saved and adopts the stored port', async () => {
    vi.mocked(getSettings).mockResolvedValue(serverSettings({ admiralPort: 7890, planningSessionInactivityTimeoutMinutes: 60 }) as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve(serverSettings({
      ...(data as Record<string, unknown>),
      admiralPort: 8001,
      planningSessionInactivityTimeoutMinutes: 60,
    }) as never));
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    fireEvent.change(planning, { target: { value: '90' } });
    fireEvent.change(await shown(ADMIRAL_PORT), { target: { value: '8000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Server Config' }));

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect((onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>).admiralPort).toBe(8000);
    expect((await shown(ADMIRAL_PORT)).value).toBe('8001');
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('90');
  });

  it('adopts a newer server value for a field the operator did not edit', async () => {
    vi.mocked(getSettings)
      .mockResolvedValueOnce(serverSettings({ maxCaptains: 4, planningSessionInactivityTimeoutMinutes: 60 }) as never)
      .mockResolvedValue(serverSettings({ maxCaptains: 9, planningSessionInactivityTimeoutMinutes: 60 }) as never);
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    fireEvent.change(planning, { target: { value: '90' } });
    await advanceRefresh();
    await waitFor(() => expect(getSettings).toHaveBeenCalledTimes(2));
    expect((await shown(MAX_CAPTAINS)).value).toBe('9');
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('90');
  });

  it('keeps an edited tunnel URL and a typed proxy password when another section is saved', async () => {
    const initial = serverSettings({ admiralPort: 7890 });
    vi.mocked(getSettings).mockResolvedValue(initial as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve(serverSettings({
      ...(data as Record<string, unknown>),
      admiralPort: 7900,
      remoteControl: (initial as { remoteControl: Record<string, unknown> }).remoteControl,
    }) as never));
    renderServer();

    fireEvent.change(await shown(TUNNEL_URL), { target: { value: 'https://proxy.example.test/tunnel' } });
    fireEvent.change(await shown(PROXY_PASSWORD), { target: { value: 'typed-secret' } });
    fireEvent.change(await shown(ADMIRAL_PORT), { target: { value: '7900' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Server Config' }));

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>;
    expect(body).not.toHaveProperty('remoteControl');
    expect((await shown(ADMIRAL_PORT)).value).toBe('7900');
    expect((await shown(TUNNEL_URL)).value).toBe('https://proxy.example.test/tunnel');
    expect((await shown(PROXY_PASSWORD)).value).toBe('typed-secret');
  });

  it('leaves an unedited redacted secret unchanged when another section is saved', async () => {
    vi.mocked(getSettings).mockResolvedValue(serverSettings({ planningSessionInactivityTimeoutMinutes: 60 }) as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve(serverSettings(data as Record<string, unknown>) as never));
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    fireEvent.change(planning, { target: { value: '90' } });
    expect((await shown(PROXY_PASSWORD)).value).toBe(REDACTED_SECRET);
    fireEvent.click(screen.getByRole('button', { name: 'Save Server Config' }));

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>;
    expect(body).not.toHaveProperty('remoteControl');
    expect((await shown(PROXY_PASSWORD)).value).toBe(REDACTED_SECRET);
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('90');
  });

  it('replaces unsaved edits when the operator uses the reload button', async () => {
    vi.mocked(getSettings).mockResolvedValue(serverSettings({ planningSessionInactivityTimeoutMinutes: 60 }) as never);
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    fireEvent.change(planning, { target: { value: '90' } });
    fireEvent.click(screen.getByRole('button', { name: RELOAD_SETTINGS }));
    await waitFor(() => expect(getSettings).toHaveBeenCalledTimes(2));
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('60');
  });

  it('keeps an unsaved planning timeout when Ask settings are saved', async () => {
    vi.mocked(getSettings).mockResolvedValue(serverSettings({ planningSessionInactivityTimeoutMinutes: 60 }) as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve(serverSettings({
      ...(data as Record<string, unknown>),
      planningSessionInactivityTimeoutMinutes: 60,
    }) as never));
    renderServer();

    const planning = await shown(PLANNING_TIMEOUT);
    fireEvent.change(planning, { target: { value: '90' } });
    fireEvent.change(await shown('Conversation history (messages)'), { target: { value: '40' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Ask Armada Settings' }));

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect((await shown(PLANNING_TIMEOUT)).value).toBe('90');
    expect((await shown('Conversation history (messages)')).value).toBe('40');
  });
});
