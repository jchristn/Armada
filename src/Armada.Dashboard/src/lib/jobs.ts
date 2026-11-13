/** Job statuses after which a job can no longer be cancelled. */
export const TERMINAL_JOB_STATUSES = ['Succeeded', 'Failed', 'Cancelled'];

/** True when background work has finished; workflows may cancel only non-terminal work. */
export function isJobTerminal(status: string): boolean {
  return TERMINAL_JOB_STATUSES.includes(status);
}
