import { secrets as secretsApi } from "../api/endpoints";
import type { SecretSummaryWire } from "../api/types";
import { aeadOpen, aeadSeal } from "../crypto/aead";
import { type Bytes, fromBase64, toBase64, utf8Decode, utf8Encode } from "../crypto/encoding";
import { CRYPTO_VERSION, openSecretPayload, sealSecretPayload, secretAad } from "../crypto/keyring";
import type { UnlockedState } from "../session/vaultSession";

/** Payload of the `password` template; stored only as ciphertext. */
export interface PasswordPayload {
  username: string;
  password: string;
  url: string;
  notes: string;
}

export interface SecretListItem {
  id: string;
  name: string;
  type: string;
  latestVersionNo: number;
}

function nameAad(tenantId: string, vaultId: string, secretId: string): Bytes {
  return utf8Encode(`secret-name|${tenantId}|${vaultId}|${secretId}|v${CRYPTO_VERSION}`);
}

async function sha256(bytes: Bytes): Promise<Bytes> {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)) as Bytes;
}

export async function listSecrets(state: UnlockedState): Promise<SecretListItem[]> {
  const rows = await secretsApi.list(state.vault.id);
  const items: SecretListItem[] = [];
  for (const row of rows) {
    items.push({ id: row.id, name: await decryptName(state, row), type: row.type, latestVersionNo: row.latestVersionNo });
  }
  return items;
}

async function decryptName(state: UnlockedState, row: SecretSummaryWire): Promise<string> {
  return utf8Decode(await aeadOpen(state.vault.key, fromBase64(row.nameCiphertext), nameAad(state.me.tenantId, state.vault.id, row.id)));
}

export async function createSecret(state: UnlockedState, name: string, payload: PasswordPayload): Promise<string> {
  // The secret id is chosen client-side so the AAD can bind it before the server sees it.
  const secretId = crypto.randomUUID();
  const aad = secretAad(state.me.tenantId, state.vault.id, secretId, 1);
  const { payloadCiphertext, wrappedDek } = await sealSecretPayload(utf8Encode(JSON.stringify(payload)), state.vault.key, aad);
  const nameCiphertext = await aeadSeal(state.vault.key, utf8Encode(name), nameAad(state.me.tenantId, state.vault.id, secretId));
  await secretsApi.create(state.vault.id, {
    id: secretId,
    type: "password",
    nameCiphertext: toBase64(nameCiphertext),
    payloadCiphertext: toBase64(payloadCiphertext),
    wrappedDek: toBase64(wrappedDek),
    aadHash: toBase64(await sha256(aad))
  });
  return secretId;
}

export async function readSecret(state: UnlockedState, secretId: string): Promise<PasswordPayload> {
  const version = await secretsApi.latest(secretId);
  const aad = secretAad(state.me.tenantId, state.vault.id, secretId, version.versionNo);
  const expectedHash = toBase64(await sha256(aad));
  if (expectedHash !== version.aadHash) {
    throw new Error("secret metadata does not match its ciphertext (tampered or moved)");
  }
  const plaintext = await openSecretPayload(fromBase64(version.payloadCiphertext), fromBase64(version.wrappedDek), state.vault.key, aad);
  return JSON.parse(utf8Decode(plaintext)) as PasswordPayload;
}

export async function deleteSecret(secretId: string): Promise<void> {
  await secretsApi.remove(secretId);
}
