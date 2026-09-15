import { type FormEvent, useCallback, useEffect, useState } from "react";
import { auth } from "../api/endpoints";
import { type PasswordPayload, type SecretListItem, createSecret, deleteSecret, listSecrets, readSecret } from "../app/secretFlow";
import type { UnlockedState } from "../session/vaultSession";

const emptyPayload: PasswordPayload = { username: "", password: "", url: "", notes: "" };

export function VaultPage({ state, onLock, onLogout }: { state: UnlockedState; onLock: () => void; onLogout: () => void }) {
  const [items, setItems] = useState<SecretListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [payload, setPayload] = useState<PasswordPayload>(emptyPayload);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<{ item: SecretListItem; payload: PasswordPayload; reveal: boolean } | null>(null);

  const reload = useCallback(async () => {
    try {
      setItems(await listSecrets(state));
    } catch {
      setError("Could not load secrets.");
    }
  }, [state]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function create(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await createSecret(state, name.trim(), payload);
      setName("");
      setPayload(emptyPayload);
      await reload();
    } catch {
      setError("Could not save the secret.");
    } finally {
      setBusy(false);
    }
  }

  async function show(item: SecretListItem) {
    setError(null);
    try {
      setOpen({ item, payload: await readSecret(state, item.id), reveal: false });
    } catch (e) {
      setError(`Could not decrypt this secret (${e instanceof Error ? e.message : "unknown error"}).`);
    }
  }

  async function remove(item: SecretListItem) {
    setError(null);
    try {
      await deleteSecret(item.id);
      setOpen(null);
      await reload();
    } catch {
      setError("Could not delete the secret.");
    }
  }

  async function logout() {
    onLock();
    await auth.logout().catch(() => undefined);
    onLogout();
  }

  return (
    <main className="shell">
      <header className="bar">
        <div>
          <p className="eyebrow">{state.vault.name} · {state.me.email}</p>
          <h1>Secrets</h1>
        </div>
        <div className="actions">
          <button type="button" onClick={onLock}>Lock</button>
          <button type="button" className="secondary" onClick={logout}>Sign out</button>
        </div>
      </header>

      {error && <p role="alert" className="error">{error}</p>}

      <section className="cards">
        <article>
          <h2>New password</h2>
          <form onSubmit={create} aria-label="New secret">
            <label>Name<input name="name" value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} /></label>
            <label>Username<input name="username" value={payload.username} onChange={(e) => setPayload({ ...payload, username: e.target.value })} autoComplete="off" /></label>
            <label>Password<input name="secret-password" type="password" value={payload.password} onChange={(e) => setPayload({ ...payload, password: e.target.value })} autoComplete="off" required /></label>
            <label>URL<input name="url" value={payload.url} onChange={(e) => setPayload({ ...payload, url: e.target.value })} autoComplete="off" /></label>
            <label>Notes<textarea name="notes" value={payload.notes} onChange={(e) => setPayload({ ...payload, notes: e.target.value })} rows={3} /></label>
            <button type="submit" disabled={busy}>{busy ? "Encrypting…" : "Save"}</button>
          </form>
        </article>

        <article>
          <h2>Stored</h2>
          {items === null && <p className="muted">Loading…</p>}
          {items !== null && items.length === 0 && <p className="muted">No secrets yet.</p>}
          <ul className="list" aria-label="Secrets">
            {(items ?? []).map((item) => (
              <li key={item.id}>
                <button type="button" className="link" onClick={() => show(item)}>{item.name}</button>
                <span className="muted"> v{item.latestVersionNo}</span>
              </li>
            ))}
          </ul>
        </article>

        {open && (
          <article aria-label="Secret detail">
            <h2>{open.item.name}</h2>
            <dl>
              <dt>Username</dt><dd>{open.payload.username || "—"}</dd>
              <dt>Password</dt>
              <dd>
                <span data-testid="password-value">{open.reveal ? open.payload.password : "••••••••"}</span>{" "}
                <button type="button" className="link" onClick={() => setOpen({ ...open, reveal: !open.reveal })}>{open.reveal ? "Hide" : "Reveal"}</button>
              </dd>
              <dt>URL</dt><dd>{open.payload.url || "—"}</dd>
              <dt>Notes</dt><dd>{open.payload.notes || "—"}</dd>
            </dl>
            <div className="actions">
              <button type="button" className="secondary" onClick={() => setOpen(null)}>Close</button>
              <button type="button" className="danger" onClick={() => remove(open.item)}>Delete</button>
            </div>
          </article>
        )}
      </section>
    </main>
  );
}
