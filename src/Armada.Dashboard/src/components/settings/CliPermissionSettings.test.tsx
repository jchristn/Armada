import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import CliPermissionSettings, { CLI_PERMISSION_DEFAULTS, validPromptTimeout } from './CliPermissionSettings';
import { updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';

vi.mock('../../api/client', () => ({ updateSettings: vi.fn() }));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

describe('validPromptTimeout', () => {
  it('accepts whole seconds from 10 to 3600 only', () => {
    for (const ok of ['10', '600', '3600']) expect(validPromptTimeout(ok)).toBe(true);
    for (const bad of ['9', '3601', '1.5', '', 'abc', '-20']) expect(validPromptTimeout(bad)).toBe(false);
  });
});

describe('CliPermissionSettings', () => {
  beforeEach(() => { vi.mocked(updateSettings).mockReset(); });

  it('shows the server values and saves the whole Permissions group', async () => {
    const saved = { permissions: { askDefaultPolicy: 'Refuse', missionDefaultPolicy: 'Bypass', allowOwnerApproval: true, promptTimeoutSeconds: 120 } };
    vi.mocked(updateSettings).mockResolvedValue(saved);
    const onSaved = vi.fn();
    const notify = vi.fn();
    render(<CliPermissionSettings permissions={CLI_PERMISSION_DEFAULTS} locked={false} onSaved={onSaved} notify={notify} />);

    const ask = screen.getByLabelText('Ask conversation default') as HTMLSelectElement;
    const mission = screen.getByLabelText('Mission default') as HTMLSelectElement;
    expect(ask.value).toBe('ApproveInArmada');
    expect(mission.value).toBe('Bypass');
    // Server defaults have no Inherit.
    expect(Array.from(ask.options).map((o) => o.value)).toEqual(['Refuse', 'ApproveInArmada', 'Bypass']);
    const save = screen.getByRole('button', { name: 'Save CLI Tool Permissions' });
    expect(save).toBeDisabled();

    fireEvent.change(ask, { target: { value: 'Refuse' } });
    fireEvent.click(screen.getByLabelText('Let owners approve their own requests'));
    fireEvent.change(screen.getByLabelText('Prompt timeout (seconds)'), { target: { value: '120' } });
    fireEvent.click(save);

    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({
      permissions: { askDefaultPolicy: 'Refuse', missionDefaultPolicy: 'Bypass', allowOwnerApproval: true, promptTimeoutSeconds: 120 },
    }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));
    expect(notify).toHaveBeenCalledWith('success', 'CLI tool permission settings saved.');
  });

  it('requires the strong warning before a default becomes Bypass', () => {
    render(<CliPermissionSettings permissions={CLI_PERMISSION_DEFAULTS} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    const ask = screen.getByLabelText('Ask conversation default') as HTMLSelectElement;
    fireEvent.change(ask, { target: { value: 'Bypass' } });
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveTextContent('Bypass lets the captain run any command on the Admiral host as the Armada service user without asking.');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(ask.value).toBe('ApproveInArmada');
    expect(screen.getByRole('button', { name: 'Save CLI Tool Permissions' })).toBeDisabled();

    fireEvent.change(ask, { target: { value: 'Bypass' } });
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Use Bypass' }));
    expect(ask.value).toBe('Bypass');
    expect(screen.getByRole('button', { name: 'Save CLI Tool Permissions' })).toBeEnabled();
  });

  it('blocks saving an out-of-range timeout and explains the range', () => {
    render(<CliPermissionSettings permissions={CLI_PERMISSION_DEFAULTS} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Prompt timeout (seconds)'), { target: { value: '5' } });
    expect(screen.getByRole('button', { name: 'Save CLI Tool Permissions' })).toBeDisabled();
    expect(screen.getByText(/Must be a whole number from 10 to 3,600/)).toBeInTheDocument();
  });

  it('falls back to the defaults when the server omits the group, and disables editing when locked', () => {
    render(<CliPermissionSettings permissions={undefined} locked onSaved={vi.fn()} notify={vi.fn()} />);
    expect((screen.getByLabelText('Prompt timeout (seconds)') as HTMLInputElement).value).toBe('600');
    expect(screen.getByLabelText('Mission default')).toBeDisabled();
  });
});
