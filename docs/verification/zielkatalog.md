# Zielkatalog — what the `verifier` proves on the running system

One row per observable goal. The `verifier` subagent (Constitution §4 Phase 4) measures against
this table, not against green exit codes. Status carries the revision at which the proof was
last produced. Rows are added per slice; a slice is not done while one of its rows is OPEN.

Layers: L0 = code reading only · L1 = HTTP against the running stack · L2 = real browser ·
L3 = database / process state observed directly.

## Slice #1 — crypto primitives + Argon2id benchmark (ADR-0006)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P1-01 | `npm test` green in Node 24 (mvp-slice-plan.md, slice #1) | Vitest `31 passed`, exit 0 in `node:24-alpine` | L3 | `docker run … node:24-alpine sh -c 'npm ci && npm test'` in `src/frontend` | flip one expected byte of a KAT → that test fails | OPEN |
| P1-02 | Argon2id KATs are bit-exact | 7 reference vectors + frozen own vector `853b272a…bab49e` at `KDF_DEFAULT` match | L3 | `kdf.test.ts` output | P1-01 negative control | OPEN |
| P1-03 | Benchmark numbers recorded, one real Chromium run | `npm run bench:kdf` JSON in Node **and** `bench/kdf.html` result table from headless Chromium (Playwright image) with `KDF_DEFAULT` p50 ≤ 1 s | L2 | TESTING.md "Frontend crypto tests and KDF benchmark" | — | OPEN |
| P1-04 | Production build green with the WASM CSP | `npm run build` exit 0; `dist/index.html` contains `script-src 'self' 'wasm-unsafe-eval'` and nothing else in `script-src` | L3 | build in `node:24-alpine`, grep | `'unsafe-eval'` absent | OPEN |
| P1-05 | WASM runs under that CSP in a real browser | `bench/kdf.html` (same `script-src`) completes in Chromium with no CSP console error other than the `frame-ancestors`-in-meta notice on `index.html` | L2 | Playwright run of P1-03, console captured | remove `'wasm-unsafe-eval'` from the bench page → `CompileError`/CSP violation | OPEN |
| P1-06 | Tampered AAD / ciphertext / tag → decrypt fails (mvp-slice-plan.md, slice #1) | `aead.test.ts` tamper cases reject; no plaintext returned | L3 | `npm test` | untampered blob round-trips | OPEN |
| P1-07 | No passphrase or key material is logged or persisted | crypto modules contain no `console.*`, no `localStorage`/`sessionStorage`/`indexedDB` access | L0 | `grep -rE "console\.|localStorage|sessionStorage|indexedDB" src/frontend/src/crypto` → 0 | — | OPEN |
