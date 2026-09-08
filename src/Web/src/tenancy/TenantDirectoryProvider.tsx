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

import { listInvitations, listTenants, getAccountCapabilities } from "../api/client";
import type { AccountCapabilities, TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";

type TenantDirectoryValue = {
  tenants: TenantMembership[];
  invitations: TenantMembership[];
  accountCapabilities: AccountCapabilities | null;
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
  const [accountCapabilities, setAccountCapabilities] = useState<AccountCapabilities | null>(null);
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
      setAccountCapabilities(null);
      setError(null);
      setIsRefreshing(false);
      return;
    }

    setIsRefreshing(true);
    try {
      const [nextTenants, nextInvitations, nextCapabilities] = await Promise.all([
        listTenants(token),
        listInvitations(token),
        getAccountCapabilities(token),
      ]);
      if (current !== requestId.current) {
        return;
      }
      setTenants(nextTenants);
      setInvitations(nextInvitations);
      setAccountCapabilities(nextCapabilities);
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
    // Token-scoped directory fetch. setState here synchronizes remote memberships.
    // oxlint-disable-next-line react/set-state-in-effect
    void refresh().catch(() => undefined);
    return () => {
      requestId.current += 1;
    };
  }, [refresh]);

  const value = useMemo(
    () => ({
      tenants,
      invitations,
      accountCapabilities,
      isRefreshing,
      error,
      refresh,
      markInvitationAccepted,
    }),
    [tenants, invitations, accountCapabilities, isRefreshing, error, refresh, markInvitationAccepted],
  );

  return (
    <TenantDirectoryContext.Provider value={value}>
      {children}
    </TenantDirectoryContext.Provider>
  );
}

// Hook colocated with its provider. Splitting the file would not change behavior.
// oxlint-disable-next-line react/only-export-components
export function useTenantDirectory(): TenantDirectoryValue {
  const value = useContext(TenantDirectoryContext);
  if (!value) {
    throw new Error("useTenantDirectory must be used inside TenantDirectoryProvider");
  }
  return value;
}
