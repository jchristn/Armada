import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import BlackBoxSettings from './BlackBoxSettings';
import { getBlackBoxStorage, getBlackBoxStorageMigration, migrateBlackBoxStorage, updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { BlackBoxSettingsData } from '../../types/blackbox';

vi.mock('../../api/client', () => ({
  getBlackBoxStorage: vi.fn(),
  getBlackBoxStorageMigration: vi.fn(),
  migrateBlackBoxStorage: vi.fn(),
  updateSettings: vi.fn(),
}));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

function settings(): BlackBoxSettingsData {
  return {
    enabled: true,
    captureTimeoutMs: 5000,
    storage: {
      target: 'S3', keyPrefix: 'armada/blackbox', compress: true, fallbackToDatabase: true, maxEvidenceObjectBytes: 1048576, maxEntryBytes: 8388608,
      disk: { directory: null },
      s3: { endpoint: 'http://minio:9000', region: 'us-east-1', bucket: 'bb', accessKey: '********', secretKey: '********', usePathStyle: true, useSsl: false },
      azureBlob: { accountName: null, container: null, accessKey: null, connectionString: null },
      cifs: { hostname: 'nas', share: 'bb', domain: null, username: 'armada', password: '********', port: 445 },
    },
    retention: { retentionDays: 180, resolvedRetentionDays: 30, keepLatestPerMission: true, maxEntriesPerVessel: 1000, maxTotalBytes: 1073741824, deleteWithMission: false, deleteWithVessel: true, gcIntervalMinutes: 60, gcBatchSize: 200, orphanGraceHours: 24 },
    sweeper: { intervalSeconds: 60 },
  };
}

const notify = vi.fn();
const onSaved = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getBlackBoxStorage).mockResolvedValue({ target: 'S3', fallbackToDatabase: true, health: { target: 'S3', healthy: true, latencyMs: 12, checkedUtc: 'now' }, usage: [{ target: 'S3', entries: 3, bytes: 2048 }] });
  vi.mocked(updateSettings).mockImplementation(async (body) => body);
});

describe('BlackBoxSettings', () => {
  it('masks stored secrets and keeps them when left untouched', async () => {
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    const secret = screen.getByLabelText('Secret key') as HTMLInputElement;
    expect(secret.type).toBe('password');
    expect(secret.value).toBe('');
    expect(secret.placeholder).toBe('Stored (leave blank to keep)');
    expect(screen.queryByDisplayValue('********')).toBeNull();
    fireEvent.change(screen.getByLabelText('Bucket'), { target: { value: 'bb2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Black Box Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalled());
    const sent = vi.mocked(updateSettings).mock.calls[0][0].blackBox as BlackBoxSettingsData;
    expect(sent.storage.s3).toMatchObject({ bucket: 'bb2', accessKey: '********', secretKey: '********' });
    // Groups the editor does not show are sent back unchanged (the PUT replaces the whole group).
    expect(sent.sweeper).toEqual({ intervalSeconds: 60 });
    expect(notify).toHaveBeenCalledWith('success', 'Black box settings saved.');
  });

  it('sends a newly typed secret, and restores the marker when the field is cleared again', async () => {
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    const secret = screen.getByLabelText('Secret key');
    fireEvent.change(secret, { target: { value: 'new-secret' } });
    fireEvent.change(screen.getByLabelText('Access key'), { target: { value: 'tmp' } });
    fireEvent.change(screen.getByLabelText('Access key'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save Black Box Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalled());
    const sent = vi.mocked(updateSettings).mock.calls[0][0].blackBox as BlackBoxSettingsData;
    expect(sent.storage.s3).toMatchObject({ secretKey: 'new-secret', accessKey: '********' });
  });

  it('shows the fields of the chosen target, including fields the server added', async () => {
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    fireEvent.change(screen.getByLabelText('Storage target'), { target: { value: 'Cifs' } });
    expect(screen.getByLabelText('Hostname')).toHaveValue('nas');
    expect(screen.getByLabelText('Port')).toHaveValue(445);
    expect((screen.getByLabelText('Password') as HTMLInputElement).type).toBe('password');
    expect(screen.queryByLabelText('Bucket')).toBeNull();
    fireEvent.change(screen.getByLabelText('Storage target'), { target: { value: 'Database' } });
    expect(screen.queryByLabelText('Hostname')).toBeNull();
  });

  it('shows the store health', async () => {
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    expect(await screen.findByText(/Store S3 is healthy \(12 ms/)).toBeInTheDocument();
  });

  it('refuses out-of-range retention values', async () => {
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    fireEvent.change(screen.getByLabelText('GC interval (minutes)'), { target: { value: '2' } });
    expect(screen.getByLabelText('GC interval (minutes)')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Save Black Box Settings' })).toBeDisabled();
  });

  it('starts a migration and follows its progress', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.mocked(migrateBlackBoxStorage).mockResolvedValue({ id: 'bbm_1', state: 'Running', to: 'Disk', deleteSource: false, totalEntries: 4, migratedEntries: 0, failedEntries: 0, bytesCopied: 0, createdUtc: '' });
      vi.mocked(getBlackBoxStorageMigration)
        .mockResolvedValueOnce({ id: 'bbm_1', state: 'Running', to: 'Disk', deleteSource: false, totalEntries: 4, migratedEntries: 2, failedEntries: 0, bytesCopied: 1024, createdUtc: '' })
        .mockResolvedValue({ id: 'bbm_1', state: 'Completed', to: 'Disk', deleteSource: false, totalEntries: 4, migratedEntries: 4, failedEntries: 0, bytesCopied: 2048, createdUtc: '' });
      render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
      await screen.findByText(/is healthy/);
      fireEvent.change(screen.getByLabelText('Move entries to'), { target: { value: 'Disk' } });
      fireEvent.click(screen.getByRole('button', { name: 'Migrate' }));
      const dialog = await screen.findByRole('alertdialog');
      fireEvent.click(within(dialog).getByRole('button', { name: 'Migrate' }));
      await waitFor(() => expect(migrateBlackBoxStorage).toHaveBeenCalledWith({ to: 'Disk', from: 'S3', deleteSource: false }));
      await waitFor(() => expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '50'));
      await act(async () => { vi.advanceTimersByTime(2100); });
      await waitFor(() => expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100'));
      expect(screen.getByTestId('blackbox-migration')).toHaveTextContent('Completed');
    } finally {
      vi.useRealTimers();
    }
  });

  it('explains a server that cannot migrate yet', async () => {
    vi.mocked(migrateBlackBoxStorage).mockRejectedValue(Object.assign(new Error('NotImplemented'), { status: 501, data: null }));
    render(<BlackBoxSettings blackBox={settings()} locked={false} onSaved={onSaved} notify={notify} />);
    await screen.findByText(/is healthy/);
    fireEvent.change(screen.getByLabelText('Move entries to'), { target: { value: 'Disk' } });
    fireEvent.click(screen.getByRole('button', { name: 'Migrate' }));
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Migrate' }));
    expect(await screen.findByText('This server cannot migrate black box storage yet.')).toBeInTheDocument();
  });

  it('locks every field in remote proxy mode', () => {
    render(<BlackBoxSettings blackBox={settings()} locked onSaved={onSaved} notify={notify} />);
    expect(screen.getByLabelText('Storage target')).toBeDisabled();
    expect(screen.getByLabelText('Move entries to')).toBeDisabled();
  });
});
