import { keyring as keyringApi, vaults as vaultsApi } from "../api/endpoints";
import type { KeyringWire, Me, VaultWire } from "../api/types";
import { aeadOpen, aeadSeal } from "../crypto/aead";
import { type Bytes, fromBase64, toBase64, utf8Decode, utf8Encode } from "../crypto/encoding";
import { KDF_DEFAULT } from "../crypto/kdf";
import {
  CRYPTO_VERSION,
  type KeyringRecord,
  type UnlockedKeyring,
  enrol,
  generateVaultKey,
  importVaultKey,
  unlock as unlockKeyring,
  unwrapKeyWithPrivateKey,
  vaultKeyContext,
  wrapKeyForPublicKey
} from "../crypto/keyring";
import type { UnlockedState, UnlockedVault } from "../session/vaultSession";

export const MIN_PASSPHRASE_LENGTH = 12;

function toWire(record: KeyringRecord): KeyringWire {
  return {
    cryptoVersion: record.cryptoVersion,
    kdf: { algorithm: "argon2id", ...record.kdfParams },
    kdfSalt: toBase64(record.kdfSalt),
    publicKey: toBase64(record.publicKey),
    encryptedPrivateKey: toBase64(record.encryptedPrivateKey)
  };
}

function fromWire(wire: KeyringWire): KeyringRecord {
  return {
    cryptoVersion: wire.cryptoVersion,
    kdfParams: { memoryKib: wire.kdf.memoryKib, iterations: wire.kdf.iterations, parallelism: wire.kdf.parallelism },
    kdfSalt: fromBase64(wire.kdfSalt),
    publicKey: fromBase64(wire.publicKey),
    encryptedPrivateKey: fromBase64(wire.encryptedPrivateKey)
  };
}

function vaultNameAad(tenantId: string, vaultId: string): Bytes {
  return utf8Encode(`vault-name|${tenantId}|${vaultId}|v${CRYPTO_VERSION}`);
}

/** Enrolment: keyring, then a personal vault whose key is wrapped for the user's own public key. */
export async function enrolAndCreateVault(passphrase: string, me: Me): Promise<UnlockedState> {
  const { record, unlocked } = await enrol(passphrase, me.userId, KDF_DEFAULT);
  await keyringApi.enrol(toWire(record));

  const vaultId = crypto.randomUUID();
  const rawVaultKey = generateVaultKey();
  const vaultKey = await importVaultKey(rawVaultKey);
  const wrapped = await wrapKeyForPublicKey(rawVaultKey, unlocked.publicKey, vaultKeyContext(me.tenantId, vaultId, me.userId));
  rawVaultKey.fill(0);
  const nameCiphertext = await aeadSeal(vaultKey, utf8Encode("Personal"), vaultNameAad(me.tenantId, vaultId));
  await vaultsApi.create(vaultId, toBase64(nameCiphertext), toBase64(wrapped));
  return { me, keyring: unlocked, vault: { id: vaultId, name: "Personal", key: vaultKey } };
}

/** Unlock: derive the KEK, open the private key, unwrap the first vault. Throws KeyringError on a wrong passphrase — before any network call. */
export async function unlockWithPassphrase(passphrase: string, me: Me, wire: KeyringWire, vaultList: VaultWire[]): Promise<UnlockedState> {
  const unlocked = await unlockKeyring(passphrase, me.userId, fromWire(wire));
  const first = vaultList[0];
  if (!first) {
    throw new Error("no vault");
  }
  const vault = await openVault(first, me, unlocked);
  return { me, keyring: unlocked, vault };
}

export async function openVault(wire: VaultWire, me: Me, unlocked: UnlockedKeyring): Promise<UnlockedVault> {
  const rawVaultKey = await unwrapKeyWithPrivateKey(fromBase64(wire.wrappedVaultKey), unlocked, vaultKeyContext(me.tenantId, wire.id, me.userId));
  const key = await importVaultKey(rawVaultKey);
  rawVaultKey.fill(0);
  const name = utf8Decode(await aeadOpen(key, fromBase64(wire.nameCiphertext), vaultNameAad(me.tenantId, wire.id)));
  return { id: wire.id, name, key };
}
