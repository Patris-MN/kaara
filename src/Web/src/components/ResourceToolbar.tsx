import { type ReactNode } from "react";

export function ResourceToolbar({
  label,
  summary,
  start,
  children,
}: {
  label: string;
  summary: ReactNode;
  start?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div className="resource-toolbar" role="toolbar" aria-label={label}>
      {start ? <div className="resource-toolbar-start">{start}</div> : null}
      <p className="resource-toolbar-summary">{summary}</p>
      {children ? <div className="resource-toolbar-actions">{children}</div> : null}
    </div>
  );
}
