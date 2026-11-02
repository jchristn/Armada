import { useState, useCallback } from 'react';
import { useLocale } from '../../context/LocaleContext';

interface RefreshButtonProps {
  onRefresh: () => Promise<void>;
  title?: string;
}

/**
 * Refresh button that spins while loading and briefly flashes a green checkmark on success.
 * Uses the existing .refresh-btn, .refreshing, and .refresh-success CSS classes.
 */
export default function RefreshButton({ onRefresh, title = 'Refresh' }: RefreshButtonProps) {
  const { t } = useLocale();
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
    <button type="button" className={className} onClick={handleClick} title={t(title)} aria-label={t(title)}>
      <span className="refresh-btn-icon" aria-hidden="true">
        {state === 'success' ? '\u2713' : '\u21BB'}
      </span>
    </button>
  );
}
