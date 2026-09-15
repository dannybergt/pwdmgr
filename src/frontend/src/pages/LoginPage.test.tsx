// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { LoginPage } from "./LoginPage";

function mockFetch(handler: (url: string, init?: RequestInit) => Response) {
  const spy = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => handler(String(input), init));
  vi.stubGlobal("fetch", spy);
  return spy;
}

afterEach(() => vi.unstubAllGlobals());

describe("LoginPage", () => {
  it("shows a generic error on 401 and keeps no session", async () => {
    mockFetch(() => new Response(JSON.stringify({ title: "Invalid credentials", status: 401 }), { status: 401, headers: { "Content-Type": "application/problem+json" } }));
    const onLoggedIn = vi.fn();
    render(<LoginPage onLoggedIn={onLoggedIn} />);
    fireEvent.change(screen.getByLabelText("E-mail"), { target: { value: "alice@example.test" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "wrong-password" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Tenant, e-mail or password is wrong.");
    expect(onLoggedIn).not.toHaveBeenCalled();
  });

  it("logs in with credentials: include and hands over the principal", async () => {
    const spy = mockFetch((url) =>
      url.endsWith("/auth/login")
        ? new Response(null, { status: 204 })
        : new Response(JSON.stringify({ userId: "u", tenantId: "t", tenantSlug: "dev", email: "alice@example.test", displayName: "Alice" }), { status: 200 })
    );
    const onLoggedIn = vi.fn();
    render(<LoginPage onLoggedIn={onLoggedIn} />);
    fireEvent.change(screen.getByLabelText("E-mail"), { target: { value: "alice@example.test" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "correct" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await waitFor(() => expect(onLoggedIn).toHaveBeenCalledWith(expect.objectContaining({ email: "alice@example.test" })));
    const [url, init] = spy.mock.calls[0]!;
    expect(String(url)).toBe("/api/v1/auth/login");
    expect(init?.credentials).toBe("include");
    expect(JSON.parse(String(init?.body))).toEqual({ tenantSlug: "dev", email: "alice@example.test", password: "correct" });
  });

  it("shows the throttle message on 429", async () => {
    mockFetch(() => new Response(JSON.stringify({ title: "Too many login attempts", status: 429 }), { status: 429 }));
    render(<LoginPage onLoggedIn={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("E-mail"), { target: { value: "a@b.c" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Too many attempts");
  });
});
