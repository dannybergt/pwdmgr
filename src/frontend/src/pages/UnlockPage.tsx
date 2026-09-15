import { type FormEvent, useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { auth, keyring as keyringApi, vaults as vaultsApi } from "../api/endpoints";
import type { KeyringWire, Me, VaultWire } from "../api/types";
import { MIN_PASSPHRASE_LENGTH } from "../app/passphraseRules";
import { enrolAndCreateVault, unlockWithPassphrase } from "../app/unlockFlow";
import { KeyringError } from "../crypto/keyring";
import type { UnlockedState } from "../session/vaultSession";

type Mode = { kind: "loading" } | { kind: "enrol" } | { kind: "unlock"; keyring: KeyringWire; vaults: VaultWire[] };

// Browser autofill hints: a new passphrase at enrolment, the existing one at unlock.
const AUTOCOMPLETE = { enrol: "new-password", unlock: "current-password" } as const;

/** Enrolment when the account has no keyring yet, otherwise unlock. Argon2id runs here, in the browser. */
export function UnlockPage({ me, onUnlocked, onLogout }: { me: Me; onUnlocked: (state: UnlockedState) => void; onLogout: () => void }) {
  const [mode, setMode] = useState<Mode>({ kind: "loading" });
  const [passphrase, setPassphrase] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  // Bumped by "Try again" so a transient network failure while loading does not strand the page.
  const [loadAttempt, setLoadAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    (async () => {
      try {
        const [record, vaultList] = await Promise.all([keyringApi.get(), vaultsApi.list()]);
        if (!cancelled) {
          setMode({ kind: "unlock", keyring: record, vaults: vaultList });
        }
      } catch (e) {
        if (!cancelled && e instanceof ApiError && e.status === 404) {
          setMode({ kind: "enrol" });
        } else if (!cancelled) {
          setError("Could not load your keyring.");
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [loadAttempt]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (mode.kind === "enrol") {
      // The strength dictionaries (~1 MB) load only when someone actually enrols.
      const { passphraseProblem } = await import("../app/passphrasePolicy");
      const problem = passphraseProblem(passphrase, [me.email, me.displayName, me.tenantSlug, me.email.split("@")[0] ?? ""]);
      if (problem) {
        setError(problem);
        return;
      }
      if (passphrase !== confirm) {
        setError("The two passphrases differ.");
        return;
      }
    }
    setBusy("Deriving your key (Argon2id, ~1 s)…");
    try {
      const state = mode.kind === "enrol"
        ? await enrolAndCreateVault(passphrase, me)
        : mode.kind === "unlock"
          ? await unlockWithPassphrase(passphrase, me, mode.keyring, mode.vaults)
          : null;
      setPassphrase("");
      setConfirm("");
      if (state) {
        onUnlocked(state);
      }
    } catch (e) {
      setError(e instanceof KeyringError ? "Wrong passphrase." : mode.kind === "enrol" ? "Could not create your vault. Please try again." : "Unlock failed. Please try again.");
    } finally {
      setBusy(null);
    }
  }

  async function logout() {
    await auth.logout().catch(() => undefined);
    onLogout();
  }

  return (
    <main className="shell narrow">
      <h1>{mode.kind === "loading" ? "Loading your keyring" : mode.kind === "enrol" ? "Create your vault passphrase" : "Unlock your vault"}</h1>
      <p className="muted">Signed in as {me.email} ({me.tenantSlug}). <button type="button" className="link" onClick={logout}>Sign out</button></p>
      {mode.kind === "loading" && !error && <p className="muted">One moment…</p>}
      {mode.kind !== "loading" && (
        <form onSubmit={submit} aria-label={mode.kind === "enrol" ? "Enrol" : "Unlock"}>
          {mode.kind === "enrol" && (
            <p className="muted">This passphrase encrypts everything. It is never sent to the server and cannot be recovered — choose something long you will remember.</p>
          )}
          <label>
            Passphrase
            <input name="passphrase" type="password" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} autoComplete={AUTOCOMPLETE[mode.kind === "enrol" ? "enrol" : "unlock"]} minLength={mode.kind === "enrol" ? MIN_PASSPHRASE_LENGTH : undefined} required />
          </label>
          {mode.kind === "enrol" && (
            <label>
              Repeat passphrase
              <input name="confirm" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete={AUTOCOMPLETE.enrol} required />
            </label>
          )}
          {error && <p role="alert" className="error">{error}</p>}
          {busy && <p role="status" className="muted">{busy}</p>}
          <button type="submit" disabled={busy !== null}>{mode.kind === "enrol" ? "Create vault" : "Unlock"}</button>
        </form>
      )}
      {mode.kind === "loading" && error && (
        <>
          <p role="alert" className="error">{error}</p>
          <button type="button" onClick={() => setLoadAttempt((n) => n + 1)}>Try again</button>
        </>
      )}
    </main>
  );
}
