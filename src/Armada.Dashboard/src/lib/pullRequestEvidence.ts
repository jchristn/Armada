import { enumerateMissionPullRequestChecks, enumerateMissionPullRequestReviews, getMissionGitHubPullRequest } from '../api/client';
import type { GitHubPullRequestCheck, GitHubPullRequestDetail, GitHubPullRequestReview } from '../types/models';

/** Reviews and checks shown per pull request; the detail's counts say how many there are in all. */
export const PULL_REQUEST_LIST_PAGE_SIZE = 10;

/** A mission pull request's detail with the newest page of its reviews and checks (shared by the dashboard and mobile). */
export interface PullRequestEvidence {
  detail: GitHubPullRequestDetail;
  reviews: GitHubPullRequestReview[];
  checks: GitHubPullRequestCheck[];
}

/** Load the detail, then one GitHub-paged page of reviews and of checks (never the whole lists). */
export async function loadPullRequestEvidence(missionId: string): Promise<PullRequestEvidence> {
  const detail = await getMissionGitHubPullRequest(missionId);
  const [reviews, checks] = await Promise.all([
    detail.reviewCount > 0 ? enumerateMissionPullRequestReviews(missionId, { pageSize: PULL_REQUEST_LIST_PAGE_SIZE }).catch(() => null) : Promise.resolve(null),
    detail.checkCount > 0 ? enumerateMissionPullRequestChecks(missionId, { pageSize: PULL_REQUEST_LIST_PAGE_SIZE }).catch(() => null) : Promise.resolve(null),
  ]);
  return { detail, reviews: reviews?.objects ?? [], checks: checks?.objects ?? [] };
}

/** " (shown/total)" when a list shows fewer items than the total, else "". */
export function shownOfTotal(shown: number, total: number | null | undefined): string {
  return total != null && total > shown ? ` (${shown}/${total})` : '';
}
