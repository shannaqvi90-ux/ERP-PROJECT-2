/** Records the custom fields screens' tests serve (API shapes as the server answers them). */
export const selectRow = () => ({
  id: "f1", recordType: "contacts.contacts", key: "cfCustomerSegment", labelEn: "Customer segment", labelAr: "شريحة العميل", fieldType: "select",
  required: false, searchable: false, sortOrder: 10, isActive: true, updatedAt: "2026-10-20T08:00:00Z", version: 3,
});
export const textRow = () => ({
  id: "f2", recordType: "contacts.contacts", key: "cfAccountManager", labelEn: "Account manager", labelAr: "مدير الحساب", fieldType: "text",
  required: true, searchable: true, sortOrder: 20, isActive: true, updatedAt: "2026-10-20T08:00:00Z", version: 1,
});
export const select = () => ({
  id: "f1", recordType: "contacts.contacts", key: "cfCustomerSegment", labelEn: "Customer segment", labelAr: "شريحة العميل", type: "select",
  required: false, default: "retail", decimals: 0, lookupTarget: null, sortOrder: 10, searchable: false, isActive: true,
  options: [{ value: "retail", labelEn: "Retail", labelAr: "تجزئة" }, { value: "wholesale", labelEn: "Wholesale", labelAr: "جملة" }],
  createdAt: "2026-10-01T00:00:00Z", updatedAt: "2026-10-20T08:00:00Z", version: 3, everyCompany: true,
});
export const text = () => ({
  ...select(), id: "f2", key: "cfAccountManager", labelEn: "Account manager", labelAr: "مدير الحساب", type: "text", required: true, default: null, options: [], searchable: true, sortOrder: 20, version: 1,
});
export const recordTypes = () => [{ table: "contacts.contacts", labelKey: "contacts.menu.contacts", canBePointedAt: true, fieldCount: 2 }];
