import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import BacklogGroupPills from './BacklogGroupPills';
import { BACKLOG_GROUPS } from './backlogUtils';

describe('BacklogGroupPills', () => {
  it('renders backlog group counts and emits the selected group', () => {
    const onChange = vi.fn();

    render(
      <BacklogGroupPills
        groups={BACKLOG_GROUPS}
        activeGroup="planning"
        counts={{ all: 9, inbox: 4, planning: 2, dispatch: 1, blocked: 2 }}
        onChange={onChange}
      />,
    );

    expect(screen.getByRole('group', { name: 'Backlog group views' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Ready For Planning/i })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('9')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Blocked/i }));
    expect(onChange).toHaveBeenCalledWith('blocked');
  });
});
