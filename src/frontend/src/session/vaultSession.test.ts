// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { IDLE_LOCK_MS, current, lock, subscribe, touch, unlock } from "./vaultSession";

const fakeState = { me: { userId: "u", tenantId: "t", tenantSlug: "dev", email: "a@b.c", displayName: "A" }, keyring: {} as never, vault: { id: "v", name: "Personal", key: {} as CryptoKey } };

describe("vaultSession", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    lock();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("locks after the idle timeout and notifies subscribers", () => {
    const seen: boolean[] = [];
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

  it("never writes to browser storage, cookies or IndexedDB", () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const cookie = vi.spyOn(document, "cookie", "set");
    const idb = { open: vi.fn() };
    vi.stubGlobal("indexedDB", idb);
    unlock(fakeState);
    touch();
    lock();
    expect(setItem).not.toHaveBeenCalled();
    expect(cookie).not.toHaveBeenCalled();
    expect(idb.open).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    vi.unstubAllGlobals();
  });
});
