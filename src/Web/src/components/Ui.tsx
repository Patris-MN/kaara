import { Children, cloneElement, isValidElement, type ReactNode } from "react";
import { Link } from "react-router-dom";

export function StatusBanner({
  tone,
  children,
}: {
  tone: "error" | "info" | "success";
  children: string;
}) {
  return (
    <p className={`banner banner-${tone}`} role={tone === "error" ? "alert" : "status"}>
      {children}
    </p>
  );
}

export function Field({
  id,
  label,
  children,
  error,
  hint,
  required,
}: {
  id: string;
  label: string;
  children: ReactNode;
  error?: string;
  hint?: string;
  required?: boolean;
}) {
  const errorId = error ? `${id}-error` : undefined;
  const hintId = hint ? `${id}-hint` : undefined;
  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {required ? <span aria-hidden="true"> *</span> : null}
      </label>
      {hint ? (
        <p id={hintId} className="field-hint">
          {hint}
        </p>
      ) : null}
      {Children.map(children, (child) => {
        if (!isValidElement<{ id?: string; "aria-invalid"?: boolean; "aria-describedby"?: string }>(child)) {
          return child;
        }
        if (child.props.id !== id) {
          return child;
        }
        return cloneElement(child, {
          "aria-invalid": error ? true : child.props["aria-invalid"],
          "aria-describedby": [errorId, hintId, child.props["aria-describedby"]].filter(Boolean).join(" ") || undefined,
        });
      })}
      {error ? (
        <p id={errorId} className="field-error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function TextLink({ to, children }: { to: string; children: string }) {
  return (
    <Link className="text-link" to={to}>
      {children}
    </Link>
  );
}
