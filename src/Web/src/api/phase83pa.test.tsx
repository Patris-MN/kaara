import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import { AuthProvider } from "../auth/AuthProvider";
import { FeedbackProvider } from "../feedback/FeedbackProvider";
import { UserMenu } from "../components/UserMenu";
import enAuth from "../locales/en/auth.json";
import enProfile from "../locales/en/profile.json";
import { ProfilePage } from "../pages/ProfilePage";
import { RegisterPage } from "../pages/RegisterPage";
import { writeAccessToken } from "../api/session";

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("phase 8.3P-A profile recovery", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    cleanup();
    localStorage.clear();
  });

  it("shows error without editable fallback when profile API fails", async () => {
    writeAccessToken("token-profile-fail");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = String(input);
        if (path.endsWith("/auth/me")) {
          return json({
            userId: "u1",
            email: "user@example.test",
            displayName: "Shell Name",
            isPlatformAdministrator: false,
          });
        }
        if (path.endsWith("/account/profile")) {
          return json({ error: "missing" }, 404);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter>
        <FeedbackProvider>
          <AuthProvider>
            <ProfilePage />
          </AuthProvider>
        </FeedbackProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText(enProfile.loadFailedTitle)).toBeTruthy();
    expect(screen.queryByLabelText(enAuth.fullName)).toBeNull();
    expect(screen.queryByText(enProfile.personalInformation)).toBeNull();
  });

  it("reloads profile after Try again succeeds", async () => {
    writeAccessToken("token-profile-retry");
    let profileCalls = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = String(input);
        if (path.endsWith("/auth/me")) {
          return json({
            userId: "u1",
            email: "user@example.test",
            displayName: "Shell Name",
            isPlatformAdministrator: false,
          });
        }
        if (path.endsWith("/account/profile")) {
          profileCalls += 1;
          if (profileCalls === 1) {
            return json({ error: "missing" }, 404);
          }
          return json({
            email: "user@example.test",
            displayName: "Authoritative Name",
            hasLocalCredential: true,
          });
        }
        return json({ error: "missing" }, 404);
      }),
    );

    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <FeedbackProvider>
          <AuthProvider>
            <ProfilePage />
          </AuthProvider>
        </FeedbackProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText(enProfile.loadFailedTitle)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enProfile.retryLoad }));
    await waitFor(() => {
      expect(screen.getByDisplayValue("Authoritative Name")).toBeTruthy();
    });
    expect(screen.getByRole("tab", { name: enProfile.personalInformation })).toBeTruthy();
    expect(screen.getByRole("tab", { name: enProfile.security })).toBeTruthy();
    expect(screen.queryByRole("tab", { name: /Preferences/i })).toBeNull();
  });

  it("avatar menu exposes only My profile and Sign out", async () => {
    writeAccessToken("token-menu");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        if (String(input).endsWith("/auth/me")) {
          return json({
            userId: "u1",
            email: "user@example.test",
            displayName: "Mohammad",
            isPlatformAdministrator: false,
          });
        }
        return json({});
      }),
    );

    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AuthProvider>
          <UserMenu onSignOut={() => undefined} />
        </AuthProvider>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByRole("button", { expanded: false })).toBeTruthy();
    });
    await user.click(screen.getByRole("button", { expanded: false }));
    expect(screen.getByRole("menuitem", { name: enProfile.myProfile })).toBeTruthy();
    expect(screen.getByRole("menuitem", { name: enAuth.signOut })).toBeTruthy();
    expect(screen.queryByRole("menuitem", { name: /Account & security/i })).toBeNull();
    expect(screen.queryByText(/Language/i)).toBeNull();
  });
});

describe("phase 8.3P-A signup refinement", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    cleanup();
  });

  it("uses Full name, confirm password, and Google after Create account", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        if (String(input).endsWith("/auth/providers")) {
          return json({ google: { available: false } });
        }
        return json({});
      }),
    );
    render(
      <MemoryRouter>
        <FeedbackProvider>
          <AuthProvider>
            <RegisterPage />
          </AuthProvider>
        </FeedbackProvider>
      </MemoryRouter>,
    );

    expect(screen.getByLabelText(enAuth.fullName)).toBeTruthy();
    expect(screen.getByLabelText(enAuth.confirmPassword)).toBeTruthy();
    const submit = screen.getByRole("button", { name: enAuth.register });
    const google = await screen.findByRole("button", { name: /Continue with Google/i });
    expect(submit.compareDocumentPosition(google) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
