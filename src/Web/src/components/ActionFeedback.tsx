import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

import type { FeedbackItem } from "../feedback/FeedbackProvider";

const SUCCESS_DISMISS_MS = 5000;
const ERROR_DISMISS_MS = 8000;

export function ActionFeedbackList({
  items,
  onDismiss,
}: {
  items: FeedbackItem[];
  onDismiss: (id: string) => void;
}) {
  const { t } = useTranslation("common");

  if (items.length === 0) {
    return null;
  }

  return (
    <div className="action-feedback-region" role="region" aria-label={t("feedback.region")}>
      {items.map((item) => (
        <ActionFeedbackItem key={item.id} item={item} onDismiss={onDismiss} />
      ))}
    </div>
  );
}

function ActionFeedbackItem({
  item,
  onDismiss,
}: {
  item: FeedbackItem;
  onDismiss: (id: string) => void;
}) {
  const { t } = useTranslation("common");
  const remainingMs = useRef(item.tone === "error" ? ERROR_DISMISS_MS : SUCCESS_DISMISS_MS);
  const startedAt = useRef(0);
  const timer = useRef<number>(0);

  useEffect(() => {
    function arm() {
      startedAt.current = Date.now();
      timer.current = window.setTimeout(() => onDismiss(item.id), remainingMs.current);
    }

    arm();
    return () => window.clearTimeout(timer.current);
  }, [item.id, onDismiss]);

  function pause() {
    window.clearTimeout(timer.current);
    remainingMs.current = Math.max(0, remainingMs.current - (Date.now() - startedAt.current));
  }

  function resume() {
    startedAt.current = Date.now();
    timer.current = window.setTimeout(() => onDismiss(item.id), remainingMs.current);
  }

  const live =
    item.tone === "error"
      ? { role: "alert" as const, "aria-live": "assertive" as const }
      : { role: "status" as const, "aria-live": "polite" as const };

  return (
    <div
      className={`action-feedback action-feedback-${item.tone}`}
      {...live}
      onMouseEnter={pause}
      onMouseLeave={resume}
      onFocus={pause}
      onBlur={resume}
    >
      <span className="action-feedback-icon" aria-hidden="true">
        {item.tone === "success" ? <SuccessMark /> : <ErrorMark />}
      </span>
      <div className="action-feedback-copy">
        <strong>{item.title}</strong>
        {item.body ? <p>{item.body}</p> : null}
      </div>
      <button
        type="button"
        className="action-feedback-dismiss"
        aria-label={t("feedback.dismiss")}
        onClick={() => onDismiss(item.id)}
      >
        ×
      </button>
    </div>
  );
}

function SuccessMark() {
  return (
    <svg className="action-feedback-mark action-feedback-mark-success" viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12.2 2.6 2.6 5.4-5.6" />
    </svg>
  );
}

function ErrorMark() {
  return (
    <svg className="action-feedback-mark action-feedback-mark-error" viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5.2M12 16.2h.01" />
    </svg>
  );
}
