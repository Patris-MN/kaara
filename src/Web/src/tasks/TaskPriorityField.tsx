import { useTranslation } from "react-i18next";

import type { TaskPriority } from "../api/types";
import { Field } from "../components/Ui";
import {
  TASK_PRIORITIES,
  normalizePriority,
  priorityLabelKey,
  priorityMarker,
  priorityToneClass,
} from "./presentation";

export function TaskPriorityBadge({ priority }: { priority: string }) {
  const { t } = useTranslation("tasks");
  const normalized = normalizePriority(priority);
  return (
    <span className={`task-priority-badge ${priorityToneClass(normalized)}`}>
      <span aria-hidden="true">{priorityMarker(normalized)}</span>
      {t(priorityLabelKey(normalized))}
    </span>
  );
}

export function TaskPriorityField({
  id,
  value,
  onChange,
  disabled,
}: {
  id: string;
  value: TaskPriority;
  onChange: (value: TaskPriority) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation("tasks");

  return (
    <Field id={id} label={t("priority.label")}>
      <select
        id={id}
        disabled={disabled}
        value={value}
        onChange={(event) => onChange(event.target.value as TaskPriority)}
      >
        {TASK_PRIORITIES.map((priority) => (
          <option key={priority} value={priority}>
            {priorityMarker(priority)} {t(priorityLabelKey(priority))}
          </option>
        ))}
      </select>
    </Field>
  );
}
