import { describe, expect, it } from "vitest";
import { fromHex, toHex, utf8Decode, utf8Encode } from "./encoding";
import { KDF_MINIMUM } from "./kdf";
import {
  KeyringError,
  enrol,
  generateVaultKey,
  importVaultKey,
  openSecretPayload,
  sealSecretPayload,
  secretAad,
  unlock,
  unwrapDek,
  unwrapKeyWithPrivateKey,
  vaultKeyContext,
  wrapDek,
  wrapKeyForPublicKey
} from "./keyring";

// Tests use the minimum KDF parameters to keep the suite fast; the KDF itself is covered in kdf.test.ts.
const FAST = KDF_MINIMUM;
const USER = "8d1b6a9c-0000-4000-8000-000000000001";

describe("keyring enrol / lock / unlock", () => {
  it("round-trips: enrol, unlock with the same passphrase, same public key", async () => {
    const { record, unlocked } = await enrol("hunter2 hunter2", USER, FAST);
    expect(record.publicKey).toHaveLength(32);
    expect(record.kdfSalt).toHaveLength(16);
    expect(record.cryptoVersion).toBe(1);
    expect(unlocked.privateKey.extractable).toBe(false);

    const again = await unlock("hunter2 hunter2", USER, record);
    expect(toHex(again.publicKey)).toBe(toHex(record.publicKey));
    expect(again.privateKey.extractable).toBe(false);

    // Both unlocked states derive the same shared secret → same private key.
    const vaultKey = generateVaultKey();
    const wrapped = await wrapKeyForPublicKey(vaultKey, record.publicKey, "ctx");
    expect(toHex(await unwrapKeyWithPrivateKey(wrapped, again, "ctx"))).toBe(toHex(vaultKey));
    expect(toHex(await unwrapKeyWithPrivateKey(wrapped, unlocked, "ctx"))).toBe(toHex(vaultKey));
  }, 30_000);

  it("rejects a wrong passphrase with KeyringError and without key material", async () => {
    const { record } = await enrol("right passphrase", USER, FAST);
    const err = await unlock("wrong passphrase", USER, record).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(KeyringError);
    expect(String(err)).not.toMatch(/[0-9a-f]{32}/);
    expect((err as Error).cause).toBeUndefined();
  }, 30_000);

  it("binds the private key to the user id", async () => {
    const { record } = await enrol("pass pass pass", USER, FAST);
    await expect(unlock("pass pass pass", "other-user", record)).rejects.toBeInstanceOf(KeyringError);
  }, 30_000);

  it("rejects a substituted public key (server-side key substitution)", async () => {
    const alice = await enrol("alice alice", USER, FAST);
    const mallory = await enrol("mallory mallory", USER, FAST);
    const swapped = { ...alice.record, publicKey: mallory.record.publicKey };
    const err = await unlock("alice alice", USER, swapped).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(KeyringError);
    expect((err as Error).cause).toBeUndefined();
  }, 30_000);

  it("refuses an unknown crypto version", async () => {
    const { record } = await enrol("pass pass pass", USER, FAST);
    await expect(unlock("pass pass pass", USER, { ...record, cryptoVersion: 2 })).rejects.toThrow(/crypto version/);
  }, 30_000);
});

describe("vault key wrapping", () => {
  it("is bound to the recipient: another key pair cannot unwrap", async () => {
    const alice = await enrol("alice alice", "alice", FAST);
    const bob = await enrol("bob bob bob", "bob", FAST);
    const vaultKey = generateVaultKey();
    const forAlice = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    await expect(unwrapKeyWithPrivateKey(forAlice, bob.unlocked, "ctx")).rejects.toBeInstanceOf(KeyringError);
    expect(toHex(await unwrapKeyWithPrivateKey(forAlice, alice.unlocked, "ctx"))).toBe(toHex(vaultKey));
  }, 30_000);

  it("is bound to the context (AAD) and detects tampering", async () => {
    const alice = await enrol("alice alice", "alice", FAST);
    const vaultKey = generateVaultKey();
    const wrapped = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, vaultKeyContext("t", "v", "alice"));
    await expect(unwrapKeyWithPrivateKey(wrapped, alice.unlocked, vaultKeyContext("t", "v", "mallory"))).rejects.toBeInstanceOf(KeyringError);
    const tampered = wrapped.slice();
    tampered[tampered.length - 1] ^= 0x01;
    await expect(unwrapKeyWithPrivateKey(tampered, alice.unlocked, vaultKeyContext("t", "v", "alice"))).rejects.toBeInstanceOf(KeyringError);
    const ephTampered = wrapped.slice();
    ephTampered[0] ^= 0x01;
    await expect(unwrapKeyWithPrivateKey(ephTampered, alice.unlocked, vaultKeyContext("t", "v", "alice"))).rejects.toBeInstanceOf(KeyringError);
  }, 30_000);

  it("rejects a low-order ephemeral point, a short blob and a wrong raw key length as KeyringError", async () => {
    const alice = await enrol("alice alice", "alice", FAST);
    const vaultKey = generateVaultKey();
    const wrapped = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    const zeroPoint = wrapped.slice();
    zeroPoint.fill(0, 0, 32);
    await expect(unwrapKeyWithPrivateKey(zeroPoint, alice.unlocked, "ctx")).rejects.toBeInstanceOf(KeyringError);
    await expect(unwrapKeyWithPrivateKey(wrapped.slice(0, 40), alice.unlocked, "ctx")).rejects.toBeInstanceOf(KeyringError);
    await expect(wrapKeyForPublicKey(new Uint8Array(16), alice.record.publicKey, "ctx")).rejects.toBeInstanceOf(KeyringError);
    await expect(wrapKeyForPublicKey(vaultKey, new Uint8Array(32), "ctx")).rejects.toBeInstanceOf(KeyringError);
  }, 30_000);

  it("uses a fresh ephemeral key per wrap", async () => {
    const alice = await enrol("alice alice", "alice", FAST);
    const vaultKey = generateVaultKey();
    const a = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    const b = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    expect(toHex(a.slice(0, 32))).not.toBe(toHex(b.slice(0, 32)));
  }, 30_000);
});

