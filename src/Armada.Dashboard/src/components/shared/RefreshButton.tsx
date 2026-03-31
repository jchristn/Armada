import { useState, useCallback } from 'react';

interface RefreshButtonProps {
  onRefresh: () => Promise<void>;
  title?: string;
}

/**
 * Refresh button that spins while loading and briefly flashes a green checkmark on success.
 * Uses the existing .refresh-btn, .refreshing, and .refresh-success CSS classes.
 */
export default function RefreshButton({ onRefresh, title = 'Refresh' }: RefreshButtonProps) {
  const [state, setState] = useState<'idle' | 'refreshing' | 'success'>('idle');

  const handleClick = useCallback(async () => {
    if (state === 'refreshing') return;
    setState('refreshing');
    try {
      await onRefresh();
      setState('success');
      setTimeout(() => setState('idle'), 1200);
    } catch {
      setState('idle');
    }
  }, [onRefresh, state]);

  const className = [
    'btn btn-sm refresh-btn',
    state === 'refreshing' ? 'refreshing' : '',
    state === 'success' ? 'refresh-success' : '',
  ].filter(Boolean).join(' ');

  return (
    <button className={className} onClick={handleClick} title={title}>
      {state === 'success' ? '\u2713' : '\u21BB'}
    </button>
  );
}
