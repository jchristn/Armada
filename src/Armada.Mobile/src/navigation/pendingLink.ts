/**
 * A deep link that arrived while no one was signed in (the protected routes redirect it to sign-in). The root
 * layout opens it once sign-in completes, so tapping a link or notification is not lost to the sign-in screen.
 */
let pending: string | null = null;
let signedIn = false;

export function setSignedInForLinks(value: boolean): void {
  signedIn = value;
}

/** Remember a link if it arrived while signed out. */
export function notePendingLink(path: string | null): void {
  if (!signedIn && path && path !== '/' && path !== '/sign-in') pending = path;
}

/** The pending link, cleared on read. */
export function takePendingLink(): string | null {
  const value = pending;
  pending = null;
  return value;
}
