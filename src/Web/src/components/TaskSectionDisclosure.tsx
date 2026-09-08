export function TaskSectionIcon({ name }: { name: "comments" | "activity" | "chevron" }) {
  return (
    <svg className="task-section-icon" viewBox="0 0 16 16" aria-hidden="true">
      {name === "comments" ? (
        <path d="M3 3.5h10a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H6l-3 2.5V4.5a1 1 0 0 1 1-1Z" />
      ) : name === "activity" ? (
        <>
          <circle cx="8" cy="8" r="5.5" />
          <path d="M8 4.5V8l2.5 1.5" />
        </>
      ) : (
        <path d="M4 6l4 4 4-4" />
      )}
    </svg>
  );
}

type TaskSectionDisclosureProps = {
  label: string;
  expanded: boolean;
  onToggle: () => void;
  icon: "comments" | "activity";
  count?: number;
  showLabel: string;
  hideLabel: string;
};

export function TaskSectionDisclosure({
  label,
  expanded,
  onToggle,
  icon,
  count,
  showLabel,
  hideLabel,
}: TaskSectionDisclosureProps) {
  const title = count !== undefined ? `${label} (${count})` : label;
  return (
    <button
      type="button"
      className="task-section-toggle"
      aria-expanded={expanded}
      onClick={onToggle}
    >
      <span className="task-section-toggle-start">
        <TaskSectionIcon name={icon} />
        <span className="task-section-title">{title}</span>
      </span>
      <span className="task-section-toggle-end">
        <span>{expanded ? hideLabel : showLabel}</span>
        <TaskSectionIcon name="chevron" />
      </span>
    </button>
  );
}
