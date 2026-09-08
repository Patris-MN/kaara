import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { changePassword, getAccountProfile, updateAccountProfile } from "../api/client";
import { isApiError } from "../api/errors";
import { useAuth } from "../auth/AuthProvider";
import { isRegistrationPasswordReady, PasswordFieldGroup } from "../auth/PasswordFieldGroup";
import { PageHeader } from "../components/PageHeader";
import { Field, StatusBanner } from "../components/Ui";
import { UserAvatar } from "../components/UserAvatar";
import { useFeedback } from "../feedback/FeedbackProvider";

type ProfileSection = "personal" | "security";
type ProfileLoadState = "loading" | "error" | "ready";

export function ProfilePage() {
  const { t } = useTranslation(["profile", "auth", "common"]);
  const { user, token, updateLocalUser } = useAuth();
  const { show } = useFeedback();
  const navigate = useNavigate();
  const [section, setSection] = useState<ProfileSection>("personal");
  const [loadState, setLoadState] = useState<ProfileLoadState>("loading");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [hasLocalCredential, setHasLocalCredential] = useState(true);
  const [savingProfile, setSavingProfile] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [currentVisible, setCurrentVisible] = useState(false);
  const [newVisible, setNewVisible] = useState(false);
  const [confirmVisible, setConfirmVisible] = useState(false);
  const [savingPassword, setSavingPassword] = useState(false);
  const [passwordError, setPasswordError] = useState<string | null>(null);

  const initialDisplayNameRef = useRef("");

  const loadProfile = useCallback(async () => {
    if (!token) {
      setLoadState("error");
      return;
    }
    setLoadState("loading");
    setSaveError(null);
    try {
      const profile = await getAccountProfile(token);
      setDisplayName(profile.displayName);
      setEmail(profile.email);
      initialDisplayNameRef.current = profile.displayName;
      setHasLocalCredential(profile.hasLocalCredential);
      setLoadState("ready");
    } catch {
      setLoadState("error");
    }
  }, [token]);

  useEffect(() => {
    void loadProfile();
  }, [loadProfile]);

  const personalDirty = loadState === "ready" && displayName.trim() !== initialDisplayNameRef.current.trim();
  const passwordReady = isRegistrationPasswordReady(newPassword, confirmNewPassword);

  async function onSaveProfile(event: React.FormEvent) {
    event.preventDefault();
    if (!token || savingProfile || !personalDirty || loadState !== "ready") {
      return;
    }
    setSavingProfile(true);
    setSaveError(null);
    try {
      const updated = await updateAccountProfile(token, displayName.trim());
      initialDisplayNameRef.current = updated.displayName;
      setDisplayName(updated.displayName);
      setEmail(updated.email);
      updateLocalUser({ displayName: updated.displayName, email: updated.email });
      show({
        tone: "success",
        title: t("profile:updatedTitle"),
        body: t("profile:updatedBody"),
      });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "invalid_display_name") {
        setSaveError(t("common:errors.invalid_display_name"));
      } else {
        setSaveError(t("profile:saveFailed"));
      }
    } finally {
      setSavingProfile(false);
    }
  }

  async function onChangePassword(event: React.FormEvent) {
    event.preventDefault();
    if (!token || savingPassword || !passwordReady || !currentPassword || loadState !== "ready") {
      return;
    }
    setSavingPassword(true);
    setPasswordError(null);
    try {
      await changePassword(token, currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      setConfirmNewPassword("");
      show({
        tone: "success",
        title: t("profile:passwordChangedTitle"),
        body: t("profile:passwordChangedBody"),
      });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "invalid_current_password") {
        setPasswordError(t("profile:wrongCurrentPassword"));
      } else if (isApiError(cause) && cause.code === "invalid_password") {
        setPasswordError(t("profile:invalidNewPassword"));
      } else {
        setPasswordError(t("profile:saveFailed"));
      }
    } finally {
      setSavingPassword(false);
    }
  }

  return (
    <div className="profile-page">
      <PageHeader eyebrow={t("profile:eyebrow")} title={t("profile:title")} description={t("profile:description")} />

      {loadState === "loading" ? (
        <StatusBanner tone="info">{t("profile:loading")}</StatusBanner>
      ) : null}

      {loadState === "error" ? (
        <div className="profile-load-error">
          <div className="banner banner-error" role="alert">
            <strong>{t("profile:loadFailedTitle")}</strong>
            <p>{t("profile:loadFailedBody")}</p>
          </div>
          <button type="button" className="secondary-action" onClick={() => void loadProfile()}>
            {t("profile:retryLoad")}
          </button>
        </div>
      ) : null}

      {loadState === "ready" ? (
        <>
          <div className="profile-tabs" role="tablist" aria-label={t("profile:sectionsLabel")}>
            {(
              [
                ["personal", t("profile:personalInformation")],
                ["security", t("profile:security")],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                type="button"
                role="tab"
                aria-selected={section === id}
                className={section === id ? "profile-tab profile-tab-active" : "profile-tab"}
                onClick={() => setSection(id)}
              >
                {label}
              </button>
            ))}
          </div>

          {saveError ? <StatusBanner tone="error">{saveError}</StatusBanner> : null}

          {section === "personal" ? (
            <form className="profile-section form-card" onSubmit={onSaveProfile}>
              <h2>{t("profile:personalInformation")}</h2>
              <UserAvatar displayName={displayName || user?.displayName || ""} email={email || user?.email || ""} />
              <Field id="profile-display-name" label={t("auth:fullName")}>
                <input
                  id="profile-display-name"
                  name="displayName"
                  type="text"
                  autoComplete="name"
                  required
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                />
              </Field>
              <div className="profile-email-readonly">
                <span className="field-label">{t("auth:businessEmail")}</span>
                <p className="profile-email-value">{email}</p>
                <p className="profile-email-note">{t("profile:changeEmailDeferred")}</p>
              </div>
              <button className="primary-action" type="submit" disabled={savingProfile || !personalDirty}>
                {savingProfile ? t("common:loading") : t("profile:saveChanges")}
              </button>
            </form>
          ) : null}

          {section === "security" ? (
            <form className="profile-section form-card" onSubmit={onChangePassword}>
              <h2>{t("profile:security")}</h2>
              {!hasLocalCredential ? (
                <StatusBanner tone="info">{t("profile:noLocalCredential")}</StatusBanner>
              ) : (
                <>
                  {passwordError ? <StatusBanner tone="error">{passwordError}</StatusBanner> : null}
                  <label className="field" htmlFor="current-password">
                    <span>{t("profile:currentPassword")}</span>
                    <div className="password-input">
                      <input
                        id="current-password"
                        name="currentPassword"
                        type={currentVisible ? "text" : "password"}
                        autoComplete="current-password"
                        required
                        value={currentPassword}
                        onChange={(event) => setCurrentPassword(event.target.value)}
                      />
                      <button
                        className="password-toggle"
                        type="button"
                        aria-label={currentVisible ? t("auth:hidePassword") : t("auth:showPassword")}
                        onClick={() => setCurrentVisible((visible) => !visible)}
                      >
                        <svg viewBox="0 0 24 24" aria-hidden="true">
                          <path d="M3 12s3.25-5 9-5 9 5 9 5-3.25 5-9 5-9-5-9-5Z" />
                          <circle cx="12" cy="12" r="2.25" />
                        </svg>
                      </button>
                    </div>
                  </label>
                  <PasswordFieldGroup
                    password={newPassword}
                    confirmPassword={confirmNewPassword}
                    onPasswordChange={setNewPassword}
                    onConfirmPasswordChange={setConfirmNewPassword}
                    passwordVisible={newVisible}
                    confirmVisible={confirmVisible}
                    onTogglePasswordVisible={() => setNewVisible((visible) => !visible)}
                    onToggleConfirmVisible={() => setConfirmVisible((visible) => !visible)}
                    passwordId="new-password"
                    confirmId="confirm-new-password"
                    passwordLabel={t("profile:newPassword")}
                    confirmLabel={t("profile:confirmNewPassword")}
                    disabled={savingPassword}
                    showConfirm
                  />
                  <button
                    className="primary-action"
                    type="submit"
                    disabled={savingPassword || !passwordReady || !currentPassword}
                  >
                    {savingPassword ? t("common:loading") : t("profile:changePassword")}
                  </button>
                </>
              )}
            </form>
          ) : null}
        </>
      ) : null}

      <button type="button" className="secondary-action profile-back" onClick={() => navigate(-1)}>
        {t("profile:back")}
      </button>
    </div>
  );
}
