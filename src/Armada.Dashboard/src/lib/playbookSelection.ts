import type { Playbook, PlaybookDeliveryMode, SelectedPlaybook } from '../types/models';

/** Pure logic of the playbook selector, shared by the dashboard (PlaybookSelector) and the mobile app. */

/** Delivery modes in display order, with English labels and descriptions (translated by the caller). */
export const PLAYBOOK_DELIVERY_MODES: Record<PlaybookDeliveryMode, { label: string; description: string }> = {
  InlineFullContent: {
    label: 'Inline Full Content',
    description: 'Inject the complete markdown into the mission instructions.',
  },
  InstructionWithReference: {
    label: 'Instruction With Reference',
    description: 'Tell the model to read the materialized playbook path outside the worktree.',
  },
  AttachIntoWorktree: {
    label: 'Attach Into Worktree',
    description: 'Materialize the playbook in `.armada/playbooks/` and instruct the model to read it there.',
  },
};

export const DEFAULT_PLAYBOOK_DELIVERY_MODE: PlaybookDeliveryMode = 'InlineFullContent';

/** Playbooks that can be attached (inactive ones are hidden). */
export function activePlaybooksOf(playbooks: readonly Playbook[]): Playbook[] {
  return playbooks.filter((playbook) => playbook.active !== false);
}

/** Active playbooks not yet selected (the "add" row's choices). */
export function availablePlaybooks(playbooks: readonly Playbook[], selected: readonly SelectedPlaybook[]): Playbook[] {
  const selectedIds = new Set(selected.map((item) => item.playbookId));
  return activePlaybooksOf(playbooks).filter((playbook) => !selectedIds.has(playbook.id));
}

/** Choices for an existing row: its own playbook (even if inactive) plus every active one not chosen elsewhere. */
export function playbookOptionsForRow(playbooks: readonly Playbook[], selected: readonly SelectedPlaybook[], playbookId: string): Playbook[] {
  const otherSelectedIds = new Set(selected.filter((item) => item.playbookId !== playbookId).map((item) => item.playbookId));
  const options = activePlaybooksOf(playbooks).filter((playbook) => playbook.id === playbookId || !otherSelectedIds.has(playbook.id));
  const current = playbooks.find((playbook) => playbook.id === playbookId);
  if (current && !options.some((playbook) => playbook.id === current.id)) return [current, ...options];
  return options;
}
