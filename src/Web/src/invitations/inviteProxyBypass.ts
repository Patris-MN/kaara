type InviteProxyRequest = {
  method?: string;
  headers?: Record<string, string | string[] | undefined>;
};

function headerValue(
  headers: InviteProxyRequest["headers"],
  name: string,
): string {
  const raw = headers?.[name];
  if (Array.isArray(raw)) {
    return raw[0] ?? "";
  }
  return raw ?? "";
}

/**
 * Vite dev-server proxy bypass: browser document navigations to /invite/:token
 * must serve the SPA; XHR/fetch preview calls must reach the backend API.
 */
export function shouldBypassInviteProxyToSpa(req: InviteProxyRequest): boolean {
  if (req.method && req.method.toUpperCase() !== "GET") {
    return false;
  }

  const mode = headerValue(req.headers, "sec-fetch-mode");
  if (mode === "cors" || mode === "no-cors") {
    return false;
  }

  const dest = headerValue(req.headers, "sec-fetch-dest");
  if (dest === "empty" || dest === "script" || dest === "style") {
    return false;
  }

  if (mode === "navigate" || dest === "document") {
    return true;
  }

  const accept = headerValue(req.headers, "accept");
  return accept.includes("text/html");
}
