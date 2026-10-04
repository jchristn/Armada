import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import DialogShell from '../components/shared/DialogShell';
import { installDialogEnhancer, openDialogCount } from './dialogA11y';

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, vars?: Record<string, string>) => (vars ? text.replace(/\{\{(\w+)\}\}/g, (_m, k) => vars[k] ?? '') : text) }),
}));

let uninstall: () => void = () => {};
beforeAll(() => { uninstall = installDialogEnhancer(document.body); });
afterAll(() => uninstall());

// user-event performs the browser's default Tab movement unless the trap prevents it.
async function tab(shift = false) {
  await userEvent.tab({ shift });
}

function ConfirmHarness({ onConfirm = () => {} }: { onConfirm?: () => void }) {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>Delete fleet</button>
      <ConfirmDialog open={open} title="Delete Fleet" message="This cannot be undone." danger confirmLabel="Delete" onConfirm={() => { onConfirm(); setOpen(false); }} onCancel={() => setOpen(false)} />
    </div>
  );
}

function LegacyHarness() {
  const [open, setOpen] = useState(false);
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>Create fleet</button>
      {open && (
        <div className="modal-overlay" onClick={() => setOpen(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>Create Fleet</h3>
            <label>Name<input /></label>
            <button type="button">Save</button>
            <button type="button" onClick={() => setOpen(false)}>Cancel</button>
          </form>
        </div>
      )}
    </div>
  );
}

describe('ConfirmDialog accessibility', () => {
  it('is a labelled modal alert dialog with the message as its description', () => {
    render(<ConfirmHarness />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete fleet' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Delete Fleet' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription('This cannot be undone.');
  });

  it('moves focus into the dialog, traps Tab, closes on Escape and returns focus', async () => {
    render(<ConfirmHarness />);
    const opener = screen.getByRole('button', { name: 'Delete fleet' });
    opener.focus();
    await act(async () => { fireEvent.click(opener); });
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const confirm = screen.getByRole('button', { name: 'Delete' });
    expect(cancel).toHaveFocus();
    await tab();
    expect(confirm).toHaveFocus();
    await tab();
    expect(cancel).toHaveFocus();
    await tab(true);
    expect(confirm).toHaveFocus();
    await act(async () => { fireEvent.keyDown(confirm, { key: 'Escape' }); });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
    expect(openDialogCount()).toBe(0);
  });

  it('focuses the confirmation box when typing delete is required', () => {
    render(<ConfirmDialog open title="Delete" message="Really?" requireDeleteConfirm onConfirm={() => {}} onCancel={() => {}} />);
    expect(screen.getByRole('textbox', { name: /Type `delete`/ })).toHaveFocus();
  });
});

describe('DialogShell accessibility', () => {
  it('traps focus and does not close on Escape while not dismissible', async () => {
    const onClose = vi.fn();
    render(
      <DialogShell open title="Run action" onClose={onClose} dismissible={false} footer={<button type="button">Run</button>}>
        <input aria-label="Target" />
      </DialogShell>,
    );
    const dialog = screen.getByRole('dialog', { name: 'Run action' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(screen.getByRole('textbox', { name: 'Target' })).toHaveFocus();
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    await tab();
    expect(screen.getByRole('button', { name: 'Run' })).toHaveFocus();
    await tab();
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();
  });

  it('gives Escape to the top dialog only when a confirm is stacked on a drawer', () => {
    const closeDrawer = vi.fn();
    const cancelConfirm = vi.fn();
    render(
      <>
        <DialogShell open title="Target" onClose={closeDrawer} variant="drawer"><button type="button">Inside</button></DialogShell>
        <ConfirmDialog open title="Cancel run" message="Stop it?" onConfirm={() => {}} onCancel={cancelConfirm} />
      </>,
    );
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    expect(cancelConfirm).toHaveBeenCalledTimes(1);
    expect(closeDrawer).not.toHaveBeenCalled();
  });
});

describe('legacy modal markup enhancer', () => {
  it('adds dialog semantics, a focus trap, Escape-to-close and focus return', async () => {
    render(<LegacyHarness />);
    const opener = screen.getByRole('button', { name: 'Create fleet' });
    opener.focus();
    await act(async () => { fireEvent.click(opener); });
    const dialog = await screen.findByRole('dialog', { name: 'Create Fleet' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus());
    await tab(true);
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    await tab();
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus();
    await act(async () => { fireEvent.keyDown(document.activeElement!, { key: 'Escape' }); });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(opener).toHaveFocus());
  });
});
