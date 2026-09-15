import { argon2id } from "hash-wasm";
import { type Bytes, utf8Encode } from "./encoding";

/**
 * Argon2id parameters as stored in the user's keyring metadata. `memoryKib` is the
 * memory cost in KiB (Argon2 `m`), `iterations` the time cost (`t`), `parallelism` the
 * number of lanes (`p`).
 */
export interface KdfParams {
  readonly memoryKib: number;
  readonly iterations: number;
  readonly parallelism: number;
}

export const KEK_LENGTH = 32;
export const KDF_SALT_LENGTH = 16;

/**
 * Lower bound the server enforces on stored parameters (slice #5). Prevents a
 * downgrade attack via tampered keyring metadata. OWASP's first recommended Argon2id
 * configuration (m=19 MiB, t=2, p=1) is the floor.
 */
export const KDF_MINIMUM: KdfParams = {
  memoryKib: 19 * 1024,
  iterations: 2,
  parallelism: 1
};

/** Default for new enrolments. Decided by benchmark, see ADR-0006. */
export const KDF_DEFAULT: KdfParams = {
  memoryKib: 64 * 1024,
  iterations: 3,
  parallelism: 4
};

export function isAtLeastMinimum(params: KdfParams): boolean {
  return (
    params.memoryKib >= KDF_MINIMUM.memoryKib &&
    params.iterations >= KDF_MINIMUM.iterations &&
    params.parallelism >= KDF_MINIMUM.parallelism
  );
}

/**
 * Normalises a passphrase before hashing so that the same visible string typed on
 * different platforms (composed vs. decomposed Unicode) yields the same key.
 */
export function normalizePassphrase(passphrase: string): Bytes {
  return utf8Encode(passphrase.normalize("NFKC"));
}

/**
 * Derives the 32-byte key-encryption key (KEK) from a passphrase. The KEK never leaves
 * the client; it only ever wraps the user's private key (slice #3).
 */
export async function deriveKek(
  passphrase: string,
  salt: Bytes,
  params: KdfParams = KDF_DEFAULT
): Promise<Bytes> {
  if (salt.length < KDF_SALT_LENGTH) {
    throw new RangeError(`salt must be at least ${KDF_SALT_LENGTH} bytes`);
  }
  if (!isAtLeastMinimum(params)) {
    throw new RangeError("kdf parameters below the enforced minimum");
  }
  const password = normalizePassphrase(passphrase);
  try {
    // hash-wasm returns a view over its own freshly allocated ArrayBuffer.
    return (await argon2id({
      password,
      salt,
      memorySize: params.memoryKib,
      iterations: params.iterations,
      parallelism: params.parallelism,
      hashLength: KEK_LENGTH,
      outputType: "binary"
    })) as Bytes;
  } finally {
    password.fill(0);
  }
}
