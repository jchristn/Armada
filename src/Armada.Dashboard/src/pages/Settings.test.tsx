import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Settings from './Settings';
import { getHealth, getSettings, updateSettings } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { onlyCallArgs } from '../test/mockCalls';

vi.mock('../api/client', () => ({
  getHealth: vi.fn(),
  getSettings: vi.fn(),
  updateSettings: vi.fn(),
  getProxySessionContext: vi.fn(() => Promise.resolve(null)),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const baseSettings = {
  admiralPort: 7890, mcpPort: 7891, maxCaptains: 4, heartbeatIntervalSeconds: 30, stallThresholdMinutes: 10,
  idleCaptainTimeoutSeconds: 0, dataDirectory: '/d', databasePath: '/d/db', logDirectory: '/d/logs', docksDirectory: '/d/docks', reposDirectory: '/d/repos',
};

describe('Settings default landing mode', () => {
  beforeEach(() => {
    vi.mocked(getHealth).mockResolvedValue({ status: 'Healthy', uptime: '1m', version: '1.0.0' } as never);
    vi.mocked(updateSettings).mockReset();
  });

  it('loads the landing mode, saves LandingMode, and sends no autoCreatePr', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, landingMode: 'MergeQueue' } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    render(<Settings />);

    const select = await screen.findByLabelText('Default Landing Mode') as HTMLSelectElement;
    expect(select.value).toBe('MergeQueue');
    expect(screen.queryByText('Auto-Create Pull Requests')).toBeNull();
    fireEvent.change(select, { target: { value: 'None' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save All Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(updateSettings))[0] as Record<string, unknown>;
    expect(body.landingMode).toBe('None');
    expect(body).not.toHaveProperty('autoCreatePr');
  });

  it('defaults to Merge and Push when the server sends no landing mode', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings } as never);
    render(<Settings />);
    const select = await screen.findByLabelText('Default Landing Mode') as HTMLSelectElement;
    expect(select.value).toBe('MergeAndPush');
  });
});
