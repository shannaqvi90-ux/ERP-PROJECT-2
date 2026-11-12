import { describe, expect, it } from "vitest";
import { customDraft, customKey, customLabel, customRequest, optionLabel, type CustomFieldEntry } from "./customFields";
import { registerDynamicStrings, translate } from "../i18n";

const entry = (over: Partial<CustomFieldEntry>): CustomFieldEntry => ({
  key: "cfX", type: "text", labelEn: "X", labelAr: "س", required: false, decimals: 0, options: [], lookupTarget: null, lookupEndpoint: null,
  lookupLabelProperty: null, lookupLabelPropertyAr: null, value: null, valueLabel: null, valueLabelAr: null, searchable: false, ...over,
});

describe("custom field values in a form's draft", () => {
  const entries = [
    entry({ key: "cfRegion", value: "North" }),
    entry({ key: "cfScore", type: "number", decimals: 1, value: "12.5" }),
    entry({ key: "cfVip", type: "boolean", value: true }),
    entry({ key: "cfSince", type: "date" }),
  ];

  it("keeps every value as text under customFields.<key>", () => {
    expect(customDraft(entries)).toEqual({ "customFields.cfRegion": "North", "customFields.cfScore": "12.5", "customFields.cfVip": "true", "customFields.cfSince": "" });
    expect(customDraft(undefined)).toEqual({});
  });

  it("sends empty text as null and a yes/no as a boolean, a number as its text", () => {
    const draft = { ...customDraft(entries), "customFields.cfVip": "false", "customFields.cfRegion": "  East " };
    expect(customRequest(entries, draft)).toEqual({ cfRegion: "East", cfScore: "12.5", cfVip: false, cfSince: null });
  });

  it("sends nothing for a record without custom fields", () => {
    expect(customRequest([], {})).toBeUndefined();
    expect(customRequest(undefined, {})).toBeUndefined();
  });

  it("labels a field and an option in the screen's language, falling back to the other", () => {
    expect(customLabel({ labelEn: "Region", labelAr: "منطقة" }, "ar")).toBe("منطقة");
    expect(customLabel({ labelEn: "Region", labelAr: "" }, "ar")).toBe("Region");
    expect(optionLabel({ labelEn: "Retail", labelAr: "تجزئة" }, "en")).toBe("Retail");
    expect(customKey("cfA")).toBe("customFields.cfA");
  });
});

describe("the tenant's own labels", () => {
  it("are found by key in both languages once registered, and never replace the product's strings", () => {
    registerDynamicStrings({ "customField.abc.label": { en: "Customer segment", ar: "شريحة العميل" } });
    expect(translate("en", "customField.abc.label")).toBe("Customer segment");
    expect(translate("ar", "customField.abc.label")).toBe("شريحة العميل");
    expect(translate("en", "customField.unknown.label")).toBe("customField.unknown.label");
    registerDynamicStrings({ "forms.close": { en: "hijacked", ar: "hijacked" } });
    expect(translate("en", "forms.close")).not.toBe("hijacked");
  });
});
