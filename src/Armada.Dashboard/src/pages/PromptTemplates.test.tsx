import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PromptTemplates from './PromptTemplates';
import { listPromptTemplates } from '../api/client';
import type { PromptTemplate } from '../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};
const localeValue = { t: translate, formatDateTime: (v: string | null | undefined) => v ?? '', formatRelativeTime: (v: string | null | undefined) => v ?? '' };
const notifications = { pushToast: vi.fn() };

vi.mock('../api/client', () => ({
  listPromptTemplates: vi.fn(),
  createPromptTemplate: vi.fn(),
  resetPromptTemplate: vi.fn(),
}));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => notifications }));
vi.mock('../lib/useAutoRefresh', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/useAutoRefresh')>()),
  useAutoRefresh: () => ({ seconds: 30, setSeconds: vi.fn() }),
}));
const auth = { isAdmin: true, isTenantAdmin: true, user: null };
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));

const DESCRIPTION = 'Instructions given to every captain at the start of a mission, including the repository context and rules.';
const template = {
  id: 'ptm_1', name: 'mission.instructions', description: DESCRIPTION, category: 'mission', scope: 'Global', isBuiltIn: true,
  content: 'abc', active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as PromptTemplate;

describe('PromptTemplates table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listPromptTemplates).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [template] } as never);
  });

  it('shows the description on one line with the full text in the tooltip', async () => {
    render(<MemoryRouter><PromptTemplates /></MemoryRouter>);
    const text = await screen.findByText(DESCRIPTION);
    expect(text).toHaveClass('truncate-text');
    expect(text.closest('td')).toHaveAttribute('title', DESCRIPTION);
    expect(screen.getByText('3 chars').closest('td')).toHaveClass('cell-nowrap');
  });

  it('puts refresh controls in the toolbar and locks Name in the column chooser', async () => {
    const { container } = render(<MemoryRouter><PromptTemplates /></MemoryRouter>);
    await screen.findByText('mission.instructions');
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Refresh prompt template data')).toBeInTheDocument();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Name/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Description/ })).not.toHaveAttribute('aria-disabled');
  });
});
