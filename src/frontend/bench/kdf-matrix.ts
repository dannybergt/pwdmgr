import { deriveKek, type KdfParams } from "../src/crypto/kdf";
import type { Bytes } from "../src/crypto/encoding";

export interface BenchResult extends KdfParams {
  readonly runs: number;
  readonly p50Ms: number;
  readonly minMs: number;
  readonly maxMs: number;
}

export const MATRIX: KdfParams[] = [];
for (const memoryMib of [32, 64, 128]) {
  for (const iterations of [2, 3, 4]) {
    for (const parallelism of [1, 4]) {
      MATRIX.push({ memoryKib: memoryMib * 1024, iterations, parallelism });
    }
  }
}

const SALT: Bytes = new Uint8Array(16).fill(0x5a);

export async function benchOne(params: KdfParams, runs: number): Promise<BenchResult> {
  const samples: number[] = [];
  for (let i = 0; i < runs; i += 1) {
    const start = performance.now();
    await deriveKek("benchmark passphrase", SALT, params);
    samples.push(performance.now() - start);
  }
  samples.sort((a, b) => a - b);
  return {
    ...params,
    runs,
    p50Ms: Math.round(samples[Math.floor(samples.length / 2)]),
    minMs: Math.round(samples[0]),
    maxMs: Math.round(samples[samples.length - 1])
  };
}

export async function benchMatrix(
  runs: number,
  onResult?: (result: BenchResult) => void
): Promise<BenchResult[]> {
  const results: BenchResult[] = [];
  for (const params of MATRIX) {
    const result = await benchOne(params, runs);
    results.push(result);
    onResult?.(result);
  }
  return results;
}

export function formatRow(r: BenchResult): string {
  return `m=${String(r.memoryKib / 1024).padStart(3)}MiB t=${r.iterations} p=${r.parallelism}  p50=${String(r.p50Ms).padStart(5)}ms  min=${String(r.minMs).padStart(5)}ms  max=${String(r.maxMs).padStart(5)}ms`;
}
