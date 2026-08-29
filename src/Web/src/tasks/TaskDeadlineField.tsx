import { useTranslation } from "react-i18next";

import { Field } from "../components/Ui";
import { formatTaskDate } from "./presentation";

export function TaskDeadlineField({
  id,
  value,
  onChange,
  disabled,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation("tasks");

  return (
    <Field id={id} label={t("deadline.label")}>
      <div className="task-deadline-control">
        <input
          id={id}
          type="date"
          disabled={disabled}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
        {value ? (
          <button
            type="button"
            className="secondary-action task-deadline-clear"
            disabled={disabled}
            onClick={() => onChange("")}
          >
            {t("deadline.remove")}
          </button>
        ) : null}
        <span className="task-deadline-value">
          {value ? <time dateTime={value}>{formatTaskDate(value)}</time> : t("deadline.none")}
        </span>
      </div>
    </Field>
  );
}
