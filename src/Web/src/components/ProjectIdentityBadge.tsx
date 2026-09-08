import { projectAccentClass, projectMonogram } from "../projects/projectIdentity";

export function ProjectIdentityBadge({
  name,
  accentToken,
  className,
}: {
  name: string;
  accentToken?: string | null;
  className?: string;
}) {
  const classes = [projectAccentClass(accentToken), className].filter(Boolean).join(" ");
  return (
    <span className={classes} aria-hidden="true">
      {projectMonogram(name)}
    </span>
  );
}
