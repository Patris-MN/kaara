import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import { loadCurrentUser, login as loginRequest, registerAccount } from "../api/client";
import { ApiError } from "../api/errors";
import { setUnauthorizedHandler } from "../api/http";
import {
  clearSession,
  readAccessToken,
  writeAccessToken,
} from "../api/session";
import type { AuthUser } from "../api/types";

type AuthContextValue = {
  user: AuthUser | null;
  token: string | null;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const logout = useCallback(() => {
    clearSession();
    setToken(null);
    setUser(null);
  }, []);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearSession();
      setToken(null);
      setUser(null);
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  useEffect(() => {
    let cancelled = false;
    const stored = readAccessToken();
    if (!stored) {
      setIsLoading(false);
      return;
    }

    void loadCurrentUser(stored)
      .then((current) => {
        if (cancelled) {
          return;
        }
        setToken(stored);
        setUser(current);
      })
      .catch((error: unknown) => {
        if (cancelled) {
          return;
        }
        if (error instanceof ApiError && error.status === 401) {
          clearSession();
        }
        setToken(null);
        setUser(null);
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const result = await loginRequest(email, password);
    writeAccessToken(result.accessToken);
    setToken(result.accessToken);
    setUser({
      userId: result.userId,
      email: result.email,
      displayName: result.displayName,
      isPlatformAdministrator: result.isPlatformAdministrator,
    });
  }, []);

  const register = useCallback(async (email: string, password: string, displayName: string) => {
    await registerAccount(email, password, displayName);
  }, []);

  const value = useMemo(
    () => ({ user, token, isLoading, login, register, logout }),
    [user, token, isLoading, login, register, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return context;
}
