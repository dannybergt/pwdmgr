export interface Me {
  userId: string;
  tenantId: string;
  tenantSlug: string;
  email: string;
  displayName: string;
}

export interface KeyringWire {
  cryptoVersion: number;
  kdf: { algorithm: string; memoryKib: number; iterations: number; parallelism: number };
  kdfSalt: string;
  publicKey: string;
  encryptedPrivateKey: string;
}

export interface VaultWire {
  id: string;
  type: string;
  nameCiphertext: string;
  cryptoVersion: number;
  keyVersion: number;
  wrappedVaultKey: string;
}

export interface SecretSummaryWire {
  id: string;
  vaultId: string;
  type: string;
  nameCiphertext: string;
  latestVersionNo: number;
  createdAt: string;
  updatedAt: string | null;
}

export interface SecretVersionWire {
  secretId: string;
  vaultId: string;
  versionNo: number;
  payloadCiphertext: string;
  wrappedDek: string;
  aadHash: string;
  cryptoVersion: number;
  createdAt: string;
}
