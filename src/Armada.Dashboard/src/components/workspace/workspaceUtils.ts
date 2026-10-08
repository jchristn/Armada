// Moved to lib/workspace.ts (shared with the mobile app); re-exported for existing imports.
export {
  normalizeWorkspacePath,
  getWorkspaceName,
  getWorkspaceParentPath,
  inferWorkspaceLanguage,
  buildScopedFileDirective,
  buildWorkspacePlanningDraft,
  buildWorkspaceDispatchDraft,
  buildWorkspaceContextSnippet,
} from '../../lib/workspace';
