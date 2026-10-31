import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import BatteryPacks from './BatteryPacks';
import * as api from '../api/client';
import type { BatteryPackDetail } from '../types/models';
vi.mock('../api/client', () => ({ listBatteryPacks: vi.fn(), getBatteryPack: vi.fn(), updateBatteryPackManifest: vi.fn(), deleteBatteryPack: vi.fn(), validateBatteryPack: vi.fn(), createBatteryPack: vi.fn(), readBatteryPackFile: vi.fn(), writeBatteryPackFile: vi.fn(), deleteBatteryPackFile: vi.fn() }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true }) }));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string, params?: Record<string, string>) => text.replace(/\{\{(\w+)\}\}/g, (_, key: string) => params?.[key] ?? key) }) }));
const detail: BatteryPackDetail = { id: 'demo', manifestJson: '{}', info: { id: 'demo', name: 'Demo', version: 1, domain: 'C#', valid: false, errors: ['Missing tasks'], tasks: [], taskCount: 0 }, files: [{ path: 'fixture/a.txt', size: 3 }] };
beforeEach(() => {
  vi.clearAllMocks();
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
