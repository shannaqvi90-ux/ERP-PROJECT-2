/** Records the numbering screens' tests serve (API shapes as the server answers them). */
export const orderRow = () => ({
  id: "s1", companyId: "c1", companyCode: "AN-DXB", documentType: "purchasing.order", nameEn: "Purchase order", nameAr: "أمر شراء",
  prefix: "P", suffix: "", padding: 5, reset: "never", gapless: false, perBranch: false, nextNumber: "P00148", nextValue: 148,
  workingCompany: true, updatedAt: "2026-10-20T08:00:00Z", version: 7, canUpdate: true,
});
export const order = () => ({
  ...orderRow(), companyNameEn: "Al Noor Trading LLC", companyNameAr: "شركة النور للتجارة ذ.م.م", previewBranchId: null, today: "2026-10-25",
  branches: [{ id: "b1", code: "DEIRA-HQ", nameEn: "Deira head office", nameAr: "المكتب الرئيسي - ديرة" }, { id: "b2", code: "AQZ-WH", nameEn: "Al Quoz warehouse", nameAr: "مستودع القوز" }],
  counters: [
    { branchId: null, branchCode: null, period: "", nextValue: 148, lastIssued: 147, lastIssuedAt: "2026-10-25T06:00:00Z" },
    { branchId: null, branchCode: null, period: "2025", nextValue: 900, lastIssued: 899, lastIssuedAt: "2025-12-30T06:00:00Z" },
  ],
  recent: [{ number: "P00147", documentDate: "2026-10-25", issuedAt: "2026-10-25T06:00:00Z", gapless: false }],
  createdAt: "2026-01-01T00:00:00Z",
});
export const invoiceRow = () => ({
  ...orderRow(), id: "s2", documentType: "sales.invoice", nameEn: "Tax invoice", nameAr: "فاتورة ضريبية", prefix: "INV-{YYYY}-", reset: "yearly", gapless: true,
  nextNumber: "INV-2026-01203", nextValue: 1203, version: 3,
});
export const invoice = () => ({ ...order(), ...invoiceRow(), counters: [{ branchId: null, branchCode: null, period: "2026", nextValue: 1203, lastIssued: 1202, lastIssuedAt: "2026-10-25T06:00:00Z" }], recent: [] });
