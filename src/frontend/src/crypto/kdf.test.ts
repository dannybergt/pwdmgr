import { describe, expect, it } from "vitest";
import { argon2id } from "hash-wasm";
import { fromHex, toHex, utf8Encode } from "./encoding";
import { KDF_DEFAULT, KDF_MINIMUM, deriveKek, isAtLeastMinimum, normalizePassphrase } from "./kdf";

/**
 * Known-answer tests from the Argon2 reference implementation (`src/test.c`, version 0x13).
 * RFC 9106 §5.3 lists only a vector with secret *and* associated data; `hash-wasm` exposes no
 * associated-data parameter and this KDF does not use one, so the reference-suite vectors
 * without K/X are the anchor. They are also the cross-implementation anchor for the .NET
 * agent (TESTING.md).
 */
const REFERENCE_VECTORS = [
  { t: 2, mKib: 65536, p: 1, passphrase: "password", salt: "somesalt",
    tag: "09316115d5cf24ed5a15a31a3ba326e5cf32edc24702987c02b6566f61913cf7" },
  { t: 2, mKib: 256, p: 1, passphrase: "password", salt: "somesalt",
    tag: "9dfeb910e80bad0311fee20f9c0e2b12c17987b4cac90c2ef54d5b3021c68bfe" },
  { t: 2, mKib: 256, p: 2, passphrase: "password", salt: "somesalt",
    tag: "6d093c501fd5999645e0ea3bf620d7b8be7fd2db59c20d9fff9539da2bf57037" },
  { t: 1, mKib: 65536, p: 1, passphrase: "password", salt: "somesalt",
    tag: "f6a5adc1ba723dddef9b5ac1d464e180fcd9dffc9d1cbf76cca2fed795d9ca98" },
  { t: 4, mKib: 65536, p: 1, passphrase: "password", salt: "somesalt",
    tag: "9025d48e68ef7395cca9079da4c4ec3affb3c8911fe4f86d1a2520856f63172c" },
  { t: 2, mKib: 65536, p: 1, passphrase: "differentpassword", salt: "somesalt",
    tag: "0b84d652cf6b0c4beaef0dfe278ba6a80df6696281d7e0d2891b817d8c458fde" },
  { t: 2, mKib: 65536, p: 1, passphrase: "password", salt: "diffsalt",
    tag: "bdf32b05ccc42eb15d58fd19b1f856b113da1e9a5874fdcc544308565aa8141c" }
] as const;

describe("argon2id (hash-wasm) against reference vectors", () => {
  for (const v of REFERENCE_VECTORS) {
    it(`t=${v.t} m=${v.mKib}KiB p=${v.p} "${v.passphrase}"/"${v.salt}"`, async () => {
      const tag = await argon2id({
        password: v.passphrase,
        salt: v.salt,
        iterations: v.t,
        memorySize: v.mKib,
        parallelism: v.p,
        hashLength: 32,
        outputType: "binary"
      });
      expect(toHex(tag)).toBe(v.tag);
    }, 30_000);
  }
});

/**
 * Own vector at the default parameters (m=64 MiB, t=3, p=4). Frozen on first run; a change
 * here means the KDF no longer produces the same KEK for existing users.
 */
const OWN_VECTOR = {
  passphrase: "correct horse battery staple",
  salt: "000102030405060708090a0b0c0d0e0f",
  kek: "853b272a44db1421c02962669a55eb0994f3cab385ed1c4c79253eee19bab49e"
};

describe("deriveKek", () => {
  it("matches the frozen own vector at KDF_DEFAULT", async () => {
    const kek = await deriveKek(OWN_VECTOR.passphrase, fromHex(OWN_VECTOR.salt), KDF_DEFAULT);
    expect(kek).toHaveLength(32);
    expect(toHex(kek)).toBe(OWN_VECTOR.kek);
  }, 30_000);

  it("normalises the passphrase to NFKC before hashing", async () => {
    const composed = "café"; // é as one code point
    const decomposed = "café"; // e + combining acute
    expect(normalizePassphrase(composed)).toEqual(normalizePassphrase(decomposed));
    expect(normalizePassphrase(composed)).toEqual(utf8Encode("café"));
  });

  it("rejects a salt shorter than 16 bytes", async () => {
    await expect(deriveKek("x", new Uint8Array(15), KDF_MINIMUM)).rejects.toThrow(RangeError);
  });

  it("rejects parameters below the enforced minimum", async () => {
    const weak = { ...KDF_MINIMUM, memoryKib: KDF_MINIMUM.memoryKib - 1 };
    expect(isAtLeastMinimum(weak)).toBe(false);
    await expect(deriveKek("x", new Uint8Array(16), weak)).rejects.toThrow(RangeError);
  });

  it("accepts the minimum itself", () => {
    expect(isAtLeastMinimum(KDF_MINIMUM)).toBe(true);
    expect(isAtLeastMinimum(KDF_DEFAULT)).toBe(true);
  });
});
