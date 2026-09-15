import { AEAD_KEY_LENGTH, aeadOpen, aeadSeal, importAeadKey } from "./aead";
import { type Bytes, concat, randomBytes, toHex, utf8Encode, wipe } from "./encoding";
import { hkdfSha256 } from "./hkdf";
import { KDF_DEFAULT, KDF_SALT_LENGTH, type KdfParams, deriveKek } from "./kdf";

/**
 * Key hierarchy (product plan §9/§10, ADR-0009):
 *
 *   passphrase ─Argon2id─▶ KEK ─AES-GCM─▶ user private key (X25519)
 *   vault key (random 32 B) ─wrapped for a public key (X25519 + HKDF + AES-GCM)─▶ WrappedKey
 *   secret DEK (random 32 B) ─AES-GCM with vault key─▶ SecretVersion.wrapped_dek
 *   payload ─AES-GCM with DEK + AAD(tenant, vault, secret, version, crypto_version)─▶ ciphertext
 *
 * Everything here runs in the browser; the server only ever sees the `Bytes` blobs.
 */
export const CRYPTO_VERSION = 1;

export const X25519_PUBLIC_KEY_LENGTH = 32;

const X25519 = { name: "X25519" } as const;

/** Thrown on any failure that a caller may show to the user; never carries key material. */
export class KeyringError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "KeyringError";
  }
}

/** Server-stored keyring metadata (`user_keyrings` row in slice #5). */
export interface KeyringRecord {
  readonly cryptoVersion: number;
  readonly kdfParams: KdfParams;
  readonly kdfSalt: Bytes;
  readonly publicKey: Bytes;
  readonly encryptedPrivateKey: Bytes;
}

/** In-memory state after unlock. The private key is a non-extractable CryptoKey. */
export interface UnlockedKeyring {
  readonly publicKey: Bytes;
  readonly privateKey: CryptoKey;
}

function privateKeyAad(userId: string, cryptoVersion: number): Bytes {
  return utf8Encode(`pwdmgr/v${cryptoVersion}/user-private-key|${userId}`);
}

async function importPublicKey(raw: Bytes): Promise<CryptoKey> {
  if (raw.length !== X25519_PUBLIC_KEY_LENGTH) {
    throw new KeyringError("invalid public key length");
  }
  return crypto.subtle.importKey("raw", raw, X25519, true, []);
}

async function importPrivateKey(pkcs8: Bytes, extractable: boolean): Promise<CryptoKey> {
  return crypto.subtle.importKey("pkcs8", pkcs8, X25519, extractable, ["deriveBits"]);
}

/**
 * First-time enrolment: fresh salt, fresh X25519 pair, private key sealed under the KEK.
 * Returns the record to store server-side and the unlocked state for immediate use.
 */
export async function enrol(
  passphrase: string,
  userId: string,
  kdfParams: KdfParams = KDF_DEFAULT
): Promise<{ record: KeyringRecord; unlocked: UnlockedKeyring }> {
  const kdfSalt = randomBytes(KDF_SALT_LENGTH);
  const kek = await deriveKek(passphrase, kdfSalt, kdfParams);
  try {
    const pair = (await crypto.subtle.generateKey(X25519, true, ["deriveBits"])) as CryptoKeyPair;
    const publicKey = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey)) as Bytes;
    const pkcs8 = new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey)) as Bytes;
    try {
      const kekKey = await importAeadKey(kek);
      const encryptedPrivateKey = await aeadSeal(kekKey, pkcs8, privateKeyAad(userId, CRYPTO_VERSION));
      const privateKey = await importPrivateKey(pkcs8, false);
      return {
        record: { cryptoVersion: CRYPTO_VERSION, kdfParams, kdfSalt, publicKey, encryptedPrivateKey },
        unlocked: { publicKey, privateKey }
      };
    } finally {
      wipe(pkcs8);
    }
  } finally {
    wipe(kek);
  }
}

/** Re-derives the KEK and opens the private key. Wrong passphrase → `KeyringError`. */
export async function unlock(passphrase: string, userId: string, record: KeyringRecord): Promise<UnlockedKeyring> {
  if (record.cryptoVersion !== CRYPTO_VERSION) {
    throw new KeyringError(`unsupported crypto version ${record.cryptoVersion}`);
  }
  const kek = await deriveKek(passphrase, record.kdfSalt, record.kdfParams);
  let pkcs8: Bytes | undefined;
  try {
    const kekKey = await importAeadKey(kek);
    try {
      pkcs8 = await aeadOpen(kekKey, record.encryptedPrivateKey, privateKeyAad(userId, record.cryptoVersion));
    } catch {
      throw new KeyringError("wrong passphrase or corrupted keyring");
    }
    const privateKey = await importPrivateKey(pkcs8, false);
    return { publicKey: record.publicKey, privateKey };
  } finally {
    wipe(kek);
    if (pkcs8) {
      wipe(pkcs8);
    }
  }
}

function wrapInfo(ephemeralPublic: Bytes, recipientPublic: Bytes, cryptoVersion: number): Bytes {
  return utf8Encode(`pwdmgr/v${cryptoVersion}/wrap|${toHex(ephemeralPublic)}|${toHex(recipientPublic)}`);
}

