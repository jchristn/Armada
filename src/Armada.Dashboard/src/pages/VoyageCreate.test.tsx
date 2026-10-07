import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import VoyageCreate from './VoyageCreate';
import { createVoyage, listPipelines, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { onlyCallArgs } from '../test/mockCalls';

vi.mock('../api/client', () => ({
  listVessels: vi.fn(),
  listPipelines: vi.fn(),
  createVoyage: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../components/shared/PlaybookSelector', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function selectFor(label: string): HTMLSelectElement {
  const select = screen.getByText(label, { selector: 'span' }).parentElement?.querySelector('select');
  if (!select) throw new Error(`no select for ${label}`);
  return select;
}

async function fillForm() {
  render(<MemoryRouter><VoyageCreate /></MemoryRouter>);
  await screen.findByRole('option', { name: 'gateway (vsl_1)' });
  fireEvent.change(screen.getByPlaceholderText('Name for this batch of missions'), { target: { value: 'Docs sweep' } });
  fireEvent.change(selectFor('Vessel'), { target: { value: 'vsl_1' } });
  fireEvent.change(screen.getByPlaceholderText('What needs to be done?'), { target: { value: 'Update README' } });
}

describe('VoyageCreate landing mode', () => {
  beforeEach(() => {
    vi.mocked(listVessels).mockResolvedValue(page([{ id: 'vsl_1', name: 'gateway' }]) as never);
    vi.mocked(listPipelines).mockResolvedValue(page([]) as never);
    vi.mocked(createVoyage).mockReset();
    vi.mocked(createVoyage).mockResolvedValue({ id: 'vyg_1' } as never);
  });

  it('has a Landing Mode select instead of the auto-push and pull request checkboxes', async () => {
    await fillForm();
    expect(screen.queryByText('Auto-Push')).toBeNull();
    expect(screen.queryByText('Auto-Create PRs')).toBeNull();
    expect(screen.queryByText('Auto-Merge PRs')).toBeNull();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    const landing = selectFor('Landing Mode');
    expect(Array.from(landing.options).map((o) => o.value)).toEqual(['', 'LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue', 'None']);
    expect(within(landing).getByRole('option', { name: 'Default (use vessel or global setting)' })).toHaveValue('');
    expect(landing.value).toBe('');
  });

  it('sends no landingMode when inheriting', async () => {
    await fillForm();
    fireEvent.click(screen.getByRole('button', { name: 'Create Voyage' }));
    await waitFor(() => expect(createVoyage).toHaveBeenCalledTimes(1));
    expect(onlyCallArgs(vi.mocked(createVoyage))[0]).not.toHaveProperty('landingMode');
  });

  it('sends the chosen landingMode', async () => {
    await fillForm();
    fireEvent.change(selectFor('Landing Mode'), { target: { value: 'MergeAndPush' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create Voyage' }));
    await waitFor(() => expect(createVoyage).toHaveBeenCalledTimes(1));
    expect(onlyCallArgs(vi.mocked(createVoyage))[0].landingMode).toBe('MergeAndPush');
  });
});
