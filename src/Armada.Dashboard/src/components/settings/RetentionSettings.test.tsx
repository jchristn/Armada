import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import catalogSource from '../../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import componentSource from './RetentionSettings.tsx?raw';
import RetentionSettings, { RETENTION_DEFAULTS, validateRetentionDraft } from './RetentionSettings';
import { updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { I18nCatalog } from '../../i18n/runtime';

vi.mock('../../api/client', () => ({
  updateSettings: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => localeValue,
}));

const draft = (overrides: Partial<Record<keyof typeof RETENTION_DEFAULTS, string>> = {}) => ({
  askThreadArchiveAfterDays: '90',
  askThreadDeleteAfterDays: '0',
  jobRetentionDays: '30',
  importBatchRetentionDays: '90',
  cliPermissionRequestRetentionDays: '90',
  ...overrides,
});

describe('validateRetentionDraft', () => {
  it('accepts whole days from 0 (never) to 3650', () => {
    expect(validateRetentionDraft(draft())).toEqual({});
    expect(validateRetentionDraft(draft({ askThreadArchiveAfterDays: '0', jobRetentionDays: '3650' }))).toEqual({});
  });

  it('rejects negative, too large, fractional, and empty values', () => {
    for (const bad of ['-1', '3651', '1.5', '', 'abc']) {
      expect(validateRetentionDraft(draft({ importBatchRetentionDays: bad })).importBatchRetentionDays).toBeTruthy();
    }
  });
});

describe('RetentionSettings', () => {
  beforeEach(() => {
    vi.mocked(updateSettings).mockReset();
  });

  it('shows the server values and saves the whole Retention group', async () => {
    const saved = { retention: { askThreadArchiveAfterDays: 30, askThreadDeleteAfterDays: 365, jobRetentionDays: 30, importBatchRetentionDays: 90, cliPermissionRequestRetentionDays: 14 } };
    vi.mocked(updateSettings).mockResolvedValue(saved);
    const onSaved = vi.fn();
    const notify = vi.fn();
    render(<RetentionSettings retention={RETENTION_DEFAULTS} locked={false} onSaved={onSaved} notify={notify} />);

    const archive = screen.getByLabelText('Archive Ask threads after (days)') as HTMLInputElement;
    expect(archive.value).toBe('90');
    const save = screen.getByRole('button', { name: 'Save Retention Settings' });
    expect(save).toBeDisabled();

    fireEvent.change(archive, { target: { value: '30' } });
    fireEvent.change(screen.getByLabelText('Delete Ask threads after (days)'), { target: { value: '365' } });
    fireEvent.change(screen.getByLabelText('CLI permission request retention (days)'), { target: { value: '14' } });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect(updateSettings).toHaveBeenCalledWith({
      retention: { askThreadArchiveAfterDays: 30, askThreadDeleteAfterDays: 365, jobRetentionDays: 30, importBatchRetentionDays: 90, cliPermissionRequestRetentionDays: 14 },
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));
    expect(notify).toHaveBeenCalledWith('success', 'Retention settings saved and applied.');
  });

  it('blocks saving an out-of-range value and explains the range', () => {
    render(<RetentionSettings retention={RETENTION_DEFAULTS} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Job retention (days)'), { target: { value: '5000' } });
    expect(screen.getByRole('button', { name: 'Save Retention Settings' })).toBeDisabled();
    expect(screen.getByText(/Must be a whole number from 0 to 3,650/)).toBeInTheDocument();
  });

  it('falls back to the defaults when the server omits the group', () => {
    render(<RetentionSettings retention={undefined} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    expect((screen.getByLabelText('Import history retention (days)') as HTMLInputElement).value).toBe('90');
  });

  it('disables editing when locked', () => {
    render(<RetentionSettings retention={RETENTION_DEFAULTS} locked onSaved={vi.fn()} notify={vi.fn()} />);
    expect(screen.getByLabelText('Archive Ask threads after (days)')).toBeDisabled();
  });
});

describe('retention settings i18n', () => {
  it('has a translation for every string in every non-English locale', () => {
    const catalog = JSON.parse(catalogSource) as I18nCatalog;
    const keys = [...componentSource.matchAll(/\bt\(\s*'((?:[^'\\]|\\.)*)'/g)].map((m) => m[1].replace(/\\'/g, "'"));
    expect(keys.length).toBeGreaterThan(10);
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });
});
