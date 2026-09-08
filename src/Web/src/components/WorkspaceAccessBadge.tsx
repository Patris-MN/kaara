/** Non-interactive workspace authorization status — not an action control. */
export function WorkspaceAccessBadge({ label }: { label: string }) {
  return <span className="access-badge">{label}</span>;
}
