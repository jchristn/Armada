import type { MergeEntry } from '../types/models';

/** The Enqueue Merge form. */
export interface EnqueueMergeForm {
  branchName: string;
  targetBranch: string;
  missionId: string;
  vesselId: string;
  testCommand: string;
  priority: number;
}

/** A blank Enqueue Merge form (target branch main, priority 0). */
export function emptyEnqueueMergeForm(): EnqueueMergeForm {
  return { branchName: '', targetBranch: 'main', missionId: '', vesselId: '', testCommand: '', priority: 0 };
}

/** The enqueueMerge request for the form (optional fields left out when empty; target defaults to main). */
export function buildEnqueueMergeRequest(form: EnqueueMergeForm): Partial<MergeEntry> {
  return {
    branchName: form.branchName,
    targetBranch: form.targetBranch || 'main',
    missionId: form.missionId || undefined,
    vesselId: form.vesselId || undefined,
    testCommand: form.testCommand || undefined,
    priority: form.priority,
  };
}
