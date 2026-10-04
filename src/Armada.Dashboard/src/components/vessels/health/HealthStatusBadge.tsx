import { useLocale } from '../../../context/LocaleContext';
import type { VesselHealthStatus } from '../../../types/models';
import { STATUS_DESCRIPTIONS, statusLabel } from '../../../lib/health/healthText';

interface HealthStatusBadgeProps {
  status: VesselHealthStatus | null | undefined;
  /** When set, the value is a manual override: a `*` marker is shown and the note becomes the tooltip. */
  overrideNote?: string | null;
  overridden?: boolean;
  /** Extra tooltip text (for example the criterion name). */
  title?: string;
  compact?: boolean;
}

function StatusIcon({ status }: { status: VesselHealthStatus }) {
  const common = { width: 12, height: 12, viewBox: '0 0 16 16', 'aria-hidden': true, focusable: false } as const;
  switch (status) {
    case 'Pass':
      return (
        <svg {...common}><path d="M13 4.5 6.5 11 3 7.5" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" /></svg>
      );
    case 'Warn':
      return (
        <svg {...common}><path d="M8 1.8 15 14H1z" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" /><path d="M8 6v3.6M8 11.6v.4" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></svg>
      );
    case 'Fail':
      return (
        <svg {...common}><circle cx="8" cy="8" r="6.4" fill="none" stroke="currentColor" strokeWidth="1.6" /><path d="M5.6 5.6l4.8 4.8M10.4 5.6l-4.8 4.8" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></svg>
      );
    case 'NotApplicable':
      return (
        <svg {...common}><path d="M4 8h8" stroke="currentColor" strokeWidth="2" strokeLinecap="round" /></svg>
      );
    default:
      return (
        <svg {...common}><circle cx="8" cy="8" r="6.4" fill="none" stroke="currentColor" strokeWidth="1.6" /><path d="M6.2 6.2a1.9 1.9 0 1 1 2.6 1.8c-.5.2-.8.6-.8 1.1v.4M8 11.6v.4" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" /></svg>
      );
  }
}

/**
 * Status pill for vessel health: an icon plus localized text, never color alone. An overridden value
 * carries a `*` marker whose tooltip shows the override note.
 */
export default function HealthStatusBadge({ status, overrideNote, overridden, title, compact }: HealthStatusBadgeProps) {
  const { t } = useLocale();
  const value: VesselHealthStatus = status ?? 'Unknown';
  const label = statusLabel(t, value);
  const isOverridden = overridden || (overrideNote !== undefined && overrideNote !== null);
  const parts: string[] = [];
  if (title) parts.push(title);
  parts.push(`${label}: ${t(STATUS_DESCRIPTIONS[value] ?? STATUS_DESCRIPTIONS.Unknown)}`);
  if (isOverridden) {
    parts.push(overrideNote
      ? t('Manual override: {{note}}', { note: overrideNote })
      : t('Manual override (no note).'));
  }
  const tooltip = parts.join('\n');

  return (
    <span
      className={`tag vh-badge ${value.toLowerCase()}${compact ? ' vh-badge-compact' : ''}`}
      title={tooltip}
      data-status={value}
    >
      <StatusIcon status={value} />
      <span className="vh-badge-text">{label}</span>
      {isOverridden && <span className="vh-override-mark" aria-label={t('Overridden')}>*</span>}
    </span>
  );
}
