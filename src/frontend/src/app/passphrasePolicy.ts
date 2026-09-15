import { ZxcvbnFactory } from "@zxcvbn-ts/core";
import * as common from "@zxcvbn-ts/language-common";
import * as en from "@zxcvbn-ts/language-en";

/**
 * The passphrase is the only thing between an attacker with a database dump and the vault
 * (ADR-0002); Argon2id slows guessing but does not rescue a dictionary word. zxcvbn runs
 * fully offline (no HIBP-style network call, which would leak material about the passphrase).
 */
import { MIN_PASSPHRASE_LENGTH } from "./passphraseRules";

const MIN_SCORE = 3;

const strength = new ZxcvbnFactory({
  dictionary: { ...common.dictionary, ...en.dictionary },
  graphs: common.adjacencyGraphs,
  translations: en.translations
});

/** Returns a user-facing reason to reject the passphrase, or null when it is acceptable. */
export function passphraseProblem(passphrase: string, userInputs: string[]): string | null {
  if (passphrase.length < MIN_PASSPHRASE_LENGTH) {
    return `Use at least ${MIN_PASSPHRASE_LENGTH} characters.`;
  }
  const result = strength.check(passphrase, userInputs.filter((v) => v.length > 0));
  if (result.score >= MIN_SCORE) {
    return null;
  }
  return result.feedback.warning || "This passphrase is too easy to guess — try a longer phrase of unrelated words.";
}
