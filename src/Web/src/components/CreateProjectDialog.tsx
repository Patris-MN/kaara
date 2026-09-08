import { type FormEvent, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import type { ProjectAccentToken } from "../projects/projectIdentity";
import { Dialog } from "./Dialog";
import { useDialogClose } from "./DialogCloseContext";
import { DEFAULT_PROJECT_ACCENT, ProjectMetadataFields } from "./ProjectMetadataFields";

type CreateProjectDialogProps = {
  open: boolean;
  busy: boolean;
  nameError?: string | null;
  formError?: string | null;
  onClose: () => void;
  onSubmit: (payload: { name: string; description: string; accentToken: ProjectAccentToken }) => void;
};

export function CreateProjectDialog({
  open,
  busy,
  nameError,
  formError,
  onClose,
  onSubmit,
}: CreateProjectDialogProps) {
  const { t } = useTranslation(["projects", "common"]);
  const nameRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [accentToken, setAccentToken] = useState<ProjectAccentToken>(DEFAULT_PROJECT_ACCENT);

  useEffect(() => {
    if (!open) {
      return;
    }
    setName("");
    setDescription("");
    setAccentToken(DEFAULT_PROJECT_ACCENT);
    const timer = window.setTimeout(() => nameRef.current?.focus(), 0);
    return () => window.clearTimeout(timer);
  }, [open]);

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

  const isDirty = name.trim() !== "" || description.trim() !== "" || accentToken !== DEFAULT_PROJECT_ACCENT;

  return (
    <Dialog
      open={open}
      titleId="create-project-title"
      title={t("projects:createDialogTitle")}
      closeLabel={t("common:close")}
      onClose={onClose}
      isDirty={isDirty}
      size="compact"
    >
      <CreateProjectForm
        busy={busy}
        nameError={nameError}
        formError={formError}
        name={name}
        description={description}
        accentToken={accentToken}
        nameRef={nameRef}
        onNameChange={setName}
        onDescriptionChange={setDescription}
        onAccentTokenChange={setAccentToken}
        onSubmit={handleSubmit}
      />
    </Dialog>
  );
}

function CreateProjectForm({
  busy,
  nameError,
  formError,
  name,
  description,
  accentToken,
  nameRef,
  onNameChange,
  onDescriptionChange,
  onAccentTokenChange,
  onSubmit,
}: {
  busy: boolean;
  nameError?: string | null;
  formError?: string | null;
  name: string;
  description: string;
  accentToken: ProjectAccentToken;
  nameRef: React.RefObject<HTMLInputElement | null>;
  onNameChange: (value: string) => void;
  onDescriptionChange: (value: string) => void;
  onAccentTokenChange: (value: ProjectAccentToken) => void;
  onSubmit: (event: FormEvent) => void;
}) {
  const { t } = useTranslation(["projects", "common"]);
  const { requestClose } = useDialogClose();

  return (
      <form className="form-card form-card-compact create-project-form" onSubmit={onSubmit}>
        <ProjectMetadataFields
          idPrefix="create-project"
          name={name}
          description={description}
          accentToken={accentToken}
          busy={busy}
          nameError={nameError}
          nameRef={nameRef}
          onNameChange={onNameChange}
          onDescriptionChange={onDescriptionChange}
          onAccentTokenChange={onAccentTokenChange}
        />
        <p className="create-project-access-note">{t("projects:workspaceAccessNote")}</p>
        {formError ? <p className="field-error">{formError}</p> : null}
        <div className="dialog-actions">
          <button type="button" className="secondary-action" onClick={requestClose} disabled={busy}>
            {t("common:cancel")}
          </button>
          <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
            {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
            {busy ? t("projects:creating") : t("projects:createSubmit")}
          </button>
        </div>
      </form>
  );
}
