/**
 * Same-origin JSON client. Cookies carry the session (ADR-0008); nothing secret ever travels
 * here — only ciphertext blobs and metadata. A 401 anywhere means the server-side session is
 * gone (expired, revoked, disabled); listeners send the user back to /login.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly errors: Record<string, string[]> = {}
  ) {
    super(title);
    this.name = "ApiError";
  }
}

type Listener = () => void;
const unauthorizedListeners = new Set<Listener>();

export function onUnauthorized(listener: Listener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

export async function api<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1${path}`, {
    method,
    credentials: "include",
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (response.status === 401 && path !== "/auth/login") {
    for (const listener of unauthorizedListeners) {
      listener();
    }
  }
  if (response.status === 204) {
    return undefined as T;
  }
  const text = await response.text();
  const json: unknown = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    const problem = (json ?? {}) as { title?: string; errors?: Record<string, string[]> };
    throw new ApiError(response.status, problem.title ?? response.statusText, problem.errors ?? {});
  }
  return json as T;
}
