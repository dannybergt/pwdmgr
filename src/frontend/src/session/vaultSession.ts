import type { Me } from "../api/types";
import type { UnlockedKeyring } from "../crypto/keyring";

/**
 * Module-level in-memory state for the unlocked vault. Nothing here is ever written to
 * localStorage, sessionStorage or IndexedDB; a reload or tab close means "locked". The idle
 * timer locks after 10 minutes without user activity.
 */
export interface UnlockedVault {
  readonly id: string;
  readonly name: string;
  readonly key: CryptoKey;
}

export interface UnlockedState {
  readonly me: Me;
  readonly keyring: UnlockedKeyring;
  readonly vault: UnlockedVault;
}

export const IDLE_LOCK_MS = 10 * 60 * 1000;

let state: UnlockedState | null = null;
let idleTimer: ReturnType<typeof setTimeout> | undefined;
const listeners = new Set<() => void>();

function notify(): void {
  for (const listener of listeners) {
    listener();
  }
}

function armIdleTimer(): void {
  if (idleTimer !== undefined) {
    clearTimeout(idleTimer);
  }
  idleTimer = setTimeout(lock, IDLE_LOCK_MS);
}

export function unlock(next: UnlockedState): void {
  state = next;
  armIdleTimer();
  notify();
}

export function lock(): void {
  if (idleTimer !== undefined) {
    clearTimeout(idleTimer);
    idleTimer = undefined;
  }
  // CryptoKey handles cannot be zeroed; dropping every reference is all JS allows (ADR-0006).
  state = null;
  notify();
}

export function current(): UnlockedState | null {
  return state;
}

export function touch(): void {
  if (state) {
    armIdleTimer();
  }
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
