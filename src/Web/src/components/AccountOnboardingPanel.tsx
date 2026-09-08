import { useState } from "react";
import { useTranslation } from "react-i18next";

import { acceptInvitation } from "../api/client";
import { isApiError } from "../api/errors";
import type { TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { EmptyState } from "./EmptyState";
import { useFeedback } from "../feedback/FeedbackProvider";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";

type AccountOnboardingPanelProps = {
  invitations: TenantMembership[];
  canCreateOrganization: boolean;
  onCreateClick: () => void;
};

export function AccountOnboardingPanel({
  invitations,
  canCreateOrganization,
  onCreateClick,
}: AccountOnboardingPanelProps) {
  const { t } = useTranslation(["tenants", "common"]);
  const { token, user } = useAuth();
  const { show } = useFeedback();
  const { markInvitationAccepted, refresh } = useTenantDirectory();
  const [busyTenantId, setBusyTenantId] = useState<string | null>(null);

  async function continueInvitation(tenantId: string) {
    if (!token || !user || busyTenantId) {
      return;
    }
    setBusyTenantId(tenantId);
    try {
      await acceptInvitation(token, tenantId);
      markInvitationAccepted(tenantId);
      await refresh();
      show({
        tone: "success",
        title: t("tenants:feedback.invitationAccepted"),
      });
    } catch (cause) {
      if (isApiError(cause) && cause.status === 403) {
        show({
          tone: "error",
          title: t("tenants:permissionTitle"),
          body: t("tenants:permissionBody"),
        });
      } else {
        show({
          tone: "error",
          title: t("common:feedback.errorTitle"),
          body: t("common:feedback.errorBody"),
        });
      }
    } finally {
      setBusyTenantId(null);
    }
  }

  if (invitations.length > 0) {
    return (
      <section className="account-onboarding">
        <EmptyState
          title={t("tenants:onboarding.pendingTitle")}
          body={t("tenants:onboarding.pendingBody")}
        />
        <ul className="account-onboarding-invitations">
          {invitations.map((invitation) => (
            <li key={invitation.tenantId}>
              <article className="account-onboarding-invitation">
                <div className="account-onboarding-invitation-copy">
                  <strong>{invitation.name}</strong>
                  <span>{t("tenants:invitedRole", { role: t(`tenants:roles.${invitation.role}`) })}</span>
                </div>
                <button
                  type="button"
                  className="primary-action"
                  disabled={busyTenantId !== null}
                  aria-busy={busyTenantId === invitation.tenantId}
                  onClick={() => void continueInvitation(invitation.tenantId)}
                >
                  {busyTenantId === invitation.tenantId ? (
                    <span className="button-spinner" aria-hidden="true" />
                  ) : null}
                  {t("tenants:onboarding.continueInvitation")}
                </button>
              </article>
            </li>
          ))}
        </ul>
        {canCreateOrganization ? (
          <p className="account-onboarding-secondary">
            {t("tenants:onboarding.orCreateFirst")}
            <button type="button" className="link-button" onClick={onCreateClick}>
              {t("tenants:onboarding.createFirstOrganization")}
            </button>
          </p>
        ) : null}
      </section>
    );
  }

  if (canCreateOrganization) {
    return (
      <EmptyState
        title={t("tenants:onboarding.welcomeTitle")}
        body={t("tenants:onboarding.welcomeBody")}
        action={
          <button type="button" className="primary-action" onClick={onCreateClick}>
            <span aria-hidden="true">+</span>
            {t("tenants:onboarding.createFirstOrganization")}
          </button>
        }
      />
    );
  }

  return (
    <EmptyState
      title={t("tenants:onboarding.noAccessTitle")}
      body={t("tenants:onboarding.noAccessBody")}
    />
  );
}
