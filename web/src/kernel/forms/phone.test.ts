import { describe, expect, it } from "vitest";
import { caretAfter, groupNational, isCompletePhone, phoneValue } from "./phone";

describe("phone numbers", () => {
  it("keeps +971 and groups the number after it as it is read", () => {
    expect(phoneValue("505550199")).toBe("+971 50 555 0199");
    expect(phoneValue("45550100")).toBe("+971 4 555 0100");
    expect(phoneValue("8001234")).toBe("+971 800 1234");
    expect(phoneValue("600522222")).toBe("+971 600 522222");
    expect(groupNational("50")).toBe("50");
    expect(groupNational("505")).toBe("50 5");
    expect(groupNational("4555")).toBe("4 555");
  });

  it("takes the number however it is typed or pasted: trunk 0, 00, +971, 971 without the +, Arabic-Indic digits", () => {
    for (const typed of ["0505550199", "050 555 0199", "+971 50 555 0199", "+971505550199", "00971 50 555 0199", "971505550199", "+971 050 555 0199", "٠٥٠٥٥٥٠١٩٩", "+971 +971 50 555 0199"]) {
      expect(phoneValue(typed), typed).toBe("+971 50 555 0199");
    }
  });

  it("keeps another country's number as typed, and the code while it is being typed", () => {
    expect(phoneValue("+44 20 7946 0958")).toBe("+44 20 7946 0958");
    expect(phoneValue("+971 +44 (20) 7946-0958")).toBe("+44 (20) 7946-0958");
    expect(phoneValue("+")).toBe("+");
    expect(phoneValue("+97")).toBe("+97");
    expect(phoneValue("+971")).toBe("");
    expect(phoneValue("+971 ")).toBe("");
    expect(phoneValue("")).toBe("");
    expect(phoneValue("0")).toBe("");
  });

  it("calls only a whole UAE mobile or fixed-line number complete", () => {
    expect(isCompletePhone("+971 50 555 0199")).toBe(true);
    expect(isCompletePhone("+971 4 555 0100")).toBe(true);
    expect(isCompletePhone("+971 50 555 019")).toBe(false);
    expect(isCompletePhone("+971 50 555 01999")).toBe(false);
    expect(isCompletePhone("+971 4 555 010")).toBe(false);
    // Toll-free and shared-cost numbers have no fixed length; a 6 followed by 0 is one of them being typed.
    expect(isCompletePhone("+971 600 52222")).toBe(false);
    expect(isCompletePhone("+971 800 1234")).toBe(false);
    expect(isCompletePhone("+44 20 7946 0958")).toBe(false);
    expect(isCompletePhone("")).toBe(false);
  });

  it("puts the caret back before the same digits", () => {
    expect(caretAfter("+971 50 555 0199", 0)).toBe(16);
    expect(caretAfter("+971 50 555 0199", 4)).toBe(12);
    expect(caretAfter("+971 50 555 0199", 5)).toBe(10);
  });
});
