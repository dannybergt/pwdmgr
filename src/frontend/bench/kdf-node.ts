// Argon2id parameter benchmark, Node runner: `npm run bench:kdf [runs]`.
// The Chromium run (see TESTING.md) uses bench/kdf.html with the same matrix.
import { benchMatrix, formatRow } from "./kdf-matrix";

const runs = Number(process.argv[2] ?? 3);
console.log(`argon2id benchmark — node ${process.version}, ${runs} runs per cell, p50 reported`);
const results = await benchMatrix(runs, (r) => console.log(formatRow(r)));
console.log(JSON.stringify({ runtime: `node ${process.version}`, results }));
