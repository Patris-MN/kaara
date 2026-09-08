import { useTranslation } from "react-i18next";

import {
  evaluatePassword,
  MIN_PASSWORD_LENGTH,
  passwordsMatch,
  type PasswordValidation,
} from "./passwordPolicy";

type PasswordFieldGroupProps = {
  password: string;
  confirmPassword?: string;
  onPasswordChange: (value: string) => void;
  onConfirmPasswordChange?: (value: string) => void;
  passwordVisible: boolean;
  confirmVisible?: boolean;
  onTogglePasswordVisible: () => void;
  onToggleConfirmVisible?: () => void;
  passwordId?: string;
  confirmId?: string;
  passwordLabel?: string;
  confirmLabel?: string;
  passwordAutoComplete?: "new-password" | "current-password";
  confirmAutoComplete?: "new-password";
  disabled?: boolean;
  showConfirm?: boolean;
  showRequirements?: boolean;
  showStrength?: boolean;
};

export function PasswordFieldGroup({
  password,
  confirmPassword = "",
  onPasswordChange,
  onConfirmPasswordChange,
  passwordVisible,
  confirmVisible = false,
  onTogglePasswordVisible,
  onToggleConfirmVisible,
  passwordId = "password",
  confirmId = "confirmPassword",
  passwordLabel,
  confirmLabel,
  passwordAutoComplete = "new-password",
  confirmAutoComplete = "new-password",
  disabled = false,
  showConfirm = false,
  showRequirements = true,
  showStrength = true,
}: PasswordFieldGroupProps) {
  const { t } = useTranslation("auth");
  const passwordTouched = password.length > 0;
  const confirmTouched = confirmPassword.length > 0;
  const validation = evaluatePassword(password, passwordTouched);
  const match = showConfirm ? passwordsMatch(password, confirmPassword, confirmTouched) : null;

  return (
    <div className="password-field-group">
      <label className="field" htmlFor={passwordId}>
        <span>{passwordLabel ?? t("password")}</span>
        <div className="password-input">
          <input
            id={passwordId}
            name={passwordId}
            type={passwordVisible ? "text" : "password"}
            autoComplete={passwordAutoComplete}
            required={passwordAutoComplete === "new-password"}
            minLength={passwordAutoComplete === "new-password" ? MIN_PASSWORD_LENGTH : undefined}
            value={password}
            disabled={disabled}
            onChange={(event) => onPasswordChange(event.target.value)}
          />
          <button
            className="password-toggle"
            type="button"
            aria-label={passwordVisible ? t("hidePassword") : t("showPassword")}
            aria-pressed={passwordVisible}
            disabled={disabled}
            onClick={onTogglePasswordVisible}
          >
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <path d="M3 12s3.25-5 9-5 9 5 9 5-3.25 5-9 5-9-5-9-5Z" />
              <circle cx="12" cy="12" r="2.25" />
            </svg>
          </button>
        </div>
      </label>

      {showRequirements ? (
        <PasswordRequirements validation={validation} hasTyped={passwordTouched} />
      ) : null}

      {showStrength && passwordTouched ? (
        <p className={`password-strength password-strength-${validation.strength}`}>
          {t(`passwordStrength.${validation.strength}`)}
        </p>
      ) : null}

      {showConfirm ? (
        <>
          <label className="field" htmlFor={confirmId}>
            <span>{confirmLabel ?? t("confirmPassword")}</span>
            <div className="password-input">
              <input
                id={confirmId}
                name={confirmId}
                type={confirmVisible ? "text" : "password"}
                autoComplete={confirmAutoComplete}
                required
                value={confirmPassword}
                disabled={disabled}
                onChange={(event) => onConfirmPasswordChange?.(event.target.value)}
              />
              <button
                className="password-toggle"
                type="button"
                aria-label={confirmVisible ? t("hidePassword") : t("showPassword")}
                aria-pressed={confirmVisible}
                disabled={disabled}
                onClick={() => onToggleConfirmVisible?.()}
              >
                <svg viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M3 12s3.25-5 9-5 9 5 9 5-3.25 5-9 5-9-5-9-5Z" />
                  <circle cx="12" cy="12" r="2.25" />
                </svg>
              </button>
            </div>
          </label>
          {match === false ? (
            <p className="field-error" role="alert">
              {t("passwordMismatch")}
            </p>
          ) : null}
          {match === true ? (
            <p className="field-hint field-hint-success">{t("passwordMatch")}</p>
          ) : null}
        </>
      ) : null}
    </div>
  );
}

function PasswordRequirements({
  validation,
  hasTyped,
}: {
  validation: PasswordValidation;
  hasTyped: boolean;
}) {
  const { t } = useTranslation("auth");

  return (
    <div className="password-requirements" aria-live="polite">
      <p className="password-requirements-title">{t("passwordRequirementsTitle")}</p>
      <ul>
        {validation.requirements.map((requirement) => {
          const state = !hasTyped ? "neutral" : requirement.satisfied ? "valid" : "invalid";
          return (
            <li key={requirement.id} className={`password-requirement-item password-requirement-${state}`}>
              <span className="password-requirement-icon" aria-hidden="true">
                {!hasTyped ? "○" : requirement.satisfied ? "✓" : "✕"}
              </span>
              <span>{t(requirement.labelKey)}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function isRegistrationPasswordReady(
  password: string,
  confirmPassword: string,
): boolean {
  const validation = evaluatePassword(password, password.length > 0);
  const match = passwordsMatch(password, confirmPassword, confirmPassword.length > 0);
  return validation.valid && match === true;
}
