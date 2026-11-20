/*
 * How Armada entities look in VS Code: codicons and theme colors per status, short descriptions. Pure, for tests.
 */

/** A codicon and an optional theme color id. */
export interface StatusLook {
  icon: string;
  color?: string;
}

/** Mission status look. */
export function missionLook(status: string): StatusLook {
  switch (status) {
    case 'Pending': return { icon: 'circle-large-outline', color: 'descriptionForeground' };
    case 'Blocked': return { icon: 'debug-pause', color: 'list.warningForeground' };
    case 'Assigned': return { icon: 'person', color: 'charts.blue' };
    case 'InProgress': return { icon: 'sync~spin', color: 'charts.blue' };
    case 'WorkProduced': return { icon: 'git-commit', color: 'charts.blue' };
    case 'PullRequestOpen': return { icon: 'git-pull-request', color: 'charts.purple' };
    case 'Testing': return { icon: 'beaker', color: 'charts.blue' };
    case 'Verifying': return { icon: 'shield', color: 'charts.blue' };
    case 'Review': return { icon: 'eye', color: 'charts.yellow' };
    case 'Complete': return { icon: 'pass-filled', color: 'testing.iconPassed' };
    case 'Failed': return { icon: 'error', color: 'testing.iconFailed' };
    case 'LandingFailed': return { icon: 'git-merge', color: 'testing.iconFailed' };
    case 'Cancelled': return { icon: 'circle-slash', color: 'disabledForeground' };
    default: return { icon: 'circle-outline' };
  }
}

/** Voyage status look. */
export function voyageLook(status: string): StatusLook {
  switch (status) {
    case 'Open': return { icon: 'circle-large-outline', color: 'descriptionForeground' };
    case 'InProgress': return { icon: 'sync~spin', color: 'charts.blue' };
    case 'Complete': return { icon: 'pass-filled', color: 'testing.iconPassed' };
    case 'Failed': return { icon: 'error', color: 'testing.iconFailed' };
    case 'Cancelled': return { icon: 'circle-slash', color: 'disabledForeground' };
    default: return { icon: 'rocket' };
  }
}

/** Captain state look. */
export function captainLook(state: string): StatusLook {
  switch (state) {
    case 'Idle': return { icon: 'person', color: 'descriptionForeground' };
    case 'Working': return { icon: 'sync~spin', color: 'charts.blue' };
    case 'Planning':
    case 'Refining':
    case 'Analyzing': return { icon: 'lightbulb', color: 'charts.blue' };
    case 'Stalled': return { icon: 'warning', color: 'list.warningForeground' };
    case 'Stopping': return { icon: 'debug-stop', color: 'list.warningForeground' };
    case 'Quarantined': return { icon: 'error', color: 'testing.iconFailed' };
    default: return { icon: 'person' };
  }
}

/** Merge queue entry look. */
export function mergeLook(status: string): StatusLook {
  switch (status) {
    case 'Queued': return { icon: 'circle-large-outline', color: 'descriptionForeground' };
    case 'Testing': return { icon: 'beaker', color: 'charts.blue' };
    case 'Passed': return { icon: 'check', color: 'testing.iconPassed' };
    case 'Landed': return { icon: 'git-merge', color: 'testing.iconPassed' };
    case 'Failed': return { icon: 'error', color: 'testing.iconFailed' };
    case 'Cancelled': return { icon: 'circle-slash', color: 'disabledForeground' };
    default: return { icon: 'git-merge' };
  }
}

/** Needs You severity look. */
export function inboxLook(severity: string): StatusLook {
  switch ((severity || '').toLowerCase()) {
    case 'critical':
    case 'error': return { icon: 'error', color: 'testing.iconFailed' };
    case 'warning': return { icon: 'warning', color: 'list.warningForeground' };
    default: return { icon: 'bell', color: 'charts.yellow' };
  }
}

/** "In Progress" for "InProgress", "Pull Request Open" for "PullRequestOpen". */
export function humanize(value: string | null | undefined): string {
  if (!value) return '';
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/_/g, ' ');
}

/** Short elapsed time ("12s", "4m", "3h 5m", "2d") between two ISO timestamps (end defaults to now). */
export function elapsed(startIso: string | null | undefined, endIso?: string | null, now: Date = new Date()): string {
  if (!startIso) return '';
  const start = Date.parse(startIso);
  const end = endIso ? Date.parse(endIso) : now.getTime();
  if (Number.isNaN(start) || Number.isNaN(end)) return '';
  const seconds = Math.max(0, Math.floor((end - start) / 1000));
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return minutes % 60 ? `${hours}h ${minutes % 60}m` : `${hours}h`;
  return `${Math.floor(hours / 24)}d`;
}