async function deriveWrapKey(privateKey: CryptoKey, peerPublic: Bytes, info: Bytes): Promise<CryptoKey> {
  const peer = await importPublicKey(peerPublic);
  const shared = new Uint8Array(await crypto.subtle.deriveBits({ name: "X25519", public: peer }, privateKey, 256)) as Bytes;
  // X25519 is unbound to the actual peer; bind the derived key to both public keys via HKDF info.
  const wrapKey = await hkdfSha256(shared, new Uint8Array(0), info, AEAD_KEY_LENGTH);
  wipe(shared);
  try {
    return await importAeadKey(wrapKey);
  } finally {
    wipe(wrapKey);
  }
}

/**
 * Wraps a raw symmetric key (vault key) for the holder of `recipientPublicKey`:
 * `ephemeralPublic(32) || nonce || ciphertext || tag`. `context` is the AAD, e.g.
 * `vault|<tenant>|<vault>|<recipient user>`, and must be reproduced on unwrap.
 * `ephemeral` is injectable for known-answer tests only.
 */
export async function wrapKeyForPublicKey(
  rawKey: Bytes,
  recipientPublicKey: Bytes,
  context: string,
  ephemeral?: CryptoKeyPair
): Promise<Bytes> {
  if (rawKey.length !== AEAD_KEY_LENGTH) {
    throw new KeyringError("raw key must be 32 bytes");
  }
  const pair = ephemeral ?? ((await crypto.subtle.generateKey(X25519, false, ["deriveBits"])) as CryptoKeyPair);
  const ephemeralPublic = new Uint8Array(await crypto.subtle.exportKey("raw", pair.publicKey)) as Bytes;
  const wrapKey = await deriveWrapKey(pair.privateKey, recipientPublicKey, wrapInfo(ephemeralPublic, recipientPublicKey, CRYPTO_VERSION));
  const sealed = await aeadSeal(wrapKey, rawKey, utf8Encode(context));
  return concat(ephemeralPublic, sealed);
}

/** Inverse of {@link wrapKeyForPublicKey} with the recipient's unlocked keyring. */
export async function unwrapKeyWithPrivateKey(wrapped: Bytes, keyring: UnlockedKeyring, context: string): Promise<Bytes> {
  if (wrapped.length < X25519_PUBLIC_KEY_LENGTH + 12 + 16) {
    throw new KeyringError("wrapped key too short");
  }
  const ephemeralPublic = wrapped.slice(0, X25519_PUBLIC_KEY_LENGTH);
  const sealed = wrapped.slice(X25519_PUBLIC_KEY_LENGTH);
  const wrapKey = await deriveWrapKey(keyring.privateKey, ephemeralPublic, wrapInfo(ephemeralPublic, keyring.publicKey, CRYPTO_VERSION));
  try {
    return await aeadOpen(wrapKey, sealed, utf8Encode(context));
  } catch {
    throw new KeyringError("cannot unwrap key: wrong recipient, context or corrupted data");
  }
}

/** A fresh vault key, returned raw so it can be wrapped; import it with {@link importVaultKey} and wipe the raw bytes. */
export function generateVaultKey(): Bytes {
  return randomBytes(AEAD_KEY_LENGTH);
}

/** Vault keys are held as non-extractable CryptoKeys while unlocked. */
export function importVaultKey(raw: Bytes): Promise<CryptoKey> {
  return importAeadKey(raw, false);
}

export function vaultKeyContext(tenantId: string, vaultId: string, recipientUserId: string): string {
  return `vault-key|${tenantId}|${vaultId}|${recipientUserId}|v${CRYPTO_VERSION}`;
}

/** AAD for a secret version's payload and its wrapped DEK. */
export function secretAad(tenantId: string, vaultId: string, secretId: string, version: number): Bytes {
  return utf8Encode(`secret|${tenantId}|${vaultId}|${secretId}|${version}|v${CRYPTO_VERSION}`);
}

/** Per-secret-version data-encryption key wrapped under the vault key. */
export async function wrapDek(dek: Bytes, vaultKey: CryptoKey, aad: Bytes): Promise<Bytes> {
  return aeadSeal(vaultKey, dek, aad);
}

export async function unwrapDek(wrappedDek: Bytes, vaultKey: CryptoKey, aad: Bytes): Promise<Bytes> {
  try {
    return await aeadOpen(vaultKey, wrappedDek, aad);
  } catch {
    throw new KeyringError("cannot unwrap data key");
  }
}

/** Encrypts a payload under a fresh DEK; returns both blobs for the server. */
export async function sealSecretPayload(
  plaintext: Bytes,
  vaultKey: CryptoKey,
  aad: Bytes
): Promise<{ payloadCiphertext: Bytes; wrappedDek: Bytes }> {
  const dek = randomBytes(AEAD_KEY_LENGTH);
  try {
    const dekKey = await importAeadKey(dek);
    const payloadCiphertext = await aeadSeal(dekKey, plaintext, aad);
    const wrappedDek = await wrapDek(dek, vaultKey, aad);
    return { payloadCiphertext, wrappedDek };
  } finally {
    wipe(dek);
  }
}

export async function openSecretPayload(
  payloadCiphertext: Bytes,
  wrappedDek: Bytes,
  vaultKey: CryptoKey,
  aad: Bytes
): Promise<Bytes> {
  const dek = await unwrapDek(wrappedDek, vaultKey, aad);
  try {
    const dekKey = await importAeadKey(dek);
    return await aeadOpen(dekKey, payloadCiphertext, aad);
  } catch {
    throw new KeyringError("cannot open secret payload");
  } finally {
    wipe(dek);
  }
}
