import type { Bytes } from "./encoding";

/**
 * HKDF-SHA256 (RFC 5869) via WebCrypto. Used to derive purpose-bound subkeys, e.g. the
 * wrapping key from an X25519 shared secret (slice #3).
 */
export async function hkdfSha256(
  ikm: Bytes,
  salt: Bytes,
  info: Bytes,
  length: number
): Promise<Bytes> {
  if (length <= 0 || length > 255 * 32) {
    throw new RangeError("hkdf output length out of range");
  }
  const key = await crypto.subtle.importKey("raw", ikm, "HKDF", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "HKDF", hash: "SHA-256", salt, info },
    key,
    length * 8
  );
  return new Uint8Array(bits);
}
