import { type ReactNode } from "react";
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
}: {
  id: string;
  label: string;
  children: ReactNode;
  error?: string;
}) {
  const errorId = error ? `${id}-error` : undefined;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children}
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
