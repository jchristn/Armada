import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { useState } from 'react';
import DataTable, { type DataTableColumn } from './DataTable';
import { columnStorageKey } from '../../lib/useColumnVisibility';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string | number>) =>
      text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(params?.[k] ?? '')),
  }),
}));

interface Row {
  id: string;
  name: string;
  repo: string;
  created: string;
}

const ROWS: Row[] = [
  { id: 'vsl_1', name: 'gateway', repo: 'https://example.com/gateway.git', created: '2026-10-01' },
  { id: 'vsl_2', name: 'billing', repo: 'https://example.com/billing.git', created: '2026-10-02' },
];

const KEY = 'test-table';

function columns(extra: Partial<Record<string, Partial<DataTableColumn<Row>>>> = {}): DataTableColumn<Row>[] {
  return [
    { key: 'name', label: 'Name', required: true, render: (r) => r.name, ...extra.name },
    { key: 'id', label: 'ID', required: true, render: (r) => r.id, ...extra.id },
    { key: 'repo', label: 'Repository', render: (r) => r.repo, ...extra.repo },
    { key: 'created', label: 'Created', defaultHidden: true, render: (r) => r.created, ...extra.created },
    { key: 'actions', label: 'Actions', fixed: true, render: () => <button type="button">act</button>, ...extra.actions },
  ];
}

function headers(): string[] {
  return screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
}

async function openChooser() {
  const trigger = screen.getByRole('button', { name: /^Columns/ });
  trigger.focus();
  await act(async () => { fireEvent.click(trigger); });
  return { trigger, menu: screen.getByRole('menu', { name: 'Choose visible columns' }) };
}

describe('DataTable column chooser', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('shows the default columns, lists choosable ones, and locks required columns', async () => {
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Repository', 'Actions']);

    const { menu } = await openChooser();
    const items = within(menu).getAllByRole('menuitemcheckbox');
    // Fixed utility columns are not listed.
    expect(items.map((i) => i.textContent)).toEqual(['\u2611NameAlways shown', '\u2611IDAlways shown', '\u2611Repository', '\u2610Created']);
    const name = items[0];
    expect(name).toHaveAttribute('aria-disabled', 'true');
    expect(name).toHaveAttribute('aria-checked', 'true');
    fireEvent.click(name);
    expect(headers()).toContain('Name');
  });

  it('hides and shows a column and persists the choice across remounts', async () => {
    const view = render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    const { menu } = await openChooser();
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Repository/ }));
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Created/ }));
    expect(headers()).toEqual(['Name', 'ID', 'Created', 'Actions']);
    expect(screen.queryByText('https://example.com/gateway.git')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Columns \(1 hidden\)/ })).toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem(columnStorageKey(KEY)) ?? '{}')).toEqual({ hidden: ['repo'], version: 0 });

    view.unmount();
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Created', 'Actions']);
  });

  it('keeps choices separate per table key', async () => {
    localStorage.setItem(columnStorageKey('other'), JSON.stringify({ hidden: ['repo'], version: 0 }));
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toContain('Repository');
  });

  it('resets to the default columns', async () => {
    localStorage.setItem(columnStorageKey(KEY), JSON.stringify({ hidden: ['repo'], version: 0 }));
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Created', 'Actions']);
    const { menu } = await openChooser();
    const reset = within(menu).getByRole('menuitem', { name: 'Reset to default' });
    expect(reset).not.toHaveAttribute('aria-disabled');
    fireEvent.click(reset);
    expect(headers()).toEqual(['Name', 'ID', 'Repository', 'Actions']);
    expect(reset).toHaveAttribute('aria-disabled', 'true');
    expect(JSON.parse(localStorage.getItem(columnStorageKey(KEY)) ?? '{}')).toEqual({ hidden: ['created'], version: 0 });
  });

  it('ignores unknown and required keys in saved state', () => {
    localStorage.setItem(columnStorageKey(KEY), JSON.stringify({ hidden: ['name', 'id', 'gone', 'repo'], version: 0 }));
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Created', 'Actions']);
    expect(JSON.parse(localStorage.getItem(columnStorageKey(KEY)) ?? '{}')).toEqual({ hidden: ['repo'], version: 0 });
  });

  it('falls back to the defaults when saved state is corrupt', () => {
    localStorage.setItem(columnStorageKey(KEY), '{not json');
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Repository', 'Actions']);
  });

  it('works when storage is unavailable (reads and writes throw)', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('SecurityError'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError'); });
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Repository', 'Actions']);
    const { menu } = await openChooser();
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Repository/ }));
    expect(headers()).toEqual(['Name', 'ID', 'Actions']);
  });

  it('reads the hidden columns an older build stored in the table preferences entry', () => {
    localStorage.setItem(`armada_table_${KEY}`, JSON.stringify({ pageSize: 50, hiddenColumns: ['repo'], defaultsVersion: 0 }));
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'ID', 'Created', 'Actions']);
  });

  it('is keyboard operable: focus moves in, arrows move, Space toggles, Escape returns focus', async () => {
    render(<DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} />);
    const { trigger, menu } = await openChooser();
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu');
    const repo = within(menu).getByRole('menuitemcheckbox', { name: /Repository/ });
    expect(repo).toHaveFocus(); // first changeable column
    fireEvent.keyDown(repo, { key: 'ArrowDown' });
    const created = within(menu).getByRole('menuitemcheckbox', { name: /Created/ });
    expect(created).toHaveFocus();
    fireEvent.keyDown(created, { key: ' ' });
    expect(created).toHaveAttribute('aria-checked', 'true');
    expect(headers()).toContain('Created');
    fireEvent.keyDown(created, { key: 'Home' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /Name/ })).toHaveFocus();
    fireEvent.keyDown(document.activeElement as Element, { key: 'End' });
    expect(within(menu).getByRole('menuitem', { name: 'Reset to default' })).toHaveFocus();
    await act(async () => { fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' }); });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes on an outside click', async () => {
    render(<div><span>outside</span><DataTable tableKey={KEY} columns={columns()} rows={ROWS} rowKey={(r) => r.id} /></div>);
    await openChooser();
    await act(async () => { fireEvent.mouseDown(screen.getByText('outside')); });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('clears a column filter when that column is hidden', async () => {
    function Harness() {
      const [filter, setFilter] = useState('gate');
      const rows = ROWS.filter((r) => r.repo.includes(filter));
      return (
        <DataTable
          tableKey={KEY}
          rows={rows}
          rowKey={(r) => r.id}
          columns={columns({ repo: { filter: <input aria-label="repo filter" value={filter} onChange={(e) => setFilter(e.target.value)} />, clearFilter: () => setFilter('') } })}
        />
      );
    }
    render(<Harness />);
    expect(screen.queryByText('billing')).not.toBeInTheDocument();
    const { menu } = await openChooser();
    fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /Repository/ }));
    expect(screen.getByText('billing')).toBeInTheDocument();
    expect(screen.queryByLabelText('repo filter')).not.toBeInTheDocument();
  });

  it('does not offer a chooser when every column is required or fixed', () => {
    render(<DataTable tableKey={KEY} columns={[columns()[0], columns()[4]]} rows={ROWS} rowKey={(r) => r.id} />);
    expect(screen.queryByRole('button', { name: /^Columns/ })).not.toBeInTheDocument();
  });
});

