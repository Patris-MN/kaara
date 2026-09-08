import { type FormEvent, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import type { Project } from "../api/types";
import type { ProjectAccentToken } from "../projects/projectIdentity";
import { Dialog } from "./Dialog";
import {
  DEFAULT_PROJECT_ACCENT,
  ProjectMetadataFields,
  readAccentToken,
} from "./ProjectMetadataFields";

type EditProjectDialogProps = {
  open: boolean;
  busy: boolean;
  project: Project | null;
  nameError?: string | null;
  formError?: string | null;
  onClose: () => void;
  onSubmit: (payload: { name: string; description: string; accentToken: ProjectAccentToken }) => void;
};

export function EditProjectDialog({
  open,
  busy,
  project,
  nameError,
  formError,
  onClose,
  onSubmit,
}: EditProjectDialogProps) {
  const { t } = useTranslation(["projects", "common"]);
  const nameRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [accentToken, setAccentToken] = useState<ProjectAccentToken>(DEFAULT_PROJECT_ACCENT);

  useEffect(() => {
    if (!open || !project) {
      return;
    }
    setName(project.name);
    setDescription(project.description ?? "");
    setAccentToken(readAccentToken(project.accentToken));
    const timer = window.setTimeout(() => nameRef.current?.focus(), 0);
    return () => window.clearTimeout(timer);
  }, [open, project]);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy) {
      return;
    }
    onSubmit({
      name: name.trim(),
      description: description.trim(),
      accentToken,
    });
  }

  return (
    <Dialog
      open={open}
      titleId="edit-project-title"
      title={t("projects:editDialogTitle")}
      closeLabel={t("common:close")}
      onClose={onClose}
      size="compact"
    >
      <form className="form-card form-card-compact create-project-form" onSubmit={handleSubmit}>
        <ProjectMetadataFields
          idPrefix="edit-project"
          name={name}
          description={description}
          accentToken={accentToken}
          busy={busy}
          nameError={nameError}
          nameRef={nameRef}
          onNameChange={setName}
          onDescriptionChange={setDescription}
          onAccentTokenChange={setAccentToken}
        />
        {formError ? <p className="field-error">{formError}</p> : null}
        <div className="dialog-actions">
          <button type="button" className="secondary-action" onClick={onClose} disabled={busy}>
            {t("common:cancel")}
          </button>
          <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
            {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
            {busy ? t("projects:saving") : t("projects:saveChanges")}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
