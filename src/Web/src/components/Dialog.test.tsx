import { useRef, useState } from "react";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Dialog } from "./Dialog";
import "../i18n";

function FallbackDialog({ onClose = () => undefined }: { onClose?: () => void }) {
  return (
    <Dialog open titleId="fallback-title" title="Fallback" closeLabel="Close" onClose={onClose}>
      <input aria-label="Later field" />
    </Dialog>
  );
}

function NamedFocusDialog() {
  const nameRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(true);
  return (
    <Dialog
      open={open}
      titleId="named-title"
      title="Named"
      closeLabel="Close"
      onClose={() => setOpen(false)}
      initialFocusRef={nameRef}
    >
      <input ref={nameRef} aria-label="Organization name" />
    </Dialog>
  );
}

describe("Dialog", () => {
  afterEach(() => {
    cleanup();
  });

  it("falls back to the first focusable control when no initial target is supplied", () => {
    render(<FallbackDialog />);
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Close" }));
  });

  it("focuses an explicit initial target when it is inside the dialog", () => {
    render(<NamedFocusDialog />);
    expect(document.activeElement).toBe(screen.getByLabelText("Organization name"));
  });

  it("does not close on backdrop click by default", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog open titleId="backdrop-title" title="Backdrop" closeLabel="Close" onClose={onClose}>
        <input aria-label="Name" />
      </Dialog>,
    );
    await user.click(document.querySelector(".dialog-backdrop")!);
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("prompts before closing a dirty dialog", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog open isDirty titleId="dirty-title" title="Dirty" closeLabel="Close" onClose={onClose}>
        <input aria-label="Name" defaultValue="changed" />
      </Dialog>,
    );
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText("Discard changes?")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Discard changes" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("closes on Escape and restores focus to the opener", async () => {
    const user = userEvent.setup();
    function Host() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Open
          </button>
          <Dialog open={open} titleId="restore-title" title="Restore" closeLabel="Close" onClose={() => setOpen(false)}>
            <p>Body</p>
          </Dialog>
        </>
      );
    }

    render(<Host />);
    const opener = screen.getByRole("button", { name: "Open" });
    await user.click(opener);
    expect(await screen.findByRole("dialog")).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(opener);
  });
});
