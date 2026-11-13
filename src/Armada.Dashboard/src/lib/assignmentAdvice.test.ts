import { describe, expect, it } from 'vitest';
import { assignmentAdviceTarget } from './assignmentAdvice';

describe('assignmentAdviceTarget', () => {
  it('opens the existing edit forms and preserves the endpoint hub tab', () => {
    expect(assignmentAdviceTarget({ text: '', action: 'EditVessel', targetId: 'vsl_1' })?.path).toBe('/vessels/vsl_1?edit=1');
    expect(assignmentAdviceTarget({ text: '', action: 'EditCaptain', targetId: 'cpt_1' })?.path).toBe('/captains/cpt_1?edit=1');
    expect(assignmentAdviceTarget({ text: '', action: 'EditEndpoint', targetId: 'mep_1' })?.path).toBe('/configuration?tab=endpoints&editEndpoint=mep_1');
    expect(assignmentAdviceTarget({ text: '', action: 'AddCaptain' })?.path).toBe('/captains?create=1');
  });
  it('uses existing native detail screens without promising an unsupported edit deep link', () => {
    expect(assignmentAdviceTarget({ text: '', action: 'EditCaptain', targetId: 'cpt_1' }, false)).toEqual({ path: '/captains/cpt_1', label: 'Review captain' });
  });
  it('keeps unprivileged and incomplete advice as text and escapes target IDs', () => {
    expect(assignmentAdviceTarget({ text: 'Read only', action: null })).toBeNull();
    expect(assignmentAdviceTarget({ text: '', action: 'EditVessel' })).toBeNull();
    expect(assignmentAdviceTarget({ text: '', action: 'OpenMission', targetId: 'x?other=1' })?.path).toBe('/missions/x%3Fother%3D1');
  });
});
