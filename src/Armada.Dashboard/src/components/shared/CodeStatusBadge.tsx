import type { ReactNode } from 'react';

import type { BadgeIcon, BadgeTone } from '../../lib/badgeTypes';

export type { BadgeIcon, BadgeTone };

/** Maps a semantic tone onto the existing `.tag.<status>` color classes so badges match the rest of the UI. */
const TONE_CLASS: Record<BadgeTone, string> = {
  pending: 'pending',
  running: 'working',
  success: 'completed',
  failed: 'failed',
  warning: 'review',
  cancelled: 'cancelled',
  skipped: 'idle',
  info: 'working',
};

function glyph(children: ReactNode): ReactNode {
  return (
    <svg className="status-icon-svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {children}
    </svg>
  );
}

const ICONS: Record<BadgeIcon, ReactNode> = {
  check: glyph(<path d="M20 6 9 17l-5-5" />),
  x: glyph(<><path d="M18 6 6 18" /><path d="m6 6 12 12" /></>),
  clock: glyph(<><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>),
  spinner: glyph(<path d="M21 12a9 9 0 1 1-6.2-8.56" />),
  skip: glyph(<><path d="m5 4 10 8-10 8V4Z" /><path d="M19 5v14" /></>),
  stop: glyph(<rect x="6" y="6" width="12" height="12" rx="1" />),
  alert: glyph(<><path d="M12 9v4" /><path d="M12 17h.01" /><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" /></>),
  info: glyph(<><circle cx="12" cy="12" r="9" /><path d="M12 16v-4" /><path d="M12 8h.01" /></>),
  dot: glyph(<circle cx="12" cy="12" r="4" fill="currentColor" />),
  link: glyph(<><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" /><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" /></>),
  lock: glyph(<><rect x="4" y="11" width="16" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></>),
};

interface CodeStatusBadgeProps {
  /** Already-localized label. */
  label: string;
  tone: BadgeTone;
  icon: BadgeIcon;
  /** Already-localized tooltip explaining the status. */
  title?: string;
  className?: string;
}

/**
 * Status badge that always pairs an icon with a text label, so state is never conveyed by color alone.
 * Callers pass localized strings (see `lib/fleetActionLabels.ts` and `lib/vesselImportLabels.ts`).
 */
export default function CodeStatusBadge({ label, tone, icon, title, className }: CodeStatusBadgeProps) {
  return (
    <span className={`tag status-icon-badge ${TONE_CLASS[tone]}${icon === 'spinner' ? ' status-icon-spin' : ''}${className ? ` ${className}` : ''}`} title={title}>
      {ICONS[icon]}
      <span>{label}</span>
    </span>
  );
}
