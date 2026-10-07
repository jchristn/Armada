/** The git anchors a dock records when it is provisioned (Dock.gitAnchorsJson). */
export interface DockGitAnchors {
  startCommit?: string | null;
  targetBranch?: string | null;
  workingBranch?: string | null;
  recentPathCommits?: string[];
  subjectTermsPresent?: string[];
}

/** Parses Dock.gitAnchorsJson; null when it is missing or not valid JSON. */
export function parseDockGitAnchors(json: string | null | undefined): DockGitAnchors | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as DockGitAnchors | null;
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch {
    return null;
  }
}
