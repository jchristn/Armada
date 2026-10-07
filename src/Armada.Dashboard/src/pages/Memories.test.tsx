import { act, fireEvent, render, screen, within } from '@testing-library/react';
import Memories from './Memories';
import { listMemories } from '../api/client';
import type { Memory } from '../types/models';

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
  listMemories: vi.fn(),
  deleteMemory: vi.fn(),
}));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => notifications }));

const CONTENT = 'The billing service retries webhook deliveries three times with exponential backoff before parking them in the dead-letter queue.';
const VESSEL = 'vsl_0123456789abcdefghijkl';
const memory = {
  id: 'mem_1', scope: 'Tenant', type: 'Semantic', topic: 'billing', summary: null, content: CONTENT, salience: 0.5,
  version: 1, sourceKind: 'Agent', tags: [], vesselId: VESSEL, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
} as unknown as Memory;

describe('Memories table', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(listMemories).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 1, totalMs: 1, objects: [memory] } as never);
  });

  it('shows the summary on one line with the full content in the tooltip, and the vessel ID clipped', async () => {
    render(<Memories />);
    const text = await screen.findByText(CONTENT);
    expect(text).toHaveClass('truncate-text');
    expect(text.closest('td')).toHaveAttribute('title', CONTENT);
    expect(screen.getByTitle(VESSEL)).toHaveClass('cell-clip');
  });

  it('puts refresh in the toolbar and locks Summary in the column chooser', async () => {
    const { container } = render(<Memories />);
    await screen.findByText(CONTENT);
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByTitle('Refresh')).toBeInTheDocument();
    expect(container.querySelector('.page-header .refresh-btn')).toBeNull();
    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Summary/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Salience/ })).not.toHaveAttribute('aria-disabled');
  });
});
