import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import type { WhoAmIResult } from '../types/models';
import { setAuthToken, setOnUnauthorized, whoami } from '../api/client';

const SESSION_STORAGE_KEY = 'armada_session_token';
const PASSWORD_CHANGE_SKIP_KEY_PREFIX = 'armada_password_change_skipped:';

function passwordChangeSkipKey(me: WhoAmIResult | null): string | null {
  const id = me?.user?.id;
  return id ? PASSWORD_CHANGE_SKIP_KEY_PREFIX + id : null;
}

function readPasswordChangeSkipped(me: WhoAmIResult | null): boolean {
  const key = passwordChangeSkipKey(me);
  if (!key) return false;
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

interface AuthState {
  sessionToken: string | null;
  user: WhoAmIResult | null;
  isAuthenticated: boolean;
  isAdmin: boolean;
  isTenantAdmin: boolean;
  loading: boolean;
  login: (token: string) => Promise<void>;
  logout: () => void;
  /** Re-read whoami (for example after a password change). */
  refresh: () => Promise<void>;
  /** The signed-in user chose to keep the default password for now (remembered per user in this browser). */
  passwordChangeSkipped: boolean;
  /** Keep the default password and continue to the dashboard; the default credentials banner stays visible. */
  skipPasswordChange: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sessionToken, setSessionToken] = useState<string | null>(() => {
    return localStorage.getItem(SESSION_STORAGE_KEY);
  });
  const [user, setUser] = useState<WhoAmIResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [skippedUserId, setSkippedUserId] = useState<string | null>(null);

  const logout = useCallback(() => {
    setSessionToken(null);
    setUser(null);
    setAuthToken(null);
    localStorage.removeItem(SESSION_STORAGE_KEY);
  }, []);

  useEffect(() => {
    setOnUnauthorized(logout);
  }, [logout]);

  // Restore session on mount
  useEffect(() => {
    const storedToken = localStorage.getItem(SESSION_STORAGE_KEY);
    if (storedToken) {
      setAuthToken(storedToken);
      whoami()
        .then((me) => {
          setUser(me);
          setSessionToken(storedToken);
        })
        .catch(() => {
          // Token is stale, clear it
          localStorage.removeItem(SESSION_STORAGE_KEY);
          setSessionToken(null);
          setAuthToken(null);
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const login = useCallback(async (token: string) => {
    setLoading(true);
    try {
      setSessionToken(token);
      setAuthToken(token);
      localStorage.setItem(SESSION_STORAGE_KEY, token);
      const me = await whoami();
      setUser(me);
    } catch {
      setSessionToken(null);
      setAuthToken(null);
      localStorage.removeItem(SESSION_STORAGE_KEY);
      throw new Error('Login failed');
    } finally {
      setLoading(false);
    }
  }, []);

  const refresh = useCallback(async () => {
    const me = await whoami();
    setUser(me);
  }, []);

  const skipPasswordChange = useCallback(() => {
    const key = passwordChangeSkipKey(user);
    if (key) {
      try {
        localStorage.setItem(key, '1');
      } catch {
        // Storage unavailable: the skip still holds for this page load.
      }
    }
    setSkippedUserId(user?.user?.id ?? null);
  }, [user]);

  const passwordChangeSkipped = readPasswordChangeSkipped(user)
    || (!!user?.user?.id && skippedUserId === user.user.id);

  const isAuthenticated = !!sessionToken && !!user;
  const isAdmin = user?.user?.isAdmin ?? false;
  const isTenantAdmin = isAdmin || (user?.user?.isTenantAdmin ?? false);

  return (
    <AuthContext.Provider value={{ sessionToken, user, isAuthenticated, isAdmin, isTenantAdmin, loading, login, logout, refresh, passwordChangeSkipped, skipPasswordChange }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
