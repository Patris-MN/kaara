import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useEffect } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import enCommon from "../locales/en/common.json";
import { FeedbackProvider, useFeedback } from "../feedback/FeedbackProvider";

function FeedbackHarness() {
  const { show } = useFeedback();
  return (
    <div>
      <button type="button" onClick={() => show({ tone: "success", title: "Saved", body: "All good." })}>
        Show success
      </button>
      <button type="button" onClick={() => show({ tone: "error", title: "Failed", body: "Try again." })}>
        Show error
      </button>
      <input aria-label="Focus trap probe" />
    </div>
  );
}

function AutoShowSuccess() {
  const { show } = useFeedback();
  useEffect(() => {
    show({ tone: "success", title: "Saved", body: "All good." });
  }, [show]);
  return <input aria-label="Focus trap probe" />;
}

describe("ActionFeedback", () => {
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("renders success and error variants with live-region semantics", async () => {
    render(
      <FeedbackProvider>
        <FeedbackHarness />
      </FeedbackProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Show success" }));
    const success = await screen.findByRole("status");
    expect(success.className).toContain("action-feedback-success");
    expect(screen.getByText("Saved")).toBeTruthy();
    expect(screen.getByText("All good.")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Show error" }));
    const error = await screen.findByRole("alert");
    expect(error.className).toContain("action-feedback-error");
    expect(screen.getByText("Failed")).toBeTruthy();
    expect(screen.getByText("Try again.")).toBeTruthy();
  });

  it("does not steal focus when feedback appears", async () => {
    render(
      <FeedbackProvider>
        <AutoShowSuccess />
      </FeedbackProvider>,
    );

    const probe = screen.getByLabelText("Focus trap probe");
    probe.focus();
    expect(document.activeElement).toBe(probe);
    expect(await screen.findByText("Saved")).toBeTruthy();
    expect(document.activeElement).toBe(probe);
  });

  it("dismisses feedback manually", async () => {
    const user = userEvent.setup();
    render(
      <FeedbackProvider>
        <FeedbackHarness />
      </FeedbackProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Show success" }));
    expect(await screen.findByText("Saved")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enCommon.feedback.dismiss }));
    expect(screen.queryByText("Saved")).toBeNull();
  });

  it("auto-dismisses success feedback after a delay", async () => {
    vi.useFakeTimers();
    await act(async () => {
      render(
        <FeedbackProvider>
          <AutoShowSuccess />
        </FeedbackProvider>,
      );
    });
    expect(screen.getByText("Saved")).toBeTruthy();
    act(() => {
      vi.advanceTimersByTime(5100);
    });
    expect(screen.queryByText("Saved")).toBeNull();
  });

  it("keeps the newest feedback events visible together", async () => {
    render(
      <FeedbackProvider>
        <FeedbackHarness />
      </FeedbackProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Show success" }));
    fireEvent.click(screen.getByRole("button", { name: "Show error" }));
    expect(await screen.findByText("Saved")).toBeTruthy();
    expect(screen.getByText("Failed")).toBeTruthy();
    expect(screen.getByRole("region", { name: enCommon.feedback.region })).toBeTruthy();
  });
});
