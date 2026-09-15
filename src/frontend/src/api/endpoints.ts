import { api } from "./client";
import type { KeyringWire, Me, SecretSummaryWire, SecretVersionWire, VaultWire } from "./types";

export const auth = {
  login: (tenantSlug: string, email: string, password: string) =>
    api<void>("POST", "/auth/login", { tenantSlug, email, password }),
  logout: () => api<void>("POST", "/auth/logout"),
  me: () => api<Me>("GET", "/auth/me")
};

export const keyring = {
  get: () => api<KeyringWire>("GET", "/me/keyring"),
  enrol: (record: KeyringWire) => api<KeyringWire>("PUT", "/me/keyring", record)
};

export const vaults = {
  list: () => api<VaultWire[]>("GET", "/vaults"),
  create: (id: string, nameCiphertext: string, wrappedVaultKey: string) =>
    api<VaultWire>("POST", "/vaults", { id, type: "personal", nameCiphertext, wrappedVaultKey })
};

export const secrets = {
  list: (vaultId: string) => api<SecretSummaryWire[]>("GET", `/vaults/${vaultId}/secrets`),
  create: (vaultId: string, body: { id: string; type: string; nameCiphertext: string; payloadCiphertext: string; wrappedDek: string; aadHash: string }) =>
    api<SecretVersionWire>("POST", `/vaults/${vaultId}/secrets`, body),
  latest: (secretId: string) => api<SecretVersionWire>("GET", `/secrets/${secretId}/versions/latest`),
  addVersion: (secretId: string, body: { versionNo: number; payloadCiphertext: string; wrappedDek: string; aadHash: string }) =>
    api<SecretVersionWire>("POST", `/secrets/${secretId}/versions`, body),
  remove: (secretId: string) => api<void>("DELETE", `/secrets/${secretId}`)
};
