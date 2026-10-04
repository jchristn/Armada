import { describe, it, expect, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import ActionMenu from './ActionMenu';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string) => text }),
}));

describe('ActionMenu keyboard access', () => {
  it('moves focus into the menu, supports arrow keys, and returns focus on Escape', async () => {
    const onEdit = vi.fn();
    render(<ActionMenu id="row-1" items={[{ label: 'Edit', onClick: onEdit }, { label: 'Disabled', onClick: () => {}, disabled: true }, { label: 'Delete', danger: true, onClick: () => {} }]} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });
    trigger.focus();
    await act(async () => { fireEvent.click(trigger); });
    expect(screen.getByRole('menu', { name: 'Actions' })).toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const edit = screen.getByRole('menuitem', { name: 'Edit' });
    const del = screen.getByRole('menuitem', { name: 'Delete' });
    expect(edit).toHaveFocus();
    fireEvent.keyDown(edit, { key: 'ArrowDown' });
    expect(del).toHaveFocus(); // the disabled item is skipped
    fireEvent.keyDown(del, { key: 'ArrowDown' });
    expect(edit).toHaveFocus();
    fireEvent.keyDown(edit, { key: 'End' });
    expect(del).toHaveFocus();
    await act(async () => { fireEvent.keyDown(del, { key: 'Escape' }); });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('runs the item and returns focus to the trigger', async () => {
    const onEdit = vi.fn();
    render(<ActionMenu id="row-2" items={[{ label: 'Edit', onClick: onEdit }]} />);
    const trigger = screen.getByRole('button', { name: 'Actions' });
    await act(async () => { fireEvent.click(trigger); });
    await act(async () => { fireEvent.click(screen.getByRole('menuitem', { name: 'Edit' })); });
    expect(onEdit).toHaveBeenCalledTimes(1);
    expect(trigger).toHaveFocus();
  });
});