describe('DataTable toolbar', () => {
  beforeEach(() => localStorage.clear());

  it('puts the record count, chooser, auto-refresh, refresh and pager in one bar', async () => {
    const onRefresh = vi.fn().mockResolvedValue(undefined);
    const onChange = vi.fn();
    const onPageChange = vi.fn();
    const { container } = render(
      <DataTable
        tableKey={KEY}
        columns={columns()}
        rows={ROWS}
        rowKey={(r) => r.id}
        autoRefresh={{ seconds: 15, onChange }}
        onRefresh={onRefresh}
        refreshTitle="Refresh vessels"
        pagination={{ pageNumber: 1, pageSize: 25, totalPages: 3, totalRecords: 60, onPageChange, onPageSizeChange: vi.fn() }}
      />,
    );
    const bars = container.querySelectorAll('.pagination-bar');
    expect(bars).toHaveLength(1);
    const bar = bars[0] as HTMLElement;
    expect(within(bar).getByText('60 records')).toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: /^Columns/ })).toBeInTheDocument();
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    fireEvent.change(within(bar).getByLabelText('Auto-refresh interval'), { target: { value: '60' } });
    expect(onChange).toHaveBeenCalledWith(60);
    await act(async () => { fireEvent.click(within(bar).getByTitle('Refresh vessels')); });
    expect(onRefresh).toHaveBeenCalledTimes(1);
    fireEvent.click(within(bar).getByRole('button', { name: 'Next' }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it('keeps the toolbar (and its refresh) when a placeholder replaces the table', () => {
    render(
      <DataTable
        tableKey={KEY}
        columns={columns()}
        rows={[]}
        rowKey={(r) => r.id}
        onRefresh={vi.fn().mockResolvedValue(undefined)}
        placeholder={<div>Nothing yet</div>}
      />,
    );
    expect(screen.getByText('Nothing yet')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByText('0 records')).toBeInTheDocument();
    expect(screen.getByTitle('Refresh')).toBeInTheDocument();
  });

  it('renders sortable headers as buttons with aria-sort', () => {
    const onSort = vi.fn();
    render(
      <DataTable
        tableKey={KEY}
        columns={columns({ name: { sortKey: 'name' }, repo: { sortKey: 'repo' } })}
        rows={ROWS}
        rowKey={(r) => r.id}
        sort={{ field: 'name', dir: 'desc', onSort }}
      />,
    );
    const nameHeader = screen.getAllByRole('columnheader')[0];
    expect(nameHeader).toHaveAttribute('aria-sort', 'descending');
    expect(screen.getAllByRole('columnheader')[2]).toHaveAttribute('aria-sort', 'none');
    fireEvent.click(within(nameHeader).getByRole('button'));
    expect(onSort).toHaveBeenCalledWith('name');
  });

  it('spans the empty message over the visible columns and the selection column', () => {
    render(
      <DataTable
        tableKey={KEY}
        columns={columns()}
        rows={[]}
        rowKey={(r) => r.id}
        emptyMessage="No rows"
        selection={{ isSelected: () => false, onToggle: vi.fn(), allSelected: false, onToggleAll: vi.fn(), selectAllLabel: 'Select all', rowLabel: () => 'Select' }}
      />,
    );
    expect(screen.getByText('No rows').closest('td')).toHaveAttribute('colspan', '5');
  });

  it('row clicks skip interactive cells and selection', () => {
    const onRowClick = vi.fn();
    const onToggle = vi.fn();
    render(
      <DataTable
        tableKey={KEY}
        columns={columns({ actions: { interactive: true } })}
        rows={ROWS}
        rowKey={(r) => r.id}
        onRowClick={onRowClick}
        selection={{ isSelected: () => false, onToggle, allSelected: false, onToggleAll: vi.fn(), selectAllLabel: 'Select all', rowLabel: (r) => `Select ${r.name}` }}
      />,
    );
    fireEvent.click(screen.getAllByText('act')[0]);
    fireEvent.click(screen.getByLabelText('Select gateway'));
    expect(onRowClick).not.toHaveBeenCalled();
    expect(onToggle).toHaveBeenCalledWith(ROWS[0]);
    fireEvent.click(screen.getByText('billing'));
    expect(onRowClick).toHaveBeenCalledWith(ROWS[1]);
  });

  it('only marks rows clickable where isRowClickable allows (regression: every row showed a pointer)', () => {
    const onRowClick = vi.fn();
    render(
      <DataTable
        tableKey={KEY}
        columns={columns()}
        rows={ROWS}
        rowKey={(r) => r.id}
        onRowClick={onRowClick}
        isRowClickable={(r) => r.id === 'vsl_2'}
      />,
    );
    const gateway = screen.getByText('gateway').closest('tr') as HTMLElement;
    const billing = screen.getByText('billing').closest('tr') as HTMLElement;
    expect(gateway).not.toHaveClass('clickable');
    expect(billing).toHaveClass('clickable');
    fireEvent.click(gateway);
    expect(onRowClick).not.toHaveBeenCalled();
    fireEvent.click(billing);
    expect(onRowClick).toHaveBeenCalledWith(ROWS[1]);
  });

  it('disables the checkbox of rows that cannot be selected', () => {
    render(
      <DataTable
        tableKey={KEY}
        columns={columns()}
        rows={ROWS}
        rowKey={(r) => r.id}
        selection={{ isSelected: () => false, isSelectable: (r) => r.id === 'vsl_1', onToggle: vi.fn(), allSelected: false, onToggleAll: vi.fn(), selectAllLabel: 'Select all', rowLabel: (r) => `Select ${r.name}` }}
      />,
    );
    expect(screen.getByLabelText('Select gateway')).not.toBeDisabled();
    expect(screen.getByLabelText('Select billing')).toBeDisabled();
  });

  it('gives one-line names and links (which truncate with an ellipsis) a title with the full value', () => {
    const long = 'a-very-long-vessel-name-that-will-be-truncated-by-the-one-line-name-rule';
    const cols = (name: string): DataTableColumn<Row>[] => [
      { key: 'name', label: 'Name', required: true, render: () => <strong>{name}</strong> },
      { key: 'repo', label: 'Repository', render: (r) => <a href="#">{r.repo}</a> },
      { key: 'id', label: 'ID', render: (r) => <strong title="custom">{r.id}</strong> },
    ];
    const view = render(<DataTable tableKey={KEY} columns={cols(long)} rows={[ROWS[0]]} rowKey={(r) => r.id} />);
    expect(screen.getByText(long)).toHaveAttribute('title', long);
    expect(screen.getByText(ROWS[0].repo)).toHaveAttribute('title', ROWS[0].repo);
    // A title the page set itself is kept.
    expect(screen.getByText('vsl_1')).toHaveAttribute('title', 'custom');
    // The tooltip follows the text when a refresh changes it.
    view.rerender(<DataTable tableKey={KEY} columns={cols('renamed')} rows={[ROWS[0]]} rowKey={(r) => r.id} />);
    expect(screen.getByText('renamed')).toHaveAttribute('title', 'renamed');
  });
});
