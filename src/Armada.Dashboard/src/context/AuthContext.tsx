import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import type { WhoAmIResult } from '../types/models';
import { setAuthToken, setOnUnauthorized, whoami } from '../api/client';

const SESSION_STORAGE_KEY = 'armada_session_token';

interface AuthState {
  sessionToken: string | null;
  user: WhoAmIResult | null;
  isAuthenticated: boolean;
  isAdmin: boolean;
  isTenantAdmin: boolean;
  loading: boolean;
  login: (token: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sessionToken, setSessionToken] = useState<string | null>(() => {
    return localStorage.getItem(SESSION_STORAGE_KEY);
  });
  const [user, setUser] = useState<WhoAmIResult | null>(null);
  const [loading, setLoading] = useState(true);

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

  const isAuthenticated = !!sessionToken && !!user;
  const isAdmin = user?.user?.isAdmin ?? false;
  const isTenantAdmin = isAdmin || (user?.user?.isTenantAdmin ?? false);

  return (
    <AuthContext.Provider value={{ sessionToken, user, isAuthenticated, isAdmin, isTenantAdmin, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
