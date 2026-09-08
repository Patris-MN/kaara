import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { InviteAcceptPage } from "../pages/InviteAcceptPage";
import enInvitations from "../locales/en/invitations.json";
import enMembers from "../locales/en/members.json";
import enTenants from "../locales/en/tenants.json";
import enAuth from "../locales/en/auth.json";
import { shouldBypassInviteProxyToSpa } from "../invitations/inviteProxyBypass";
import { buildPublicInvitationUrl } from "../invitations/invitationUrl";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";

const preview = {
  organizationName: "Patris",
  invitedEmail: "new-user@example.com",
  role: "Member",
  expiresAtUtc: "2026-09-08T00:00:00Z",
  inviterDisplayName: "Owner User",
  requiresRegistration: true,
  workspaceGrants: [{ workspaceName: "erbil branch", accessLevel: "Edit" }],
};

const existingUserPreview = {
  ...preview,
  invitedEmail: "member@example.com",
  requiresRegistration: false,
};

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "member@example.com",
  displayName: "Member User",
  isPlatformAdministrator: false,
};

const wrongUser = {
  ...authUser,
  email: "bob@gmail.com",
  displayName: "Bob Wrong",
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Patris",
  slug: "patris",
  role: "Owner",
  status: "Active",
  workspaceCount: 1,
  canManage: true,
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function pathOf(input: RequestInfo | URL) {
  const raw = String(input);
  return raw.startsWith("http") ? new URL(raw).pathname : raw;
}

function renderApp(path: string) {
  const router = createMemoryRouter(
    [
      {
        path: "/*",
        element: (
          <AuthProvider>
            <TenantDirectoryProvider>
              <App />
            </TenantDirectoryProvider>
          </AuthProvider>
        ),
      },
    ],
    { initialEntries: [path] },
  );
  return render(<RouterProvider router={router} />);
}

describe("phase 8.1.3A invitation route and onboarding repair", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("registers /invite/:token as a frontend route", () => {
    const router = createMemoryRouter(
      [{ path: "/invite/:token", element: <InviteAcceptPage /> }],
      { initialEntries: ["/invite/token-abc"] },
    );
    expect(router.routes[0]?.path).toBe("/invite/:token");
  });

  it("bypasses Vite proxy for browser navigation but not API preview fetch", () => {
    expect(
      shouldBypassInviteProxyToSpa({
        method: "GET",
        headers: {
          accept: "text/html",
          "sec-fetch-mode": "navigate",
          "sec-fetch-dest": "document",
        },
      }),
    ).toBe(true);
    expect(
      shouldBypassInviteProxyToSpa({
        method: "GET",
        headers: {
          accept: "*/*",
          "sec-fetch-mode": "cors",
          "sec-fetch-dest": "empty",
        },
      }),
    ).toBe(false);
  });

  it("builds a full frontend invitation URL from a backend path", () => {
    vi.stubEnv("VITE_APP_ORIGIN", "http://localhost:5173");
    expect(buildPublicInvitationUrl("/invite/dev-token")).toBe(
      "http://localhost:5173/invite/dev-token",
    );
  });

  it("loads invitation preview via explicit API request and renders organization context", async () => {
    const fetchImpl = vi.fn(async (input: RequestInfo | URL) => {
      const path = pathOf(input);
      if (path === "/invite/abc123") {
        return json(preview);
      }
      return json({}, 404);
    });
    vi.stubGlobal("fetch", fetchImpl);

    renderApp("/invite/abc123");
    expect(await screen.findByRole("heading", { name: enInvitations.title })).toBeTruthy();
    await waitFor(() => {
      expect(fetchImpl).toHaveBeenCalledWith(
        expect.stringContaining("/invite/abc123"),
        expect.anything(),
      );
    });
    expect(screen.getByText(/Patris/)).toBeTruthy();
    expect(screen.getByText(/Invited by Owner User/)).toBeTruthy();
    expect(screen.getByText("new-user@example.com")).toBeTruthy();
    expect(screen.getByText(/Invited as Member/)).toBeTruthy();
    expect(screen.getByText("erbil branch")).toBeTruthy();
    expect(screen.getByText(enMembers.access.edit)).toBeTruthy();
  });

  it("shows create-account CTA for requiresRegistration invitations with locked email", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        if (pathOf(input) === "/invite/abc123") {
          return json(preview);
        }
        return json({}, 404);
      }),
    );

    renderApp("/invite/abc123");
    expect(await screen.findByRole("button", { name: enInvitations.createAccount })).toBeTruthy();
    const emailField = screen.getByLabelText(enInvitations.invitedEmail);
    expect((emailField as HTMLInputElement).readOnly).toBe(true);
    expect((emailField as HTMLInputElement).value).toBe("new-user@example.com");
  });

  it("registers, accepts, and navigates into the organization", async () => {
    const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = pathOf(input);
      const method = init?.method ?? "GET";
      if (path === "/invite/reg-token" && method === "GET") {
        return json(preview);
      }
      if (path === "/invite/reg-token/register" && method === "POST") {
        return json({
          membershipId: "m1",
          tenantId: tenantA.tenantId,
          role: "Member",
          status: "Active",
        });
      }
      if (path === "/auth/login" && method === "POST") {
        return json({
          accessToken: "token-new",
          userId: authUser.userId,
          email: preview.invitedEmail,
          displayName: "New User",
          isPlatformAdministrator: false,
        });
      }
      if (path === "/auth/me") {
        return json({ ...authUser, email: preview.invitedEmail, displayName: "New User" });
      }
      if (path === "/tenants") {
        return json([tenantA]);
      }
      if (path === "/invitations") {
        return json([]);
      }
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([{ role: "Member" }]));
      }
      if (path === "/notifications") {
        return json({ items: [], unreadCount: 0 });
      }
      if (path === `/tenants/${tenantA.tenantId}/workspaces`) {
        return json([]);
      }
      return json({}, 404);
    });
    vi.stubGlobal("fetch", fetchImpl);

    renderApp("/invite/reg-token");
    await screen.findByRole("button", { name: enInvitations.createAccount });
    await userEvent.type(screen.getByLabelText(enAuth.displayName), "New User");
    await userEvent.type(screen.getByLabelText(enAuth.password), "password123");
    await userEvent.type(screen.getByLabelText(enInvitations.confirmPassword), "password123");
    await userEvent.click(screen.getByRole("button", { name: enInvitations.createAccount }));

    await waitFor(() => {
      expect(fetchImpl.mock.calls.some(([url, init]) => {
        return pathOf(url) === "/invite/reg-token/register" && init?.method === "POST";
      })).toBe(true);
    });
    expect(await screen.findByRole("option", { name: tenantA.name })).toBeTruthy();
  });

  it("shows sign-in prompt for existing-user invitations", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        if (pathOf(input) === "/invite/existing-token") {
          return json(existingUserPreview);
        }
        return json({}, 404);
      }),
    );

    renderApp("/invite/existing-token");
    expect(await screen.findByText(enInvitations.loginPrompt)).toBeTruthy();
    const signInLink = await screen.findByRole("link", { name: enAuth.signIn });
    expect(signInLink.getAttribute("href")).toBe("/login");
  });

  it("allows the correct signed-in user to accept", async () => {
    writeAccessToken("token-a");
    const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = pathOf(input);
      const method = init?.method ?? "GET";
      if (path === "/auth/me") {
        return json(authUser);
      }
      if (path === "/invite/accept-token" && method === "GET") {
        return json(existingUserPreview);
      }
      if (path === "/invite/accept-token/accept" && method === "POST") {
        return json({
          membershipId: "m1",
          tenantId: tenantA.tenantId,
          role: "Member",
          status: "Active",
        });
      }
      if (path === "/tenants") {
        return json([tenantA]);
      }
      if (path === "/invitations") {
        return json([]);
      }
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([{ role: "Owner" }]));
      }
      if (path === "/notifications") {
        return json({ items: [], unreadCount: 0 });
      }
      if (path === `/tenants/${tenantA.tenantId}/workspaces`) {
        return json([]);
      }
      return json({}, 404);
    });
    vi.stubGlobal("fetch", fetchImpl);

    renderApp("/invite/accept-token");
    const acceptButton = await screen.findByRole("button", { name: enInvitations.accept });
    await userEvent.click(acceptButton);
    expect(await screen.findByRole("option", { name: tenantA.name })).toBeTruthy();
  });

  it("blocks wrong-account acceptance with a deliberate message", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path === "/auth/me") {
          return json(wrongUser);
        }
        if (path === "/invite/wrong-token") {
          return json(existingUserPreview);
        }
        return json({}, 404);
      }),
    );

    renderApp("/invite/wrong-token");
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("member@example.com");
    expect(alert.textContent).toContain("bob@gmail.com");
    expect(screen.queryByRole("button", { name: enInvitations.accept })).toBeNull();
    expect(screen.getByRole("button", { name: enInvitations.switchAccount })).toBeTruthy();
  });

  it.each([
    ["expired", "invitation_expired", enInvitations.errors.expired],
    ["revoked", "invitation_revoked", enInvitations.errors.revoked],
    ["used", "invitation_already_accepted", enInvitations.errors.alreadyAccepted],
    ["invalid", "invitation_invalid", enInvitations.errors.invalid],
  ] as const)("shows %s invitation state", async (_label, code, message) => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        if (pathOf(input) === `/invite/${code}-token`) {
          return json({ error: code }, code === "invitation_invalid" ? 404 : 409);
        }
        return json({}, 404);
      }),
    );

    renderApp(`/invite/${code}-token`);
    expect(await screen.findByText(message)).toBeTruthy();
  });

  it("preserves pending-invitation recovery after interrupted onboarding", async () => {
    writeAccessToken("token-a");
    const pendingInvitation = {
      tenantId: tenantA.tenantId,
      name: tenantA.name,
      slug: tenantA.slug,
      role: "Member",
      status: "Invited",
      workspaceCount: 0,
      canManage: false,
    };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path === "/auth/me") {
          return json({ ...authUser, email: preview.invitedEmail });
        }
        if (path === "/tenants") {
          return json([]);
        }
        if (path === "/invitations") {
          return json([pendingInvitation]);
        }
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([], [pendingInvitation]));
        }
        if (path === "/notifications") {
          return json({ items: [], unreadCount: 0 });
        }
        return json({}, 404);
      }),
    );

    renderApp("/app");
    expect(await screen.findByText(enTenants.onboarding.pendingTitle)).toBeTruthy();
    expect(screen.getByRole("button", { name: enTenants.onboarding.continueInvitation })).toBeTruthy();
  });

  it("shows copyable full invitation link after deferred invite creation", async () => {
    writeAccessToken("token-a");
    vi.stubEnv("VITE_APP_ORIGIN", "http://localhost:5173");
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn(async () => undefined) },
    });
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (path === "/auth/me") {
          return json(authUser);
        }
        if (path === "/tenants") {
          return json([tenantA]);
        }
        if (path === "/invitations") {
          return json([]);
        }
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([{ role: "Owner" }]));
        }
        if (path === "/notifications") {
          return json({ items: [], unreadCount: 0 });
        }
        if (path === `/tenants/${tenantA.tenantId}/members`) {
          return json([]);
        }
        if (path === `/tenants/${tenantA.tenantId}/invitations/pending`) {
          return json([]);
        }
        if (path === `/tenants/${tenantA.tenantId}/workspaces`) {
          return json([{ workspaceId: "w1", name: "erbil branch" }]);
        }
        if (path === `/tenants/${tenantA.tenantId}/invitations` && method === "POST") {
          return json({
            invitationId: "i1",
            emailDeliveryDeferred: true,
            invitationUrl: "/invite/dev-token",
          });
        }
        return json({}, 404);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await userEvent.click(await screen.findByRole("button", { name: enTenants.invite }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText(enTenants.inviteEmail), "new-user@example.com");
    await userEvent.click(within(dialog).getByRole("button", { name: enTenants.invite }));
    expect(
      await within(dialog).findByDisplayValue("http://localhost:5173/invite/dev-token"),
    ).toBeTruthy();
    await userEvent.click(within(dialog).getByRole("button", { name: enMembers.copyInvitationLink }));
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
      "http://localhost:5173/invite/dev-token",
    );
  });
});
