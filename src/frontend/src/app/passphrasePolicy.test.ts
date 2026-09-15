import { describe, expect, it } from "vitest";
import { passphraseProblem } from "./passphrasePolicy";

describe("passphrasePolicy", () => {
  it("rejects short, dictionary and user-derived passphrases", () => {
    expect(passphraseProblem("short", [])).toMatch(/at least 12/);
    expect(passphraseProblem("passwordpassword", [])).not.toBeNull();
    expect(passphraseProblem("123456789012", [])).not.toBeNull();
    expect(passphraseProblem("alice@example.test", ["alice@example.test", "alice"])).not.toBeNull();
  });

  it("accepts a long phrase of unrelated words", () => {
    expect(passphraseProblem("correct horse battery staple violet", [])).toBeNull();
    expect(passphraseProblem("e2e dev-seed passphrase 2026", [])).toBeNull();
  });
});
