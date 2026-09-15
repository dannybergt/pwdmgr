// Byte/string helpers shared by the crypto modules. Everything that crosses the
// API boundary is Base64 (standard alphabet, padded); everything internal is `Bytes`.

/** A byte view over a plain (non-shared) ArrayBuffer, as WebCrypto's BufferSource requires. */
export type Bytes = Uint8Array<ArrayBuffer>;

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder("utf-8", { fatal: true });

export function utf8Encode(text: string): Bytes {
  return textEncoder.encode(text);
}

export function utf8Decode(bytes: Uint8Array): string {
  return textDecoder.decode(bytes);
}

export function toBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary);
}

const BASE64_PATTERN = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

export function fromBase64(text: string): Bytes {
  if (!BASE64_PATTERN.test(text)) {
    throw new TypeError("invalid base64 input");
  }
  const binary = atob(text);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

export function toHex(bytes: Uint8Array): string {
  let hex = "";
  for (const byte of bytes) {
    hex += byte.toString(16).padStart(2, "0");
  }
  return hex;
}

export function fromHex(text: string): Bytes {
  if (text.length % 2 !== 0 || /[^0-9a-fA-F]/.test(text)) {
    throw new TypeError("invalid hex input");
  }
  const bytes = new Uint8Array(text.length / 2);
  for (let i = 0; i < bytes.length; i += 1) {
    bytes[i] = parseInt(text.slice(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

export function concat(...parts: Uint8Array[]): Bytes {
  const total = parts.reduce((sum, part) => sum + part.length, 0);
  const out = new Uint8Array(total);
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

export function randomBytes(length: number): Bytes {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return bytes;
}

/** Constant-time comparison for equal-length byte arrays. */
export function timingSafeEqual(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) {
    return false;
  }
  let diff = 0;
  for (let i = 0; i < a.length; i += 1) {
    diff |= a[i] ^ b[i];
  }
  return diff === 0;
}

/** Overwrite key material in place once it is no longer needed. */
export function wipe(bytes: Uint8Array): void {
  bytes.fill(0);
}
