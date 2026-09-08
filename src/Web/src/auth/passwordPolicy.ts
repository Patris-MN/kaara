export const MIN_PASSWORD_LENGTH = 8;

export type PasswordRequirementId = "minLength";

export type PasswordRequirement = {
  id: PasswordRequirementId;
  labelKey: string;
  satisfied: boolean;
};

export type PasswordValidation = {
  valid: boolean;
  requirements: PasswordRequirement[];
  strength: "none" | "weak" | "fair" | "strong";
};

export function isUuidLike(value: string | null | undefined): boolean {
  if (!value) {
    return false;
  }
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value.trim());
}

export function evaluatePassword(password: string, hasTyped: boolean): PasswordValidation {
  const minLength = password.length >= MIN_PASSWORD_LENGTH;
  const requirements: PasswordRequirement[] = [
    {
      id: "minLength",
      labelKey: "passwordRequirements.minLength",
      satisfied: hasTyped && minLength,
    },
  ];

  let strength: PasswordValidation["strength"] = "none";
  if (hasTyped && password.length > 0) {
    if (password.length < MIN_PASSWORD_LENGTH) {
      strength = "weak";
    } else if (password.length < 12) {
      strength = "fair";
    } else {
      strength = "strong";
    }
  }

  return {
    valid: minLength,
    requirements,
    strength,
  };
}

export function passwordsMatch(password: string, confirmPassword: string, hasTypedConfirm: boolean): boolean | null {
  if (!hasTypedConfirm) {
    return null;
  }
  return password === confirmPassword;
}
