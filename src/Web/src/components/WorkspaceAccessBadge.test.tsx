import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { WorkspaceAccessBadge } from "./WorkspaceAccessBadge";

describe("WorkspaceAccessBadge", () => {
  it("renders as a non-interactive status badge", () => {
    const { container } = render(<WorkspaceAccessBadge label="Full access" />);

    const badge = container.querySelector(".access-badge");
    expect(badge).toBeTruthy();
    expect(badge?.tagName).toBe("SPAN");
    expect(screen.getByText("Full access")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Full access" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Full access" })).toBeNull();
  });
});
