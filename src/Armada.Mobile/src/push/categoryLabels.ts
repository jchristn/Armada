import type { PushCategory } from './types';

/** Settings label and description of each push category (English source strings for t()). */
export const CATEGORY_TEXT: Record<PushCategory, { label: string; description: string }> = {
  AskProposal: { label: 'Ask Armada proposals', description: 'An action Ask Armada proposed is waiting for your approval' },
  CliPermission: { label: 'CLI permission requests', description: 'A captain is asking to use a tool' },
  MissionReview: { label: 'Mission reviews', description: 'A mission is waiting for review' },
  DeploymentApproval: { label: 'Deployment approvals', description: 'A deployment is waiting for approval' },
  MissionFailed: { label: 'Failed missions', description: 'A mission failed' },
  LandingFailed: { label: 'Failed landings', description: 'A mission could not land' },
  CaptainStalled: { label: 'Stalled captains', description: 'A captain stopped making progress' },
  VoyageFinished: { label: 'Finished voyages', description: 'A voyage completed or failed' },
};
