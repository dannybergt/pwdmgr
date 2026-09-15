import { type Bytes, concat } from "./encoding";

/**
 * AES-256-GCM with additional authenticated data. Wire format of a sealed blob is
 * `nonce (12 bytes) || ciphertext || tag (16 bytes)`; the AAD is not part of the blob and
 * must be reconstructed by the caller from context (tenant, vault, secret, version).
 */
export const AEAD_KEY_LENGTH = 32;
export const AEAD_NONCE_LENGTH = 12;
export const AEAD_TAG_LENGTH = 16;

export async function importAeadKey(raw: Bytes, extractable = false): Promise<CryptoKey> {
  if (raw.length !== AEAD_KEY_LENGTH) {
    throw new RangeError(`aead key must be ${AEAD_KEY_LENGTH} bytes`);
  }
  return crypto.subtle.importKey("raw", raw, { name: "AES-GCM" }, extractable, ["encrypt", "decrypt"]);
}

export async function generateAeadKey(extractable = false): Promise<CryptoKey> {
  return crypto.subtle.generateKey({ name: "AES-GCM", length: AEAD_KEY_LENGTH * 8 }, extractable, [
    "encrypt",
    "decrypt"
  ]);
}

export async function aeadSeal(key: CryptoKey, plaintext: Bytes, aad: Bytes): Promise<Bytes> {
  const nonce = crypto.getRandomValues(new Uint8Array(AEAD_NONCE_LENGTH));
  const ciphertext = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: AEAD_TAG_LENGTH * 8 },
    key,
    plaintext
  );
  return concat(nonce, new Uint8Array(ciphertext));
}

/**
 * Rejects with `OperationError` (WebCrypto) when the nonce, ciphertext, tag or AAD was
 * tampered with. Callers must not distinguish these cases.
 */
export async function aeadOpen(key: CryptoKey, blob: Bytes, aad: Bytes): Promise<Bytes> {
  if (blob.length < AEAD_NONCE_LENGTH + AEAD_TAG_LENGTH) {
    throw new RangeError("aead blob too short");
  }
  const nonce = blob.subarray(0, AEAD_NONCE_LENGTH);
  const ciphertext = blob.subarray(AEAD_NONCE_LENGTH);
  const plaintext = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: AEAD_TAG_LENGTH * 8 },
    key,
    ciphertext
  );
  return new Uint8Array(plaintext);
}
