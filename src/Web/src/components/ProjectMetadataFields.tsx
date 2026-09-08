import { type RefObject } from "react";
import { useTranslation } from "react-i18next";

import {
  DEFAULT_PROJECT_ACCENT,
  PROJECT_ACCENT_TOKENS,
  type ProjectAccentToken,
  projectAccentClass,
  projectMonogram,
  resolveProjectAccent,
} from "../projects/projectIdentity";
import { Field } from "./Ui";

type ProjectMetadataFieldsProps = {
  idPrefix: string;
  name: string;
  description: string;
  accentToken: ProjectAccentToken;
  busy: boolean;
  nameError?: string | null;
  nameRef?: RefObject<HTMLInputElement | null>;
  onNameChange: (value: string) => void;
  onDescriptionChange: (value: string) => void;
  onAccentTokenChange: (token: ProjectAccentToken) => void;
};

export function ProjectMetadataFields({
  idPrefix,
  name,
  description,
  accentToken,
  busy,
  nameError,
  nameRef,
  onNameChange,
  onDescriptionChange,
  onAccentTokenChange,
}: ProjectMetadataFieldsProps) {
  const { t } = useTranslation(["projects"]);
  const previewInitials = name.trim() ? projectMonogram(name.trim()) : "PR";

  return (
    <>
      <Field
        id={`${idPrefix}-name`}
        label={t("projects:name")}
        error={nameError ?? undefined}
        required
      >
        <input
          id={`${idPrefix}-name`}
          ref={nameRef}
          type="text"
          required
          value={name}
          disabled={busy}
          placeholder={t("projects:namePlaceholder")}
          onChange={(event) => onNameChange(event.target.value)}
        />
      </Field>

      <Field id={`${idPrefix}-description`} label={t("projects:descriptionLabel")}>
        <textarea
          id={`${idPrefix}-description`}
          className="create-project-description"
          rows={3}
          value={description}
          disabled={busy}
          placeholder={t("projects:descriptionPlaceholder")}
          onChange={(event) => onDescriptionChange(event.target.value)}
        />
      </Field>

      <fieldset className="create-project-color-fieldset">
        <legend>{t("projects:colorLabel")}</legend>
        <div className="project-accent-swatches" role="radiogroup" aria-label={t("projects:colorLabel")}>
          {PROJECT_ACCENT_TOKENS.map((token) => (
            <label key={token} className="project-accent-swatch-label">
              <input
                type="radio"
                name={`${idPrefix}-accent`}
                value={token}
                checked={accentToken === token}
                disabled={busy}
                onChange={() => onAccentTokenChange(token)}
              />
              <span
                className={`project-accent-swatch project-accent-swatch-${token}`}
                aria-hidden="true"
              />
              <span className="sr-only">{t(`projects:accents.${token}`)}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="create-project-preview" aria-live="polite">
        <span className="create-project-preview-label">{t("projects:identityPreviewHint")}</span>
        <span className={projectAccentClass(accentToken)} aria-hidden="true">
          {previewInitials}
        </span>
        <span className="sr-only">
          {t("projects:identityPreviewHint")}: {previewInitials}
        </span>
      </div>
    </>
  );
}

export function readAccentToken(value?: string | null): ProjectAccentToken {
  return resolveProjectAccent(value);
}

export { DEFAULT_PROJECT_ACCENT };
