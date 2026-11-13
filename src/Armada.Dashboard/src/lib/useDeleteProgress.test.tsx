import { describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ApiError } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { useDeleteProgress, type DeleteProgressOptions } from './useDeleteProgress';

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params) }),
}));

const MESSAGE = 'This may take some time while related records are cleaned up.';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

/** A page with one Delete button that runs the given options and records each outcome. */
function Harness({ options, outcomes }: { options: () => DeleteProgressOptions; outcomes: boolean[] }) {
  const deletion = useDeleteProgress();
  return (
    <div>
      <button type="button" onClick={() => { void deletion.run(options()).then((done) => outcomes.push(done)); }}>Delete</button>
      {deletion.dialog}
    </div>
  );
}

describe('useDeleteProgress', () => {
  it('opens a busy modal at once that Escape and the backdrop cannot close, then closes on success', async () => {
    const pending = deferred<void>();
    const action = vi.fn(() => pending.promise);
    const outcomes: boolean[] = [];
    render(<Harness options={() => ({ kind: 'vessel', action })} outcomes={outcomes} />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    const progress = screen.getByRole('alertdialog', { name: 'Deleting vessel' });
    expect(progress).toHaveTextContent(MESSAGE);
    expect(progress).toHaveAttribute('aria-busy', 'true');
    expect(within(progress).queryAllByRole('button')).toHaveLength(0);
    fireEvent.keyDown(document, { key: 'Escape' });
    fireEvent.click(progress.parentElement as HTMLElement);
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByRole('alertdialog', { name: 'Deleting vessel' })).toBeInTheDocument();
    expect(outcomes).toEqual([]);

    await act(async () => { pending.resolve(); await pending.promise; });
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(outcomes).toEqual([true]);
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('titles a bulk delete by count', () => {
    const outcomes: boolean[] = [];
    render(<Harness options={() => ({ kind: 'mission', count: 12, action: () => new Promise(() => undefined) })} outcomes={outcomes} />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(screen.getByRole('alertdialog', { name: 'Deleting 12 missions' })).toBeInTheDocument();
  });

  it('runs the delete once when the button is clicked twice', async () => {
    const pending = deferred<void>();
    const action = vi.fn(() => pending.promise);
    const outcomes: boolean[] = [];
    render(<Harness options={() => ({ kind: 'fleet', action })} outcomes={outcomes} />);
    const button = screen.getByRole('button', { name: 'Delete' });
    fireEvent.click(button);
    fireEvent.click(button);
    await act(async () => { await Promise.resolve(); });
    expect(action).toHaveBeenCalledTimes(1);
    expect(outcomes).toEqual([false]);
    await act(async () => { pending.resolve(); await pending.promise; });
    await waitFor(() => expect(outcomes).toEqual([false, true]));
    expect(action).toHaveBeenCalledTimes(1);
  });

  it('turns a failure into an error with Dismiss and Retry, and Retry runs the delete again', async () => {
    const action = vi.fn()
      .mockRejectedValueOnce(new ApiError('Database is locked', 500, null))
      .mockResolvedValueOnce(undefined);
    const outcomes: boolean[] = [];
    render(<Harness options={() => ({ kind: 'dock', action })} outcomes={outcomes} />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    const failure = await screen.findByRole('alertdialog', { name: 'Error' });
    expect(failure).toHaveTextContent('Database is locked');
    expect(failure).toHaveAttribute('aria-busy', 'false');
    expect(within(failure).getByRole('button', { name: 'Dismiss' })).toHaveFocus();
    expect(outcomes).toEqual([]);

    fireEvent.click(within(failure).getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(action).toHaveBeenCalledTimes(2);
    expect(outcomes).toEqual([true]);
  });

  it('Dismiss after a failure resolves false and allows another delete', async () => {
    const action = vi.fn()
      .mockRejectedValueOnce(new Error('network down'))
      .mockResolvedValueOnce(undefined);
    const outcomes: boolean[] = [];
    render(<Harness options={() => ({ kind: 'captain', action })} outcomes={outcomes} />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    // Non-API errors get the generic fallback.
    const failure = await screen.findByRole('alertdialog', { name: 'Error' });
    expect(failure).toHaveTextContent('Delete failed.');
    fireEvent.click(within(failure).getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => expect(outcomes).toEqual([false]));

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(outcomes).toEqual([false, true]));
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('uses a custom title, message, and error text when given', async () => {
    const outcomes: boolean[] = [];
    render(<Harness
      options={() => ({
        kind: 'conversation',
        title: 'Removing it',
        message: 'Hold on.',
        action: () => Promise.reject(new Error('x')),
        errorMessage: () => 'It could not be removed.',
      })}
      outcomes={outcomes}
    />);
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(screen.getByRole('alertdialog', { name: 'Removing it' })).toHaveTextContent('Hold on.');
    expect(await screen.findByText('It could not be removed.')).toBeInTheDocument();
  });
});
