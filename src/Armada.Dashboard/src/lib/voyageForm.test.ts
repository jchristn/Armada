import { describe, expect, it } from 'vitest';
import { buildVoyageCreateRequest, emptyVoyageForm, validateVoyageForm, type VoyageFormState } from './voyageForm';

function form(patch: Partial<VoyageFormState> = {}): VoyageFormState {
  return { ...emptyVoyageForm(), title: 'Docs sweep', vesselId: 'vsl_1', missions: [{ title: 'Update README', description: '', priority: 100 }], ...patch };
}

describe('validateVoyageForm', () => {
  it('requires a title, a vessel, and one titled mission, in that order', () => {
    expect(validateVoyageForm(emptyVoyageForm())).toBe('Voyage title is required.');
    expect(validateVoyageForm(form({ title: '  ' }))).toBe('Voyage title is required.');
    expect(validateVoyageForm(form({ vesselId: '' }))).toBe('Please select a vessel.');
    expect(validateVoyageForm(form({ missions: [{ title: ' ', description: 'x', priority: 1 }] }))).toBe('At least one mission with a title is required.');
    expect(validateVoyageForm(form())).toBeNull();
  });
});

describe('buildVoyageCreateRequest', () => {
  it('drops untitled rows, defaults descriptions and priorities, and omits unset options', () => {
    const request = buildVoyageCreateRequest(form({
      description: '  ',
      missions: [{ title: ' A ', description: '', priority: 0 }, { title: '', description: 'ignored', priority: 5 }, { title: 'B', description: ' do b ', priority: 7 }],
    }));
    expect(request).toEqual({
      title: 'Docs sweep',
      description: undefined,
      vesselId: 'vsl_1',
      missions: [
        { title: 'A', description: 'A', vesselId: 'vsl_1', priority: 100 },
        { title: 'B', description: 'do b', vesselId: 'vsl_1', priority: 7 },
      ],
    });
    expect(request).not.toHaveProperty('landingMode');
    expect(request).not.toHaveProperty('pipeline');
    expect(request).not.toHaveProperty('selectedPlaybooks');
  });

  it('sends the pipeline, playbooks, and landing mode when chosen', () => {
    const request = buildVoyageCreateRequest(form({
      pipeline: 'Reviewed',
      landingMode: 'PullRequest',
      selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'AttachIntoWorktree' }],
    }));
    expect(request.pipeline).toBe('Reviewed');
    expect(request.landingMode).toBe('PullRequest');
    expect(request.selectedPlaybooks).toEqual([{ playbookId: 'pbk_1', deliveryMode: 'AttachIntoWorktree' }]);
  });
});
