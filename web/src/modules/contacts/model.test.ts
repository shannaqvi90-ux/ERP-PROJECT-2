import { describe, expect, it } from "vitest";
import { draftOf, requestOf } from "./ContactForm";
import { contactName, duplicateQuery, parseTags, trnDigits } from "./types";

const defaults = { country: "AE", defaultCurrency: "AED", baseCurrency: "AED" };

describe("contacts model", () => {
  it("reads tags typed as one line, each once, in the order typed, with Arabic commas too", () => {
    expect(parseTags(" Customer, VIP ,customer;Export،  ")).toEqual(["Customer", "VIP", "Export"]);
    expect(parseTags("")).toEqual([]);
  });

  it("turns Arabic-Indic digits of a TRN into 0 to 9 as they are typed", () => {
    expect(trnDigits("١٠٠-١٢٣ 4")).toBe("100-123 4");
  });

  it("names a contact in the screen's language, falling back to the other name", () => {
    expect(contactName({ nameEn: "Falcon LLC", nameAr: "فالكون" }, "ar")).toBe("فالكون");
    expect(contactName({ nameEn: "Falcon LLC", nameAr: "" }, "ar")).toBe("Falcon LLC");
    expect(contactName({ nameEn: "", nameAr: "فالكون" }, "en")).toBe("فالكون");
  });

  it("asks for duplicates only for what is worth comparing", () => {
    const blank = draftOf(null, defaults);
    expect(duplicateQuery(blank, null)).toBeNull();
    // A phone field holding only the +971 it keeps, a short name or an e-mail without @ is not compared.
    expect(duplicateQuery({ ...blank, nameEn: "ab", phone: "+971 ", email: "sales" }, null)).toBeNull();
    const query = new URLSearchParams(duplicateQuery({ ...blank, nameEn: " Falcon ", email: "a@b.example", mobile: "+971 50 555 0102", taxRegistrationNumber: "١٠٠١٢٣٤٥٦٧٨٩٠٠٣" }, "c1")!);
    expect(query.get("sameName")).toBe("Falcon");
    expect(query.get("sameEmail")).toBe("a@b.example");
    expect(query.get("sameMobile")).toBe("+971 50 555 0102");
    expect(query.get("sameTaxNumber")).toBe("١٠٠١٢٣٤٥٦٧٨٩٠٠٣");
    expect(query.get("except")).toBe("c1");
  });

  it("sends no credit limit when no amount is typed, no emirate outside the UAE and no organisation for an organisation", () => {
    const draft = { ...draftOf(null, defaults), nameEn: "Muscat Marine LLC", country: "om", emirate: "dubai" as const, organisationId: "x", jobTitle: "Boss", phone: "+971 " };
    const body = requestOf(draft, null);
    expect(body.creditLimitAmount).toBeNull();
    expect(body.creditLimitCurrency).toBeNull();
    expect(body.emirate).toBeNull();
    expect(body.country).toBe("OM");
    expect(body.organisationId).toBeNull();
    expect(body.jobTitle).toBeNull();
    expect(body.phone).toBeNull();
    const person = requestOf({ ...draftOf(null, defaults, { kind: "person", organisationId: "o1" }), nameEn: "Hamad", creditLimitAmount: "1000.00", creditLimitCurrency: "usd", creditLimitExchangeRate: "3.6725" }, 4);
    expect(person.organisationId).toBe("o1");
    expect(person.creditLimitAmount).toBe("1000.00");
    expect(person.creditLimitCurrency).toBe("USD");
    expect(person.creditLimitExchangeRate).toBe("3.6725");
    expect(person.version).toBe(4);
  });

  it("starts a saved contact's credit limit rate only for a foreign currency", () => {
    const base = { id: "c", kind: "organisation" as const, code: null, nameEn: "A", nameAr: "", organisationId: null, organisationName: null, organisationNameAr: null,
      jobTitle: null, email: null, phone: null, mobile: null, website: null, addressLine1: null, addressLine2: null, area: null, city: null, emirate: null,
      poBox: null, country: "AE", addressAr: null, tags: ["VIP"], tradeLicenceNumber: null, taxRegistrationNumber: null, defaultCurrency: "USD", notes: null,
      isActive: true, peopleCount: 0, createdAt: "", updatedAt: "", version: 1 };
    const usd = draftOf({ ...base, creditLimit: { amount: "10.00", currency: "USD", exchangeRate: "3.6725", baseAmount: "36.73", baseCurrency: "AED" } }, defaults);
    expect(usd.creditLimitExchangeRate).toBe("3.6725");
    expect(usd.tags).toBe("VIP");
    const aed = draftOf({ ...base, creditLimit: { amount: "10.00", currency: "AED", exchangeRate: "1", baseAmount: "10.00", baseCurrency: "AED" } }, defaults);
    expect(aed.creditLimitExchangeRate).toBe("");
    const none = draftOf({ ...base, creditLimit: null }, defaults);
    expect(none.creditLimitCurrency).toBe("USD");
  });
});
