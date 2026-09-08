import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { EmptyState } from "./EmptyState";

describe("EmptyState", () => {
  it("wraps the action in a spacing container below the description", () => {
    const { container } = render(
      <EmptyState
        title="No workspaces yet"
        body="Create your first workspace."
        action={<button type="button">New workspace</button>}
      />,
    );

    const actionWrapper = container.querySelector(".empty-state-action");
    expect(actionWrapper).toBeTruthy();
    expect(actionWrapper?.querySelector("button")?.textContent).toBe("New workspace");
  });
});
