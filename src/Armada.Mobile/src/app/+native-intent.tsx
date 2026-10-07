import { appPathFromLink } from '../navigation/deepLinks';
import { notePendingLink } from '../navigation/pendingLink';

/**
 * Every incoming link (armada://..., a pasted dashboard URL, a notification) is mapped to the app path for the same
 * dashboard page. Unknown or unsafe links open the app without navigating.
 */
export function redirectSystemPath({ path }: { path: string; initial: boolean }): string | null {
  try {
    const mapped = appPathFromLink(path);
    notePendingLink(mapped);
    return mapped;
  } catch {
    return null;
  }
}
