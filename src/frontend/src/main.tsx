import React from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

function App() {
  return (
    <main className="shell">
      <section className="hero">
        <p className="eyebrow">pwdmgr / Privora</p>
        <h1>Zero-knowledge secrets, files and privileged access.</h1>
        <p>
          Bootstrap UI for the enterprise vault: tenants, shared safes,
          encrypted files, typed templates, agents and audit.
        </p>
      </section>
      <section className="cards" aria-label="Initial modules">
        <article>
          <h2>Vault</h2>
          <p>Personal and shared vaults with client-side encryption.</p>
        </article>
        <article>
          <h2>Templates</h2>
          <p>Credentials, certificates, credit cards, licenses and custom forms.</p>
        </article>
        <article>
          <h2>Agent</h2>
          <p>Windows-first use-only access and local application handoff.</p>
        </article>
      </section>
    </main>
  );
}

const root = createRoot(document.getElementById("root") as HTMLElement);
root.render(<App />);

