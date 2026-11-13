import type { AssignmentBlockerAdvice } from '../types/models';

/** Only known in-app targets can become advice links; no server-supplied URL is executed. */
export function assignmentAdviceTarget(advice: AssignmentBlockerAdvice, editForms = true): { path: string; label: string } | null {
  const id = advice.targetId ? encodeURIComponent(advice.targetId) : null;
  switch (advice.action) {
    case 'EditVessel': return id ? { path: `/vessels/${id}${editForms ? '?edit=1' : ''}`, label: editForms ? 'Edit vessel limits' : 'Review vessel' } : null;
    case 'EditCaptain': return id ? { path: `/captains/${id}${editForms ? '?edit=1' : ''}`, label: editForms ? 'Edit captain capacity' : 'Review captain' } : null;
    case 'AddCaptain': return { path: editForms ? '/captains?create=1' : '/captains', label: editForms ? 'Add captain' : 'View captains' };
    case 'EditSettings': return { path: '/server', label: 'Review server settings' };
    case 'EditEndpoint': return id ? { path: `/configuration?tab=endpoints${editForms ? `&editEndpoint=${id}` : ''}`, label: 'Review endpoint' } : null;
    case 'OpenMission': return id ? { path: `/missions/${id}`, label: 'Open mission' } : null;
    case 'OpenVoyage': return id ? { path: `/voyages/${id}`, label: 'Open voyage' } : null;
    default: return null;
  }
}
