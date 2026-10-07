/** Semantic tone of a status badge; each client maps it onto its own colors. */
export type BadgeTone = 'pending' | 'running' | 'success' | 'failed' | 'warning' | 'cancelled' | 'skipped' | 'info';

/** Glyph shown on a status badge. */
export type BadgeIcon = 'check' | 'x' | 'clock' | 'spinner' | 'skip' | 'stop' | 'alert' | 'info' | 'dot' | 'link' | 'lock';
