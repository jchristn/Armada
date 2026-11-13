import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import BatteryPacks from './BatteryPacks';
import * as api from '../api/client';
import type { BatteryPackDetail } from '../types/models';
vi.mock('../api/client', () => ({ listBatteryPacks: vi.fn(), getBatteryPack: vi.fn(), updateBatteryPackManifest: vi.fn(), deleteBatteryPack: vi.fn(), validateBatteryPack: vi.fn(), createBatteryPack: vi.fn(), readBatteryPackFile: vi.fn(), writeBatteryPackFile: vi.fn(), deleteBatteryPackFile: vi.fn() }));
const auth = vi.hoisted(() => ({ isAdmin: true }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string, params?: Record<string, string>) => text.replace(/\{\{(\w+)\}\}/g, (_, key: string) => params?.[key] ?? key) }) }));
const detail: BatteryPackDetail = { id: 'demo', manifestJson: '{}', info: { id: 'demo', name: 'Demo', version: 1, domain: 'C#', valid: false, errors: ['Missing tasks'], tasks: [], taskCount: 0 }, files: [{ path: 'fixture/a.txt', size: 3 }] };
beforeEach(() => {
  vi.clearAllMocks();
  auth.isAdmin = true;
  vi.mocked(api.listBatteryPacks).mockResolvedValue({ objects: [detail.info], totalPages: 1 } as Awaited<ReturnType<typeof api.listBatteryPacks>>);
  vi.mocked(api.getBatteryPack).mockResolvedValue(detail);
  vi.mocked(api.updateBatteryPackManifest).mockResolvedValue({ ...detail, manifestJson: '{"id":"demo"}' });
  vi.mocked(api.deleteBatteryPack).mockResolvedValue({ deleted: true });
});
async function action(name: string) {
  fireEvent.click(await screen.findByRole('button', { name: 'Actions' }));
  fireEvent.click(screen.getByRole('menuitem', { name }));
}
it('lists pack validity, language and task count', async () => {
  render(<BatteryPacks />);
  expect(await screen.findByText('Demo')).toBeInTheDocument();
  expect(screen.getByText('Invalid')).toHaveAttribute('title', 'Missing tasks');
  expect(screen.getByText('C#')).toBeInTheDocument();
});
it('loads and saves the manifest and displays validation errors', async () => {
  render(<BatteryPacks />); await action('View/Edit');
  const editor = await screen.findByRole('textbox', { name: 'Manifest JSON' });
  fireEvent.change(editor, { target: { value: '{"id":"demo"}' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save manifest' }));
  await waitFor(() => expect(api.updateBatteryPackManifest).toHaveBeenCalledWith('demo', '{"id":"demo"}'));
  expect(await screen.findByText('Missing tasks')).toBeInTheDocument();
});
it('requires confirmation before deleting a pack', async () => {
  render(<BatteryPacks />); await action('Delete');
  expect(api.deleteBatteryPack).not.toHaveBeenCalled();
  fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }));
  await waitFor(() => expect(api.deleteBatteryPack).toHaveBeenCalledWith('demo'));
});
it('shows API errors without discarding the manifest draft', async () => {
  vi.mocked(api.updateBatteryPackManifest).mockRejectedValue(new Error('Manifest must be valid JSON.'));
  render(<BatteryPacks />); await action('View/Edit');
  fireEvent.change(await screen.findByRole('textbox', { name: 'Manifest JSON' }), { target: { value: '{' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save manifest' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Manifest must be valid JSON.');
  expect(screen.getByRole('textbox', { name: 'Manifest JSON' })).toHaveValue('{');
});
it('renders through the shared PageHeader and DataTable with server paging', async () => {
  vi.mocked(api.listBatteryPacks).mockResolvedValue({ objects: [detail.info], totalPages: 3, totalRecords: 60, pageNumber: 1, pageSize: 25 } as Awaited<ReturnType<typeof api.listBatteryPacks>>);
  const { container } = render(<BatteryPacks />);
  expect(await screen.findByText('Demo')).toBeInTheDocument();
  expect(container.querySelector('.page-header h2')).toHaveTextContent('Battery packs');
  expect(container.querySelector('.table-wrap table')).not.toBeNull();
  expect(container.querySelector('.pagination-bar')).not.toBeNull();
  expect(screen.getByText('Invalid')).toHaveClass('tag', 'invalid');
  expect(api.listBatteryPacks).toHaveBeenCalledWith(25, 1);
  expect(container.querySelector('[style]')).toBeNull();
});
it('shows the shared empty placeholder when no packs are installed', async () => {
  vi.mocked(api.listBatteryPacks).mockResolvedValue({ objects: [], totalPages: 0, totalRecords: 0 } as unknown as Awaited<ReturnType<typeof api.listBatteryPacks>>);
  render(<BatteryPacks />);
  expect(await screen.findByText('No battery packs are installed.')).toBeInTheDocument();
});
it('shows a load failure with a retry that re-runs the same query', async () => {
  vi.mocked(api.listBatteryPacks).mockRejectedValueOnce(new Error('Server unavailable'));
  render(<BatteryPacks />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Server unavailable');
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(await screen.findByText('Demo')).toBeInTheDocument();
  expect(api.listBatteryPacks).toHaveBeenLastCalledWith(25, 1);
});
it('opens the editor in a dialog from a row click and reads a file from the files table', async () => {
  vi.mocked(api.readBatteryPackFile).mockResolvedValue({ path: 'fixture/a.txt', content: 'abc' });
  render(<BatteryPacks />);
  fireEvent.click(await screen.findByText('Demo'));
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByRole('textbox', { name: 'Manifest JSON' })).toHaveClass('mono');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Open' }));
  await waitFor(() => expect(api.readBatteryPackFile).toHaveBeenCalledWith('demo', 'fixture/a.txt'));
  expect(await within(dialog).findByRole('textbox', { name: 'File content' })).toHaveValue('abc');
});
it('marks valid and invalid tags with the shared complete and failed colors', async () => {
  const { readFileSync } = await import('node:fs');
  const css = readFileSync('./src/App.css', 'utf8');
  expect(css).toMatch(/\.tag\.passed[^{]*\.tag\.valid\s*\{\s*background: var\(--tag-complete-bg\)/);
  expect(css).toMatch(/\.tag\.landingfailed, \.tag\.invalid\s*\{\s*background: var\(--tag-failed-bg\)/);
});
it('keeps packs read-only for non-admins: View only, no create, delete, save or new file', async () => {
  auth.isAdmin = false;
  render(<BatteryPacks />);
  expect(await screen.findByText('Demo')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '+ New pack' })).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Actions' }));
  expect(screen.queryByRole('menuitem', { name: 'Delete' })).toBeNull();
  expect(screen.queryByRole('menuitem', { name: 'View/Edit' })).toBeNull();
  fireEvent.click(screen.getByRole('menuitem', { name: 'View' }));
  const dialog = await screen.findByRole('dialog');
  expect(within(dialog).getByRole('textbox', { name: 'Manifest JSON' })).toHaveAttribute('readonly');
  expect(within(dialog).queryByRole('button', { name: 'Save manifest' })).toBeNull();
  expect(within(dialog).queryByRole('button', { name: '+ New file' })).toBeNull();
});
