import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../api/types";
import { toBase64 } from "../crypto/encoding";
import { KDF_MINIMUM } from "../crypto/kdf";
import { enrol } from "../crypto/keyring";
import { unlockWithPassphrase } from "./unlockFlow";

const me: Me = { userId: "8d1b6a9c-0000-4000-8000-000000000001", tenantId: "t", tenantSlug: "dev", email: "a@b.c", displayName: "A" };

afterEach(() => vi.unstubAllGlobals());

describe("unlockWithPassphrase", () => {
  it("creates the personal vault when the keyring exists but no vault does (interrupted enrolment)", async () => {
    const { record } = await enrol("interrupted enrolment passphrase", me.userId, KDF_MINIMUM);
    const wire = {
      cryptoVersion: 1,
      kdf: { algorithm: "argon2id", ...record.kdfParams },
      kdfSalt: toBase64(record.kdfSalt),
      publicKey: toBase64(record.publicKey),
      encryptedPrivateKey: toBase64(record.encryptedPrivateKey)
    };
    const posted: Array<{ url: string; body: unknown }> = [];
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/vaults") && init?.method === "POST") {
        const body = JSON.parse(String(init.body)) as { id: string; type: string; nameCiphertext: string; wrappedVaultKey: string };
        posted.push({ url, body });
        return new Response(JSON.stringify({ ...body, cryptoVersion: 1, keyVersion: 1 }), { status: 201 });
      }
      return new Response("{}", { status: 500 });
    }));

    const state = await unlockWithPassphrase("interrupted enrolment passphrase", me, wire, []);
    expect(state.vault.name).toBe("Personal");
    expect(posted).toHaveLength(1);
    const body = posted[0]!.body as { id: string; type: string; wrappedVaultKey: string };
    expect(body.id).toBe(state.vault.id);
    expect(body.type).toBe("personal");
    expect(body.wrappedVaultKey.length).toBeGreaterThan(100);
  }, 30_000);
});
