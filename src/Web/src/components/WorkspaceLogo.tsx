import { workspaceMonogram } from "../workspaces/workspaceMonogram";

export function WorkspaceLogo({
  name,
  logoUrl,
  className,
}: {
  name: string;
  logoUrl?: string | null;
  className?: string;
}) {
  const classes = ["workspace-logo", className].filter(Boolean).join(" ");

  if (logoUrl) {
    return <img className={classes} src={logoUrl} alt="" />;
  }

  return (
    <span className={classes} aria-hidden="true">
      {workspaceMonogram(name)}
    </span>
  );
}
