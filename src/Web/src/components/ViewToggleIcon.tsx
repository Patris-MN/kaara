export function ViewToggleIcon({ name }: { name: "grid" | "list" }) {
  return (
    <svg className="view-toggle-icon" viewBox="0 0 16 16" aria-hidden="true">
      {name === "grid" ? (
        <>
          <rect x="1.5" y="1.5" width="5.5" height="5.5" rx="1" />
          <rect x="9" y="1.5" width="5.5" height="5.5" rx="1" />
          <rect x="1.5" y="9" width="5.5" height="5.5" rx="1" />
          <rect x="9" y="9" width="5.5" height="5.5" rx="1" />
        </>
      ) : (
        <path d="M2 3.5h12M2 8h12M2 12.5h12" />
      )}
    </svg>
  );
}
