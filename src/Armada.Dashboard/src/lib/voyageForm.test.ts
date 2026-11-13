import { describe, expect, it } from 'vitest';
import { buildVoyageCreateRequest, emptyVoyageForm, parseOwnedPaths, removeVoyageMissionRow, validateVoyageForm, type VoyageFormState } from './voyageForm';

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

describe('mission hierarchy in the voyage form', () => {
  it('sends keys only for referenced rows, a parent only when it is a kept earlier row, and owned paths', () => {
    const request = buildVoyageCreateRequest(form({
      missions: [
        { key: 'm1', title: 'Schema', description: '', priority: 100, ownedPaths: 'src/Db/\n src/Db/ , docs/schema.md' },
        { key: 'm2', title: 'API', description: '', priority: 100, parentKey: 'm1', branchStrategy: 'ForkFromParent' },
        { key: 'm3', title: '', description: 'dropped', priority: 100 },
        { key: 'm4', title: 'Orphan', description: '', priority: 100, parentKey: 'm3', branchStrategy: 'ContinueParent' },
        { key: 'm5', title: 'Forward', description: '', priority: 100, parentKey: 'm6' },
        { key: 'm6', title: 'Later', description: '', priority: 100 },
      ],
    }));
    expect(request.missions[0]).toEqual({ title: 'Schema', description: 'Schema', vesselId: 'vsl_1', priority: 100, key: 'm1', ownedPaths: ['src/Db/', 'docs/schema.md'] });
    expect(request.missions[1]).toEqual({ title: 'API', description: 'API', vesselId: 'vsl_1', priority: 100, parentKey: 'm1', branchStrategy: 'ForkFromParent' });
    expect(request.missions[2]).not.toHaveProperty('parentKey');
    expect(request.missions[2]).not.toHaveProperty('branchStrategy');
    expect(request.missions[3]).not.toHaveProperty('parentKey');
    expect(request.missions[4]).not.toHaveProperty('key');
  });

  it('clears the parent of rows whose parent is removed', () => {
    const rows = [
      { key: 'a', title: 'A', description: '', priority: 100 },
      { key: 'b', title: 'B', description: '', priority: 100, parentKey: 'a', branchStrategy: 'ForkFromParent' as const },
    ];
    expect(removeVoyageMissionRow(rows, 0)).toEqual([{ key: 'b', title: 'B', description: '', priority: 100, parentKey: '', branchStrategy: '' }]);
  });

  it('parses owned paths on commas and newlines', () => {
    expect(parseOwnedPaths(' a.cs,\nb/ ,, a.cs ')).toEqual(['a.cs', 'b/']);
    expect(parseOwnedPaths(undefined)).toEqual([]);
  });
});
