import { type FormEvent, useState } from "react";
import { ApiError } from "../api/client";
import { auth } from "../api/endpoints";
import type { Me } from "../api/types";

export function LoginPage({ onLoggedIn }: { onLoggedIn: (me: Me) => void }) {
  const [tenantSlug, setTenantSlug] = useState("dev");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await auth.login(tenantSlug.trim(), email.trim(), password);
      setPassword("");
      onLoggedIn(await auth.me());
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        setError("Tenant, e-mail or password is wrong.");
      } else if (e instanceof ApiError && e.status === 429) {
        setError("Too many attempts. Please wait a minute.");
      } else {
        setError("Login failed. Please try again.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="shell narrow">
      <h1>Sign in</h1>
      <p className="muted">Your login password only opens the session. The vault passphrase comes next and never leaves this browser.</p>
      <form onSubmit={submit} aria-label="Sign in">
        <label>
          Tenant
          <input name="tenant" value={tenantSlug} onChange={(e) => setTenantSlug(e.target.value)} autoComplete="organization" required />
        </label>
        <label>
          E-mail
          <input name="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required />
        </label>
        <label>
          Password
          <input name="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        </label>
        {error && <p role="alert" className="error">{error}</p>}
        <button type="submit" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
      </form>
    </main>
  );
}
