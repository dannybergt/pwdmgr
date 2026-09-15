import { describe, expect, it } from "vitest";
import { AEAD_NONCE_LENGTH, AEAD_TAG_LENGTH, aeadOpen, aeadSeal, generateAeadKey, importAeadKey } from "./aead";
import { randomBytes, toHex, utf8Decode, utf8Encode } from "./encoding";

const aad = utf8Encode("tenant=t1;vault=v1;secret=s1;version=1;crypto=1");

describe("aead (AES-256-GCM)", () => {
  it("round-trips plaintext with AAD", async () => {
    const key = await generateAeadKey();
    const plaintext = utf8Encode("hunter2 — ünïcödé");
    const blob = await aeadSeal(key, plaintext, aad);
    expect(blob).toHaveLength(AEAD_NONCE_LENGTH + plaintext.length + AEAD_TAG_LENGTH);
    expect(utf8Decode(await aeadOpen(key, blob, aad))).toBe("hunter2 — ünïcödé");
  });

  it("round-trips an empty plaintext", async () => {
    const key = await generateAeadKey();
    const blob = await aeadSeal(key, new Uint8Array(0), aad);
    expect(await aeadOpen(key, blob, aad)).toHaveLength(0);
  });

  it("fails when the AAD differs", async () => {
    const key = await generateAeadKey();
    const blob = await aeadSeal(key, utf8Encode("x"), aad);
    await expect(aeadOpen(key, blob, utf8Encode("tenant=t2"))).rejects.toThrow();
  });

  it("fails when the ciphertext is tampered with", async () => {
    const key = await generateAeadKey();
    const blob = await aeadSeal(key, utf8Encode("hello"), aad);
    blob[AEAD_NONCE_LENGTH] ^= 0x01;
    await expect(aeadOpen(key, blob, aad)).rejects.toThrow();
  });

  it("fails when the tag is tampered with", async () => {
    const key = await generateAeadKey();
    const blob = await aeadSeal(key, utf8Encode("hello"), aad);
    blob[blob.length - 1] ^= 0x80;
    await expect(aeadOpen(key, blob, aad)).rejects.toThrow();
  });

  it("fails with a different key", async () => {
    const blob = await aeadSeal(await generateAeadKey(), utf8Encode("hello"), aad);
    await expect(aeadOpen(await generateAeadKey(), blob, aad)).rejects.toThrow();
  });

  it("rejects blobs shorter than nonce + tag", async () => {
    const key = await generateAeadKey();
    await expect(aeadOpen(key, new Uint8Array(AEAD_NONCE_LENGTH + AEAD_TAG_LENGTH - 1), aad)).rejects.toThrow(RangeError);
  });

  it("imports raw 32-byte keys as non-extractable by default", async () => {
    const key = await importAeadKey(randomBytes(32));
    expect(key.extractable).toBe(false);
    await expect(importAeadKey(randomBytes(16))).rejects.toThrow(RangeError);
  });

  it("uses a fresh nonce per seal (10k distinct)", async () => {
    const key = await generateAeadKey();
    const nonces = new Set<string>();
    for (let i = 0; i < 10_000; i += 1) {
      const blob = await aeadSeal(key, new Uint8Array(0), aad);
      nonces.add(toHex(blob.subarray(0, AEAD_NONCE_LENGTH)));
    }
    expect(nonces.size).toBe(10_000);
  }, 60_000);
});
