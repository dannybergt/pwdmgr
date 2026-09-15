// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../api/types";
import { toBase64 } from "../crypto/encoding";
import { KDF_MINIMUM } from "../crypto/kdf";
import { enrol } from "../crypto/keyring";
import { UnlockPage } from "./UnlockPage";

const me: Me = { userId: "8d1b6a9c-0000-4000-8000-000000000001", tenantId: "t", tenantSlug: "dev", email: "alice@example.test", displayName: "Alice" };

afterEach(() => vi.unstubAllGlobals());

describe("UnlockPage", () => {
  it("rejects a wrong passphrase locally: error shown, no request carries the passphrase", async () => {
    const { record } = await enrol("the right passphrase", me.userId, KDF_MINIMUM);
    const wire = {
      cryptoVersion: 1,
      kdf: { algorithm: "argon2id", ...record.kdfParams },
      kdfSalt: toBase64(record.kdfSalt),
      publicKey: toBase64(record.publicKey),
      encryptedPrivateKey: toBase64(record.encryptedPrivateKey)
    };
    const spy = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/me/keyring")) {
        return new Response(JSON.stringify(wire), { status: 200 });
      }
      if (url.endsWith("/vaults")) {
        return new Response("[]", { status: 200 });
      }
      return new Response("{}", { status: 500 });
    });
    vi.stubGlobal("fetch", spy);

    const onUnlocked = vi.fn();
    render(<UnlockPage me={me} onUnlocked={onUnlocked} onLogout={vi.fn()} />);
    const input = await screen.findByLabelText("Passphrase");
    fireEvent.change(input, { target: { value: "the wrong passphrase" } });
    fireEvent.click(screen.getByRole("button", { name: "Unlock" }));

    expect(await screen.findByRole("alert", {}, { timeout: 20_000 })).toHaveTextContent("Wrong passphrase.");
    expect(onUnlocked).not.toHaveBeenCalled();
    for (const [, init] of spy.mock.calls) {
      expect(String(init?.body ?? "")).not.toContain("passphrase");
    }
    expect(spy.mock.calls.every(([url]) => String(url).endsWith("/me/keyring") || String(url).endsWith("/vaults"))).toBe(true);
  }, 30_000);

  it("switches to enrolment when the account has no keyring and validates the passphrase", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/me/keyring")) {
        return new Response(JSON.stringify({ title: "Not Found", status: 404 }), { status: 404 });
      }
      return new Response("[]", { status: 200 });
    }));
    render(<UnlockPage me={me} onUnlocked={vi.fn()} onLogout={vi.fn()} />);
    expect(await screen.findByRole("heading", { name: "Create your vault passphrase" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Passphrase"), { target: { value: "short" } });
    fireEvent.change(screen.getByLabelText("Repeat passphrase"), { target: { value: "short" } });
    fireEvent.click(screen.getByRole("button", { name: "Create vault" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("at least 12 characters"));
    fireEvent.change(screen.getByLabelText("Passphrase"), { target: { value: "long enough passphrase" } });
    fireEvent.change(screen.getByLabelText("Repeat passphrase"), { target: { value: "long enough passphrasx" } });
    fireEvent.click(screen.getByRole("button", { name: "Create vault" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("differ"));
  });
});
