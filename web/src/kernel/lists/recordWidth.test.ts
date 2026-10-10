import { afterEach, describe, expect, it } from "vitest";
import { clampShare, readShare, recordShare, shareAfterKey, shareFromPointer, shareToFit, toggledShare, writeShare } from "./recordWidth";

afterEach(() => window.localStorage.clear());

describe("record form width", () => {
  it("keeps every share between the narrowest and the widest form, whole percents only", () => {
    expect(clampShare(10)).toBe(recordShare.min);
    expect(clampShare(99)).toBe(recordShare.wide);
    expect(clampShare(51.6)).toBe(52);
    expect(clampShare(Number.NaN)).toBe(recordShare.standard);
  });

  it("widens with the arrow pointing away from the form: left in English, right in Arabic", () => {
    expect(shareAfterKey("ArrowLeft", 48, false)).toBe(52);
    expect(shareAfterKey("ArrowRight", 48, false)).toBe(44);
    expect(shareAfterKey("ArrowRight", 48, true)).toBe(52);
    expect(shareAfterKey("ArrowLeft", 48, true)).toBe(44);
    expect(shareAfterKey("Home", 60, false)).toBe(recordShare.min);
    expect(shareAfterKey("End", 40, true)).toBe(recordShare.wide);
    expect(shareAfterKey("Enter", 48, false)).toBe(recordShare.wide);
    expect(shareAfterKey("Enter", recordShare.wide, false)).toBe(recordShare.standard);
    expect(shareAfterKey("a", 48, false)).toBeNull();
    expect(shareAfterKey("ArrowLeft", recordShare.wide, false)).toBe(recordShare.wide);
  });

  it("switches between the widest and the standard form", () => {
    expect(toggledShare(48)).toBe(recordShare.wide);
    expect(toggledShare(63)).toBe(recordShare.wide);
    expect(toggledShare(recordShare.wide)).toBe(recordShare.standard);
  });

  it("measures a drag from the form's side of the list body, the left in Arabic", () => {
    // The list body from x = 100 to 1,100.
    const body = new DOMRect(100, 0, 1000, 600);
    expect(shareFromPointer(600, body, false)).toBe(50);
    expect(shareFromPointer(400, body, false)).toBe(70);
    expect(shareFromPointer(400, body, true)).toBe(30);
    expect(shareFromPointer(1050, body, false)).toBe(recordShare.min);
    expect(shareFromPointer(0, body, false)).toBe(recordShare.wide);
  });

  it("grows a form whose content is wider than the panel just enough to show it, never shrinks it", () => {
    // 806px panel in a 1,680px body whose table needs 1,054px: 63% (1,058px).
    expect(shareToFit(48, 806, 248, 1680)).toBe(63);
    expect(shareToFit(63, 1058, 0, 1680)).toBe(63);
    expect(shareToFit(48, 500, 2000, 1000)).toBe(recordShare.wide);
    expect(shareToFit(55, 806, 1, 1680)).toBe(55);
  });

  it("remembers the user's choice per list and survives storage that refuses", () => {
    expect(readShare("identity.users")).toBeNull();
    writeShare("identity.users", 66);
    expect(readShare("identity.users")).toBe(66);
    expect(readShare("identity.roles")).toBeNull();
    window.localStorage.setItem("erp.lists.recordShare.identity.roles", "not a number");
    expect(readShare("identity.roles")).toBeNull();
    writeShare("identity.users", null);
    expect(readShare("identity.users")).toBeNull();
    const original = Object.getOwnPropertyDescriptor(window, "localStorage")!;
    Object.defineProperty(window, "localStorage", {
      configurable: true,
      get() {
        throw new Error("blocked");
      },
    });
    try {
      expect(readShare("identity.users")).toBeNull();
      expect(() => writeShare("identity.users", 60)).not.toThrow();
    } finally {
      Object.defineProperty(window, "localStorage", original);
    }
  });
});
