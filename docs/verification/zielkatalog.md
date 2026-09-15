# Zielkatalog — what the `verifier` proves on the running system

One row per observable goal. The `verifier` subagent (Constitution §4 Phase 4) measures against
this table, not against green exit codes. Status carries the revision at which the proof was
last produced. Rows are added per slice; a slice is not done while one of its rows is OPEN.

Layers: L0 = code reading only · L1 = HTTP against the running stack · L2 = real browser ·
L3 = database / process state observed directly.

## Slice #1 — crypto primitives + Argon2id benchmark (ADR-0006)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P1-01 | `npm test` green in Node 24 (mvp-slice-plan.md, slice #1) | Vitest `39 passed`, exit 0 in `node:24-alpine` | L3 | `docker run … node:24-alpine sh -c 'npm ci && npm test'` in `src/frontend` | flip one expected byte of a KAT → exactly that test fails, exit 1 | PROVEN ee141f7 (31 tests); re-prove after test additions |
| P1-02 | Argon2id KATs are bit-exact, in Node and in Chromium | 7 reference vectors + frozen own vectors (`853b272a…bab49e` ASCII, `641be819…1cd9fb` NFKC-sensitive) match at `KDF_DEFAULT`; the ASCII vector also matches when `/src/crypto/kdf.ts` is imported in a Chromium page | L3 + L2 | `kdf.test.ts` output; Playwright page importing `kdf.ts` | flip one byte of a frozen vector → that test fails | PROVEN ee141f7 (Node + Chromium 153 for the ASCII vector); NFKC vector added after — re-prove |
| P1-03 | Benchmark numbers recorded, one real Chromium run | `npm run bench:kdf` JSON in Node **and** `bench/kdf.html` result table from headless Chromium (Playwright image) with `KDF_DEFAULT` p50 ≤ 1 s | L2 | TESTING.md "Frontend crypto tests and KDF benchmark" | in the same run at least one cell (128 MiB, t=4) exceeds 1 s, otherwise the harness is not measuring | PROVEN ee141f7 (Node p50 641 ms / 3 runs, Chromium 153 p50 609 ms / 1 run, 5-run table in ADR-0006; 128 MiB t=4 p=4 1745 ms) |
| P1-04 | Production build green with the WASM CSP | `npm run build` exit 0; `dist/index.html` contains `script-src 'self' 'wasm-unsafe-eval'` and nothing else in `script-src`; no inline `<script>` | L3 | build in `node:24-alpine`, grep | `'unsafe-eval'` absent (grep on a mutated copy hits) | PROVEN ee141f7 |
| P1-05 | WASM runs under that CSP in a real browser | `bench/kdf.html` (same `script-src`) completes in Chromium with `console errors: []`; the built `index.html` (`vite preview`, not the dev server) shows only the `frame-ancestors`-in-meta notice | L2 | Playwright run of P1-03, console captured; `vite preview` in Chromium | reverse proxy stripping `'wasm-unsafe-eval'` → `CompileError: WebAssembly.compile() … violates … "script-src 'self'"`, page never finishes; passthrough proxy → finishes | PROVEN ee141f7 |
| P1-06 | Tampered AAD / ciphertext / tag → decrypt fails (mvp-slice-plan.md, slice #1) | `aead.test.ts` tamper cases reject with `OperationError`; no plaintext returned | L3 | `npm test`; direct tsx script against `aead.ts` | untampered blob round-trips; removing the tamper lines makes the tests fail | PROVEN ee141f7 |
| P1-07 | No passphrase or key material is logged or persisted | crypto modules contain no `console.*`, no `localStorage`/`sessionStorage`/`indexedDB`/`fetch`/`postMessage` | L0 | `grep -rE "console\.|localStorage|sessionStorage|indexedDB" src/frontend/src/crypto` → 0 | the same grep over `bench/` hits `kdf-node.ts` | PROVEN ee141f7 |

Known noise, not a gap: on the Vite **dev server** `index.html` reports `Applying inline style
violates … style-src 'self'` because Vite injects `styles.css` inline in dev mode; the production
build does not. Tracked in STATE.md (CSP moves to response headers with the web image).
