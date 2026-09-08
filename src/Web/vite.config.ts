import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

import { shouldBypassInviteProxyToSpa } from "./src/invitations/inviteProxyBypass.ts";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/auth": { target: "http://localhost:5227", changeOrigin: true },
      "/tenants": { target: "http://localhost:5227", changeOrigin: true },
      "/invitations": { target: "http://localhost:5227", changeOrigin: true },
      "/invite": {
        target: "http://localhost:5227",
        changeOrigin: true,
        bypass(req) {
          if (shouldBypassInviteProxyToSpa(req)) {
            return "/index.html";
          }
        },
      },
      "/notifications": { target: "http://localhost:5227", changeOrigin: true },
      "/account": { target: "http://localhost:5227", changeOrigin: true },
      "/health": { target: "http://localhost:5227", changeOrigin: true },
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    setupFiles: ["src/test/setup.ts"],
  },
});
