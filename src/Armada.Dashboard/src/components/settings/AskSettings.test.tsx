import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import catalogSource from '../../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import componentSource from './AskSettings.tsx?raw';
import AskSettings, { ASK_DEFAULTS } from './AskSettings';
import { updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { I18nCatalog } from '../../i18n/runtime';
import type { AskSettingsData } from '../../types/models';

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

const SERVER: AskSettingsData = {
  historyTurns: 30,
  proposalExpiryMinutes: 90,
  trackerIntervalSeconds: 10,
  narrateMilestones: true,
  reportResultsOnCompletion: true,
  captainAutoApprove: true,
  narrationTimeoutSeconds: 45,
  turnTimeoutMinutes: 20,
};

describe('AskSettings', () => {
  beforeEach(() => {
    vi.mocked(updateSettings).mockReset();
  });

  it('shows the server values and saves the whole Ask group, keeping captainAutoApprove', async () => {
    const saved = { ask: { ...SERVER, reportResultsOnCompletion: false, historyTurns: 50 } };
    vi.mocked(updateSettings).mockResolvedValue(saved);
    const onSaved = vi.fn();
    const notify = vi.fn();
    render(<AskSettings ask={SERVER} locked={false} onSaved={onSaved} notify={notify} />);

    const report = screen.getByLabelText('Report results when work finishes') as HTMLInputElement;
    expect(report.checked).toBe(true);
    expect((screen.getByLabelText('Conversation history (messages)') as HTMLInputElement).value).toBe('30');
    expect(screen.queryByLabelText(/auto-approve/i)).toBeNull();
    const save = screen.getByRole('button', { name: 'Save Ask Armada Settings' });
    expect(save).toBeDisabled();

    fireEvent.click(report);
    fireEvent.change(screen.getByLabelText('Conversation history (messages)'), { target: { value: '50' } });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    expect(updateSettings).toHaveBeenCalledWith({
      ask: {
        historyTurns: 50,
        proposalExpiryMinutes: 90,
        trackerIntervalSeconds: 10,
        narrateMilestones: true,
        reportResultsOnCompletion: false,
        captainAutoApprove: true,
        narrationTimeoutSeconds: 45,
        turnTimeoutMinutes: 20,
      },
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));
    expect(notify).toHaveBeenCalledWith('success', 'Ask Armada settings saved and applied.');
  });

  it('blocks saving an out-of-range value and explains that field\'s range', () => {
    render(<AskSettings ask={SERVER} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Turn timeout (minutes)'), { target: { value: '121' } });
    expect(screen.getByRole('button', { name: 'Save Ask Armada Settings' })).toBeDisabled();
    expect(screen.getByText('Must be a whole number from 1 to 120.')).toBeInTheDocument();
    expect(screen.getByLabelText('Turn timeout (minutes)')).toHaveAttribute('aria-invalid', 'true');
  });

  it('falls back to the defaults when the server omits the group and still saves the full object', async () => {
    vi.mocked(updateSettings).mockResolvedValue({});
    render(<AskSettings ask={undefined} locked={false} onSaved={vi.fn()} notify={vi.fn()} />);
    expect((screen.getByLabelText('Proposal expiry (minutes)') as HTMLInputElement).value).toBe('60');
    expect((screen.getByLabelText('Narrate milestones') as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByLabelText('Narrate milestones'));
    fireEvent.click(screen.getByRole('button', { name: 'Save Ask Armada Settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ ask: { ...ASK_DEFAULTS, narrateMilestones: false } }));
  });

  it('disables editing when locked', () => {
    render(<AskSettings ask={SERVER} locked onSaved={vi.fn()} notify={vi.fn()} />);
    expect(screen.getByLabelText('Report results when work finishes')).toBeDisabled();
    expect(screen.getByLabelText('Work tracking interval (seconds)')).toBeDisabled();
  });
});

describe('ask settings i18n', () => {
  it('has a translation for every string in every non-English locale', () => {
    const catalog = JSON.parse(catalogSource) as I18nCatalog;
    const keys = [...componentSource.matchAll(/\bt\(\s*'((?:[^'\\]|\\.)*)'/g)].map((m) => m[1].replace(/\\'/g, "'"));
    expect(keys.length).toBeGreaterThan(15);
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
