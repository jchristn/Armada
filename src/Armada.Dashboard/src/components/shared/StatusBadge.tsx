interface StatusBadgeProps {
  status: string;
  className?: string;
}

const tooltips: Record<string, string> = {
  pending: 'Waiting to be assigned to a captain. Next: a captain will pick this up automatically when one becomes available.',
  assigned: 'Assigned to a captain but work has not started yet. Next: the captain will begin working shortly.',
  inprogress: 'A captain is actively working on this mission. Next: wait for the captain to finish, or check the log for progress.',
  workproduced: 'Code was produced on a branch but has not landed yet. Next: retry landing, or manually merge the branch if retry fails.',
  testing: 'Mission work is being tested or validated. Next: wait for tests to complete, then review results.',
  review: 'Mission work is ready for review. Next: approve (transitions to WorkProduced for landing) or reject (restart or cancel).',
  complete: 'Mission completed and work has been merged. No action needed.',
  completed: 'Completed successfully. No action needed.',
  failed: 'Mission failed after exhausting recovery attempts. Next: review the log, fix the issue, and restart the mission.',
  landingfailed: 'Code was produced but the merge failed (conflict, push error, etc.). Next: retry landing, or cancel and re-dispatch.',
  cancelled: 'Mission was cancelled. Next: restart if the work is still needed.',
  open: 'Voyage is open and accepting missions. Next: missions will be dispatched to captains automatically.',
  idle: 'Captain is available. Next: it will pick up the next pending mission automatically.',
  working: 'Captain is actively executing a mission. Next: wait for completion, or check the mission log.',
  stalled: 'Captain is unresponsive -- recovery attempts exhausted. Next: stop the captain and restart it.',
  stopping: 'Captain is being stopped. Next: wait for it to finish stopping.',
  queued: 'Queued in the merge queue, waiting to be processed. Next: process the merge queue to test and land.',
  passed: 'Tests passed in the merge queue. Next: process the merge queue to land the branch.',
  landed: 'Branch has been merged into the target branch. No action needed.',
  active: 'Active and available for use. No action needed.',
  inactive: 'No longer active. Next: reactivate if needed.',
  assignment: 'Captain was assigned a new mission.',
  progress: 'Progress update from a captain or the system.',
  completion: 'A captain has finished its work.',
  error: 'An error occurred during execution. Next: check the log for details.',
  heartbeat: 'Periodic heartbeat -- captain is alive.',
  nudge: 'A nudge signal sent to prompt action.',
  mail: 'A message between the admiral and a captain.',
  pass: 'Health check passed. No action needed.',
  warn: 'Health check has warnings. Next: review the warnings and address if needed.',
  fail: 'Health check failed. Next: investigate and resolve the issue immediately.',
};

export default function StatusBadge({ status, className = '' }: StatusBadgeProps) {
  const normalized = (status || '').toLowerCase();
  const tooltip = tooltips[normalized] || '';

  return (
    <span
      className={`tag ${normalized} ${className}`.trim()}
      title={tooltip}
    >
      {status}
    </span>
  );
}
