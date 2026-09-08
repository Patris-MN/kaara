import { type ReactNode } from "react";

export function InfoCallout({
  icon,
  title,
  children,
  action,
}: {
  icon: ReactNode;
  title: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="info-callout" role="status">
      <div className="info-callout-icon" aria-hidden="true">
        {icon}
      </div>
      <div className="info-callout-copy">
        <strong>{title}</strong>
        <div className="info-callout-body">{children}</div>
        {action ? <div className="info-callout-action">{action}</div> : null}
      </div>
    </div>
  );
}
