import {
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from "react";

import type { MemberMenuItem } from "../members/memberActions";

type ContextMenuProps = {
  label: string;
  items: MemberMenuItem[];
};

export function ContextMenu({ label, items }: ContextMenuProps) {
  const menuId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [focusIndex, setFocusIndex] = useState(0);

  useLayoutEffect(() => {
    if (!open || items.length === 0 || !panelRef.current || !triggerRef.current) {
      return;
    }
    const panel = panelRef.current;
    panel.style.insetBlockStart = "";
    panel.style.insetBlockEnd = "";
    panel.style.insetInlineStart = "";
    panel.style.insetInlineEnd = "0";

    const triggerRect = triggerRef.current.getBoundingClientRect();
    const panelRect = panel.getBoundingClientRect();

    if (panelRect.bottom > window.innerHeight - 8) {
      panel.style.insetBlockStart = "auto";
      panel.style.insetBlockEnd = "calc(100% + 6px)";
    }

    const nextRect = panel.getBoundingClientRect();
    if (nextRect.left < 8) {
      panel.style.insetInlineEnd = "auto";
      panel.style.insetInlineStart = "0";
    } else if (nextRect.right > window.innerWidth - 8) {
      panel.style.insetInlineStart = "auto";
      panel.style.insetInlineEnd = "0";
    }

    if (triggerRect.top < 0) {
      panel.style.insetBlockEnd = "";
      panel.style.insetBlockStart = "calc(100% + 6px)";
    }

    const firstItem = panel.querySelector<HTMLElement>('[role="menuitem"]');
    firstItem?.focus();
  }, [open, items.length]);

  useEffect(() => {
    if (!open) {
      return;
    }
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const item = panelRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]')[focusIndex];
    item?.focus();
  }, [focusIndex, open]);

  if (items.length === 0) {
    return null;
  }

  function activateItem(item: MemberMenuItem) {
    setOpen(false);
    triggerRef.current?.focus();
    item.onClick();
  }

  function onPanelKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setFocusIndex((current) => (current + 1) % items.length);
      return;
    }
    if (event.key === "ArrowUp") {
      event.preventDefault();
      setFocusIndex((current) => (current - 1 + items.length) % items.length);
      return;
    }
    if (event.key === "Home") {
      event.preventDefault();
      setFocusIndex(0);
      return;
    }
    if (event.key === "End") {
      event.preventDefault();
      setFocusIndex(items.length - 1);
    }
  }

  function toggleMenu() {
    setOpen((current) => {
      if (!current) {
        setFocusIndex(0);
      }
      return !current;
    });
  }

  return (
    <div className="context-menu" ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className="context-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        aria-label={label}
        onClick={toggleMenu}
      >
        <span aria-hidden="true">⋯</span>
      </button>
      {open ? (
        <div
          ref={panelRef}
          className="context-menu-panel"
          id={menuId}
          role="menu"
          aria-label={label}
          onKeyDown={onPanelKeyDown}
        >
          {items.map((item, index) => (
            <div key={item.id} role="none">
              {item.separatorBefore ? <div className="context-menu-separator" role="separator" /> : null}
              <button
                type="button"
                role="menuitem"
                tabIndex={index === focusIndex ? 0 : -1}
                className={
                  item.destructive ? "context-menu-item context-menu-item-destructive" : "context-menu-item"
                }
                onClick={() => activateItem(item)}
              >
                {item.label}
              </button>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}
