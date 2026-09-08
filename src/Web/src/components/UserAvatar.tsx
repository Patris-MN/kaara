import { organizationMonogram } from "../tenancy/organizationMonogram";

export function UserAvatar({
  displayName,
  email,
  imageUrl,
  className,
}: {
  displayName: string;
  email: string;
  imageUrl?: string | null;
  className?: string;
}) {
  const label = displayName || email;
  const monogram = organizationMonogram(label);

  if (imageUrl?.trim()) {
    return (
      <img
        className={["entity-avatar", className].filter(Boolean).join(" ")}
        src={imageUrl}
        alt=""
        aria-hidden="true"
      />
    );
  }

  return (
    <span className={["entity-monogram", className].filter(Boolean).join(" ")} aria-hidden="true">
      {monogram}
    </span>
  );
}
