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
  }, 30_000);

  it("binds the private key to the user id", async () => {
    const { record } = await enrol("pass pass pass", USER, FAST);
    await expect(unlock("pass pass pass", "other-user", record)).rejects.toBeInstanceOf(KeyringError);
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

  it("uses a fresh ephemeral key per wrap", async () => {
    const alice = await enrol("alice alice", "alice", FAST);
    const vaultKey = generateVaultKey();
    const a = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    const b = await wrapKeyForPublicKey(vaultKey, alice.record.publicKey, "ctx");
    expect(toHex(a.slice(0, 32))).not.toBe(toHex(b.slice(0, 32)));
  }, 30_000);
});

/**
 * Frozen wrap/unwrap vector (deterministic thanks to the injectable ephemeral key). Recipient
 * and ephemeral private keys are RFC 7748 §6.1 test keys (Alice = recipient, Bob = ephemeral).
 * If this ever changes, existing wrapped keys can no longer be opened.
 */
const KAT = {
  recipientPrivatePkcs8: "302e020100300506032b656e04220420" + "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a",
  recipientPublic: "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a",
  ephemeralPrivatePkcs8: "302e020100300506032b656e04220420" + "5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb",
  ephemeralPublic: "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f",
  rawKey: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f",
  context: "vault-key|tenant|vault|alice|v1",
  wrapped: "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4fbb98d7372fbe3c0d0c0f2d25f50816854cde741c566980e54446fce832c1ac13a089c92a435b1d32bed7aa0f5a623d1dc2a884d96bae4a22dbae991c"
};

describe("wrap known-answer vector", () => {
  it("wraps deterministically with an injected ephemeral key and unwraps to the raw key", async () => {
    const recipientPrivate = await crypto.subtle.importKey("pkcs8", fromHex(KAT.recipientPrivatePkcs8), { name: "X25519" }, false, ["deriveBits"]);
    const recipientPublic = fromHex(KAT.recipientPublic);
    const ephemeral: CryptoKeyPair = {
      privateKey: await crypto.subtle.importKey("pkcs8", fromHex(KAT.ephemeralPrivatePkcs8), { name: "X25519" }, false, ["deriveBits"]),
      publicKey: await crypto.subtle.importKey("raw", fromHex(KAT.ephemeralPublic), { name: "X25519" }, true, [])
    };
    // AES-GCM nonce inside aeadSeal is random, so only the ephemeral prefix is deterministic;
    // the frozen vector therefore covers unwrap of a stored blob.
    const wrapped = await wrapKeyForPublicKey(fromHex(KAT.rawKey), recipientPublic, KAT.context, ephemeral);
    expect(toHex(wrapped.slice(0, 32))).toBe(KAT.ephemeralPublic);
    const unlocked = { publicKey: recipientPublic, privateKey: recipientPrivate };
    expect(toHex(await unwrapKeyWithPrivateKey(wrapped, unlocked, KAT.context))).toBe(KAT.rawKey);
    expect(toHex(await unwrapKeyWithPrivateKey(fromHex(KAT.wrapped), unlocked, KAT.context))).toBe(KAT.rawKey);
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
