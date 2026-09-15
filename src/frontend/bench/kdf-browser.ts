// Browser runner for the Argon2id benchmark; served by `vite dev` at /bench/kdf.html.
import { benchMatrix, formatRow } from "./kdf-matrix";

const out = document.getElementById("out") as HTMLPreElement;
const runs = Number(new URLSearchParams(location.search).get("runs") ?? 3);
out.textContent = `argon2id benchmark — ${navigator.userAgent}, ${runs} runs per cell, p50 reported\n`;
const results = await benchMatrix(runs, (r) => {
  out.textContent += `${formatRow(r)}\n`;
});
const json = JSON.stringify({ runtime: navigator.userAgent, results });
out.textContent += `${json}\n`;
document.body.dataset.done = "1";
