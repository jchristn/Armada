import {
  buildScopedFileDirective,
  buildWorkspaceDispatchDraft,
  buildWorkspacePlanningDraft,
  normalizeWorkspacePath,
} from './workspaceUtils';

const vessel = {
  id: 'vsl_123',
  tenantId: null,
  fleetId: 'flt_123',
  name: 'Workspace Vessel',
  repoUrl: 'https://example.com/repo.git',
  localPath: null,
  workingDirectory: 'C:/code/workspace',
  defaultBranch: 'main',
  projectContext: null,
  styleGuide: null,
  enableModelContext: false,
  modelContext: null,
  landingMode: null,
  branchCleanupPolicy: null,
  requirePassingChecksToLand: false,
  defaultPipelineId: null,
  protectedBranchPatterns: [],
  releaseBranchPrefix: 'release/',
  hotfixBranchPrefix: 'hotfix/',
  requirePullRequestForProtectedBranches: false,
  requireMergeQueueForReleaseBranches: false,
  hasGitHubTokenOverride: false,
  active: true,
  createdUtc: '2026-05-01T00:00:00Z',
  lastUpdateUtc: '2026-05-01T00:00:00Z',
};

describe('workspaceUtils', () => {
  it('normalizes Windows-style workspace paths', () => {
    expect(normalizeWorkspacePath('\\src\\Feature\\Thing.cs')).toBe('src/Feature/Thing.cs');
  });

  it('builds a scoped file directive that reuses Armada mission conventions', () => {
    expect(buildScopedFileDirective(['src/One.cs', 'src/Two.cs'])).toBe('Touch only src/One.cs, src/Two.cs');
  });

  it('builds planning and dispatch drafts around the scoped selection', () => {
    const planning = buildWorkspacePlanningDraft(vessel, ['src/Feature/Thing.cs']);
    const dispatch = buildWorkspaceDispatchDraft(vessel, ['src/Feature/Thing.cs']);

    expect(planning.prompt).toContain('Touch only src/Feature/Thing.cs');
    expect(planning.prompt).toContain('Help me plan the changes needed');
    expect(dispatch.prompt).toContain('Touch only src/Feature/Thing.cs');
    expect(dispatch.prompt).toContain('Implement the requested change');
  });
});
