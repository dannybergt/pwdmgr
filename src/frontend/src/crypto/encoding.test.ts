import { describe, expect, it } from "vitest";
import { concat, fromBase64, fromHex, toBase64, toHex, utf8Decode, utf8Encode, wipe } from "./encoding";

describe("encoding", () => {
  it("round-trips base64", () => {
    const bytes = fromHex("00ff10fe7f80");
    expect(toBase64(bytes)).toBe("AP8Q/n+A");
    expect(fromBase64("AP8Q/n+A")).toEqual(bytes);
    expect(toBase64(new Uint8Array(0))).toBe("");
  });

  it("rejects malformed base64", () => {
    expect(() => fromBase64("AP8Q/n+A=")).toThrow(TypeError);
    expect(() => fromBase64("not base64!")).toThrow(TypeError);
  });

  it("round-trips hex and rejects odd/invalid input", () => {
    expect(toHex(fromHex("deadBEEF"))).toBe("deadbeef");
    expect(() => fromHex("abc")).toThrow(TypeError);
    expect(() => fromHex("zz")).toThrow(TypeError);
  });

  it("round-trips utf-8 and rejects invalid sequences", () => {
    expect(utf8Decode(utf8Encode("ß€😀"))).toBe("ß€😀");
    expect(() => utf8Decode(fromHex("ff"))).toThrow();
  });

  it("concatenates", () => {
    const joined = concat(fromHex("01"), fromHex("0203"), new Uint8Array(0));
    expect(toHex(joined)).toBe("010203");
  });

  it("wipes buffers in place", () => {
    const bytes = fromHex("ffff");
    wipe(bytes);
    expect(toHex(bytes)).toBe("0000");
  });
});
