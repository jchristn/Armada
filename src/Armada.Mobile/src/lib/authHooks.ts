import type { AuthHooks } from '../auth/AuthContext';

/**
 * Run several services' auth hooks as one (AuthProvider takes a single hooks object). Each hook runs in order and
 * a failing one never stops the next.
 */
export function combineAuthHooks(...all: AuthHooks[]): AuthHooks {
  return {
    onSessionEnding: async (profile, session) => {
      for (const hooks of all) {
        try {
          await hooks.onSessionEnding?.(profile, session);
        } catch {
          // Cleanup is best effort; the session still ends.
        }
      }
    },
  };
}
