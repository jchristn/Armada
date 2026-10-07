/** Job statuses after which a job can no longer be cancelled. */
export const TERMINAL_JOB_STATUSES = ['Succeeded', 'Failed', 'Cancelled'];

/** True when the job has finished (the Jobs page offers Cancel only before this). */
export function isJobTerminal(status: string): boolean {
  return TERMINAL_JOB_STATUSES.includes(status);
}
