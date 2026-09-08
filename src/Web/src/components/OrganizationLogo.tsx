import { organizationMonogram } from "../tenancy/organizationMonogram";

export function OrganizationLogo({
  name,
  logoUrl,
  className,
}: {
  name: string;
  logoUrl?: string | null;
  className?: string;
}) {
  const classes = ["org-logo", className].filter(Boolean).join(" ");

  if (logoUrl) {
    return <img className={classes} src={logoUrl} alt="" />;
  }

  return (
    <span className={classes} aria-hidden="true">
      {organizationMonogram(name)}
    </span>
  );
}
