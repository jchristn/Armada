import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import DataTable from './DataTable';
import LoadingValue from './LoadingValue';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, vars?: Record<string, unknown>) => text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(vars?.[k] ?? '')) }),
}));

const columns = [{ key: 'name', label: 'Name', render: (row: { id: string }) => row.id }];

describe('loading states', () => {
  it('shows a loading row and no record count while the first rows load, not the empty message or 0 records', () => {
    render(<DataTable tableKey="t" columns={columns} rows={[]} rowKey={(r) => r.id} loading emptyMessage="Nothing here" />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading...');
    expect(screen.queryByText('Nothing here')).toBeNull();
    expect(screen.queryByText('0 records')).toBeNull();
  });

  it('keeps the rows while a refresh loads, and shows the empty message once loaded', () => {
    const { rerender } = render(<DataTable tableKey="t" columns={columns} rows={[{ id: 'a' }]} rowKey={(r) => r.id} loading emptyMessage="Nothing here" />);
    expect(screen.getByText('a')).toBeInTheDocument();
    expect(screen.queryByRole('status')).toBeNull();
    rerender(<DataTable tableKey="t" columns={columns} rows={[]} rowKey={(r) => r.id} emptyMessage="Nothing here" />);
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
    expect(screen.getByText('0 records')).toBeInTheDocument();
  });

  it('shows a spinner instead of a value that has not arrived', () => {
    const { rerender } = render(<span data-testid="v"><LoadingValue loading>{0}</LoadingValue></span>);
    expect(screen.getByTestId('v')).not.toHaveTextContent('0');
    expect(screen.getByRole('status', { name: 'Loading...' })).toBeInTheDocument();
    rerender(<span data-testid="v"><LoadingValue loading={false}>{0}</LoadingValue></span>);
    expect(screen.getByTestId('v')).toHaveTextContent('0');
  });
});
