import { type ReactNode } from "react";

export function EmptyState({
  title,
  body,
  secondary,
  action,
  compact = false,
  icon = "default",
}: {
  title: string;
  body?: string;
  secondary?: string;
  action?: ReactNode;
  compact?: boolean;
  icon?: "default" | "project";
}) {
  const iconGlyph = icon === "project" ? "▣" : "◇";

  return (
    <div className={compact ? "empty-state empty-state-compact" : "empty-state"}>
      <span className="empty-state-icon" aria-hidden="true">
        {iconGlyph}
      </span>
      <strong>{title}</strong>
      {body ? <p>{body}</p> : null}
      {secondary ? <p className="empty-state-secondary">{secondary}</p> : null}
      {action ? <div className="empty-state-action">{action}</div> : null}
    </div>
  );
}
