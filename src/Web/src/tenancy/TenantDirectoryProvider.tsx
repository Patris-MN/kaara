import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";

import { listInvitations, listTenants } from "../api/client";
import type { TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";

type TenantDirectoryValue = {
  tenants: TenantMembership[];
  invitations: TenantMembership[];
  isRefreshing: boolean;
  error: unknown | null;
  refresh: () => Promise<void>;
  markInvitationAccepted: (tenantId: string) => void;
};

const TenantDirectoryContext = createContext<TenantDirectoryValue | null>(null);

export function TenantDirectoryProvider({ children }: { children: ReactNode }) {
  const { token } = useAuth();
  const requestId = useRef(0);
  const [tenants, setTenants] = useState<TenantMembership[]>([]);
  const [invitations, setInvitations] = useState<TenantMembership[]>([]);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<unknown | null>(null);

  const markInvitationAccepted = useCallback((tenantId: string) => {
    setInvitations((currentInvitations) => {
      const accepted = currentInvitations.find((invitation) => invitation.tenantId === tenantId);
      if (accepted) {
        setTenants((currentTenants) =>
          currentTenants.some((tenant) => tenant.tenantId === tenantId)
            ? currentTenants
            : [...currentTenants, { ...accepted, status: "Active" }],
        );
      }
      return currentInvitations.filter((invitation) => invitation.tenantId !== tenantId);
    });
  }, []);

  const refresh = useCallback(async () => {
    const current = requestId.current + 1;
    requestId.current = current;

    if (!token) {
      setTenants([]);
      setInvitations([]);
      setError(null);
      setIsRefreshing(false);
      return;
    }

    setIsRefreshing(true);
    try {
      const [nextTenants, nextInvitations] = await Promise.all([
        listTenants(token),
        listInvitations(token),
      ]);
      if (current !== requestId.current) {
        return;
      }
      setTenants(nextTenants);
      setInvitations(nextInvitations);
      setError(null);
    } catch (cause) {
      if (current === requestId.current) {
        setError(cause);
      }
      throw cause;
    } finally {
      if (current === requestId.current) {
        setIsRefreshing(false);
      }
    }
  }, [token]);

  useEffect(() => {
    void refresh().catch(() => undefined);
    return () => {
      requestId.current += 1;
    };
  }, [refresh]);

  const value = useMemo(
    () => ({ tenants, invitations, isRefreshing, error, refresh, markInvitationAccepted }),
    [tenants, invitations, isRefreshing, error, refresh, markInvitationAccepted],
  );

  return (
    <TenantDirectoryContext.Provider value={value}>
      {children}
    </TenantDirectoryContext.Provider>
  );
}

export function useTenantDirectory(): TenantDirectoryValue {
  const value = useContext(TenantDirectoryContext);
  if (!value) {
    throw new Error("useTenantDirectory must be used inside TenantDirectoryProvider");
  }
  return value;
}