/**
 * Frozen unwrap vector. Recipient = RFC 7748 §6.1 "Alice" key pair, the blob was produced with
 * "Bob" as the ephemeral key (private scalar 5dab087e…88e0eb) so a second implementation can
 * reproduce shared → HKDF(info) → AES-GCM. If this ever changes, existing wrapped keys can no
 * longer be opened.
 */
const KAT = {
  recipientPrivatePkcs8: "302e020100300506032b656e04220420" + "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a",
  recipientPublic: "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a",
  ephemeralPublic: "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f",
  rfc7748DeriveBitsOutput: "4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742",
  rawKey: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
  context: "vault-key|tenant|vault|alice|v1",
  wrapped: "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4fbb98d7372fbe3c0d0c0f2d25f50816854cde741c566980e54446fce832c1ac13a089c92a435b1d32bed7aa0f5a623d1dc2a884d96bae4a22dbae991c"
};

describe("known-answer vectors", () => {
  it("X25519 in WebCrypto reproduces the RFC 7748 §6.1 shared secret", async () => {
    const alicePrivate = await crypto.subtle.importKey("pkcs8", fromHex(KAT.recipientPrivatePkcs8), { name: "X25519" }, false, ["deriveBits"]);
    const bobPublic = await crypto.subtle.importKey("raw", fromHex(KAT.ephemeralPublic), { name: "X25519" }, true, []);
    const shared = new Uint8Array(await crypto.subtle.deriveBits({ name: "X25519", public: bobPublic }, alicePrivate, 256));
    expect(toHex(shared)).toBe(KAT.rfc7748DeriveBitsOutput);
  });

  it("unwraps the frozen blob to the raw key", async () => {
    const recipientPrivate = await crypto.subtle.importKey("pkcs8", fromHex(KAT.recipientPrivatePkcs8), { name: "X25519" }, false, ["deriveBits"]);
    const unlocked = { publicKey: fromHex(KAT.recipientPublic), privateKey: recipientPrivate };
    expect(toHex(fromHex(KAT.wrapped).slice(0, 32))).toBe(KAT.ephemeralPublic);
    expect(toHex(await unwrapKeyWithPrivateKey(fromHex(KAT.wrapped), unlocked, KAT.context))).toBe(KAT.rawKey);
    await expect(unwrapKeyWithPrivateKey(fromHex(KAT.wrapped), unlocked, "vault-key|tenant|vault|bob|v1")).rejects.toBeInstanceOf(KeyringError);
  });
});

describe("secret payload", () => {
  it("seals with a fresh DEK and opens with the vault key and matching AAD", async () => {
    const vaultKey = await importVaultKey(generateVaultKey());
    const aad = secretAad("t", "v", "s", 1);
    const { payloadCiphertext, wrappedDek } = await sealSecretPayload(utf8Encode('{"password":"hunter2"}'), vaultKey, aad);
    expect(utf8Decode(await openSecretPayload(payloadCiphertext, wrappedDek, vaultKey, aad))).toBe('{"password":"hunter2"}');
    await expect(openSecretPayload(payloadCiphertext, wrappedDek, vaultKey, secretAad("t", "v", "s", 2))).rejects.toBeInstanceOf(KeyringError);
    const other = await importVaultKey(generateVaultKey());
    await expect(openSecretPayload(payloadCiphertext, wrappedDek, other, aad)).rejects.toBeInstanceOf(KeyringError);
  });

  it("DEK wrap/unwrap round-trips and rejects a foreign vault key", async () => {
    const vaultKey = await importVaultKey(generateVaultKey());
    const dek = generateVaultKey();
    const aad = secretAad("t", "v", "s", 1);
    expect(toHex(await unwrapDek(await wrapDek(dek, vaultKey, aad), vaultKey, aad))).toBe(toHex(dek));
    await expect(unwrapDek(await wrapDek(dek, vaultKey, aad), await importVaultKey(generateVaultKey()), aad)).rejects.toBeInstanceOf(KeyringError);
  });
});
