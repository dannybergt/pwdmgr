import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { IDLE_LOCK_MS, current, lock, subscribe, touch, unlock } from "./vaultSession";

const fakeState = { me: { userId: "u", tenantId: "t", tenantSlug: "dev", email: "a@b.c", displayName: "A" }, keyring: {} as never, vault: { id: "v", name: "Personal", key: {} as CryptoKey } };

describe("vaultSession", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    lock();
    vi.useRealTimers();
  });

  it("locks after the idle timeout and notifies subscribers", () => {
    const seen: (boolean)[] = [];
    const off = subscribe(() => seen.push(current() !== null));
    unlock(fakeState);
    expect(current()).not.toBeNull();
    vi.advanceTimersByTime(IDLE_LOCK_MS - 1000);
    expect(current()).not.toBeNull();
    vi.advanceTimersByTime(1000);
    expect(current()).toBeNull();
    expect(seen).toEqual([true, false]);
    off();
  });

  it("activity resets the idle timer", () => {
    unlock(fakeState);
    vi.advanceTimersByTime(IDLE_LOCK_MS - 1000);
    touch();
    vi.advanceTimersByTime(IDLE_LOCK_MS - 1000);
    expect(current()).not.toBeNull();
    vi.advanceTimersByTime(1000);
    expect(current()).toBeNull();
  });

  it("never touches browser storage", () => {
    unlock(fakeState);
    expect(Object.keys(globalThis).some((k) => k === "localStorage")).toBe(false);
  });
});
