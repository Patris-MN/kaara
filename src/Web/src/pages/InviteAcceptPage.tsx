import { type FormEvent, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  acceptInvitationByToken,
  previewInvitation,
  registerAndAcceptInvitation,
} from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { InvitationPreview } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { Field, StatusBanner } from "../components/Ui";
import { useFeedback } from "../feedback/FeedbackProvider";
import { formatAbsoluteTime } from "../time/relativeTime";

type LoadState = "loading" | "ready" | "error";

export function InviteAcceptPage() {
  const { t } = useTranslation(["invitations", "auth", "tenants", "common"]);
  const { token: invitationToken } = useParams();
  const { token: authToken, user, login, logout, isLoading: authLoading } = useAuth();
  const { show } = useFeedback();
  const navigate = useNavigate();
  const requestId = useRef(0);

  const [preview, setPreview] = useState<InvitationPreview | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [displayNameError, setDisplayNameError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [confirmPasswordError, setConfirmPasswordError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!invitationToken) {
      setLoadState("error");
      setErrorMessage(t("invitations:errors.invalid"));
      return;
    }

    const current = requestId.current + 1;
    requestId.current = current;
    setLoadState("loading");
    setErrorMessage(null);
    setPreview(null);

    void (async () => {
      try {
        const nextPreview = await previewInvitation(invitationToken);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setPreview(nextPreview);
        setLoadState("ready");
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setLoadState("error");
        setErrorMessage(invitationErrorMessage(cause, t));
      }
    })();
  }, [invitationToken, t]);

  async function onAccept() {
    if (!authToken || !invitationToken || busy) {
      return;
    }
    setBusy(true);
    try {
      const result = await acceptInvitationByToken(authToken, invitationToken);
      show({
        tone: "success",
        title: t("invitations:acceptedTitle"),
        body: t("invitations:acceptedBody", { organization: preview?.organizationName ?? "" }),
      });
      navigate(`/app/tenants/${result.tenantId}`);
    } catch (cause) {
      setErrorMessage(invitationErrorMessage(cause, t));
    } finally {
      setBusy(false);
    }
  }

  async function onRegister(event: FormEvent) {
    event.preventDefault();
    if (!invitationToken || !preview || busy) {
      return;
    }

    const nextDisplayNameError = displayName.trim() ? null : t("common:errors.invalid_display_name");
    const nextPasswordError =
      password.length >= 8 ? null : t("common:errors.invalid_password");
    const nextConfirmError =
      password === confirmPassword ? null : t("invitations:errors.passwordMismatch");
    setDisplayNameError(nextDisplayNameError);
    setPasswordError(nextPasswordError);
    setConfirmPasswordError(nextConfirmError);
    if (nextDisplayNameError || nextPasswordError || nextConfirmError) {
      return;
    }

    setBusy(true);
    setErrorMessage(null);
    try {
      const result = await registerAndAcceptInvitation(
        invitationToken,
        displayName.trim(),
        password,
      );
      await login(preview.invitedEmail, password);
      show({
        tone: "success",
        title: t("auth:registerInviteSuccessTitle"),
        body: t("auth:registerInviteSuccessBody", { organization: preview.organizationName }),
      });
      navigate(`/app/tenants/${result.tenantId}`);
    } catch (cause) {
      if (isApiError(cause) && cause.code === "email_already_registered") {
        setErrorMessage(t("auth:errors.emailTaken"));
      } else {
        setErrorMessage(invitationErrorMessage(cause, t));
      }
    } finally {
      setBusy(false);
    }
  }

  const loginReturn = invitationToken ? `/invite/${invitationToken}` : "/app";
  const signedInEmailMatches =
    preview && user
      ? emailsMatch(user.email, preview.invitedEmail)
      : false;
  const wrongAccount =
    preview && user && !preview.requiresRegistration && !signedInEmailMatches;

  function workspaceAccessLabel(accessLevel: string): string {
    const normalized = accessLevel.toLowerCase();
    if (normalized === "edit") {
      return t("members:access.edit");
    }
    if (normalized === "view") {
      return t("members:access.view");
    }
    return accessLevel;
  }

  return (
    <main className="login-page register-page">
      <section className="login-pane" aria-labelledby="invite-accept-heading">
        <div className="login-pane-inner">
          <header className="login-header">
            <Link className="auth-brand" to="/" aria-label={t("auth:brandHome")}>
              <span className="auth-brand-mark" aria-hidden="true">
                <svg viewBox="0 0 32 32">
                  <path d="M7 8.5h8.5V17H7zM16.5 15H25v8.5h-8.5z" />
                  <path d="M15.5 12.75h2.25V15H15.5zM10.2 17h2.25v5.4H17v2.25h-6.8z" />
                </svg>
              </span>
              <span>{t("auth:productName")}</span>
            </Link>
            <LanguageSwitcher />
          </header>

          <div className="login-content register-content">
            <div className="login-intro">
              <p className="login-eyebrow">{t("invitations:eyebrow")}</p>
              <h1 id="invite-accept-heading">{t("invitations:title")}</h1>
              {loadState === "loading" || authLoading ? (
                <p role="status">{t("common:loading")}</p>
              ) : null}
            </div>

            {errorMessage ? <StatusBanner tone="error">{errorMessage}</StatusBanner> : null}

            {loadState === "ready" && preview ? (
              <div className="form-card">
                <p>{t("invitations:previewIntro", { organization: preview.organizationName })}</p>
                <ul className="invitation-preview-meta">
                  {preview.inviterDisplayName ? (
                    <li>
                      {t("invitations:invitedBy", { name: preview.inviterDisplayName })}
                    </li>
                  ) : null}
                  <li>
                    {t("invitations:invitedEmail")}: <strong>{preview.invitedEmail}</strong>
                  </li>
                  <li>
                    {t("invitations:invitedRole", {
                      role: t(`tenants:roles.${preview.role}`, { defaultValue: preview.role }),
                    })}
                  </li>
                  <li>
                    {t("invitations:expiresAt", {
                      date: formatAbsoluteTime(preview.expiresAtUtc),
                    })}
                  </li>
                </ul>
                {preview.workspaceGrants.length > 0 ? (
                  <div>
                    <p className="field-label">{t("invitations:workspaceGrants")}</p>
                    <ul className="member-access-list">
                      {preview.workspaceGrants.map((grant) => (
                        <li key={`${grant.workspaceName}-${grant.accessLevel}`} className="member-access-row">
                          <span>{grant.workspaceName}</span>
                          <span>{workspaceAccessLabel(grant.accessLevel)}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}

                {wrongAccount ? (
                  <div className="invitation-wrong-account">
                    <StatusBanner tone="error">
                      {t("invitations:wrongAccount", {
                        invitedEmail: preview.invitedEmail,
                        signedInEmail: user?.email ?? "",
                      })}
                    </StatusBanner>
                    <div className="dialog-actions">
                      <button
                        className="secondary-action"
                        type="button"
                        onClick={() => {
                          logout();
                        }}
                      >
                        {t("invitations:switchAccount")}
                      </button>
                      <Link className="text-link" to="/login" state={{ from: loginReturn }}>
                        {t("auth:signIn")}
                      </Link>
                    </div>
                  </div>
                ) : preview.requiresRegistration ? (
                  <form className="login-form" onSubmit={onRegister} noValidate>
                    <Field id="invite-bound-email" label={t("invitations:invitedEmail")}>
                      <input
                        id="invite-bound-email"
                        type="email"
                        readOnly
                        aria-readonly="true"
                        value={preview.invitedEmail}
                      />
                    </Field>
                    <Field
                      id="invite-display-name"
                      label={t("auth:displayName")}
                      error={displayNameError ?? undefined}
                    >
                      <input
                        id="invite-display-name"
                        type="text"
                        required
                        autoComplete="name"
                        value={displayName}
                        onChange={(event) => {
                          setDisplayName(event.target.value);
                          setDisplayNameError(null);
                        }}
                      />
                    </Field>
                    <Field id="invite-password" label={t("auth:password")} error={passwordError ?? undefined}>
                      <input
                        id="invite-password"
                        type="password"
                        required
                        autoComplete="new-password"
                        value={password}
                        onChange={(event) => {
                          setPassword(event.target.value);
                          setPasswordError(null);
                        }}
                      />
                    </Field>
                    <Field
                      id="invite-confirm-password"
                      label={t("invitations:confirmPassword")}
                      error={confirmPasswordError ?? undefined}
                    >
                      <input
                        id="invite-confirm-password"
                        type="password"
                        required
                        autoComplete="new-password"
                        value={confirmPassword}
                        onChange={(event) => {
                          setConfirmPassword(event.target.value);
                          setConfirmPasswordError(null);
                        }}
                      />
                    </Field>
                    <button className="login-submit" type="submit" disabled={busy} aria-busy={busy}>
                      {busy ? t("invitations:accepting") : t("invitations:createAccount")}
                    </button>
                  </form>
                ) : user ? (
                  <div className="dialog-actions">
                    <button
                      className="primary-action"
                      type="button"
                      disabled={busy}
                      aria-busy={busy}
                      onClick={() => void onAccept()}
                    >
                      {busy ? t("invitations:accepting") : t("invitations:accept")}
                    </button>
                  </div>
                ) : (
                  <p>
                    {t("invitations:loginPrompt")}{" "}
                    <Link className="text-link" to="/login" state={{ from: loginReturn }}>
                      {t("auth:signIn")}
                    </Link>
                  </p>
                )}
              </div>
            ) : null}
          </div>
        </div>
      </section>
    </main>
  );
}

function invitationErrorMessage(
  cause: unknown,
  t: (key: string, options?: Record<string, unknown>) => string,
): string {
  if (isApiError(cause)) {
    const key = `invitations:errors.${cause.code.replace(/-/g, "_")}`;
    const translated = t(key, { defaultValue: "" });
    if (translated) {
      return translated;
    }
    switch (cause.code) {
      case "invitation_invalid":
        return t("invitations:errors.invalid");
      case "invitation_expired":
        return t("invitations:errors.expired");
      case "invitation_revoked":
        return t("invitations:errors.revoked");
      case "invitation_already_accepted":
        return t("invitations:errors.alreadyAccepted");
      case "invitation_email_mismatch":
        return t("invitations:errors.emailMismatch");
      case "already_tenant_member":
        return t("invitations:errors.alreadyMember");
      default:
        break;
    }
  }
  return t("invitations:errors.generic");
}

function emailsMatch(left: string, right: string): boolean {
  return left.trim().toLowerCase() === right.trim().toLowerCase();
}
