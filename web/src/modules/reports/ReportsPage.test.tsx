import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, settle, submit, type Rendered } from "../../test/render";
import type { ReportCatalog, ReportDocument } from "./model";
import { reportUrl } from "./model";
import { lookupLabel, ReportsPage, ReportView } from "./ReportsPage";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/reports/catalog");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
});

const catalog: ReportCatalog = {
  items: [
    {
      key: "identity.usersByRole",
      module: "identity",
      title: "Users by role",
      description: "Who can sign in, grouped by role.",
      path: "/api/reports/run/identity.usersByRole",
      parameters: [
        { key: "status", label: "Status", type: "choice", required: false, choices: [{ value: "active", label: "Active" }, { value: "disabled", label: "Disabled" }], lookup: null, lookupEndpoint: null, lookupLabels: [] },
        { key: "signedInSince", label: "Signed in since", type: "date", required: false, choices: [], lookup: null, lookupEndpoint: null, lookupLabels: [] },
      ],
      columns: [
        { key: "role", label: "Role", type: "text", total: false, groupable: true },
        { key: "email", label: "Email", type: "text", total: false, groupable: false },
      ],
      defaultGroupBy: "role",
      isDocument: false,
    },
  ],
  lists: [{ key: "tenancy.companies", title: "Companies", path: "/api/reports/lists/tenancy.companies" }],
};

const arabicDocument: ReportDocument = {
  key: "identity.usersByRole",
  title: "المستخدمون حسب الدور",
  subject: null,
  issuer: "ديمو للتجارة",
  language: "ar",
  direction: "rtl",
  numerals: "latn",
  parameters: [{ label: "الحالة", text: "نشط" }],
  facts: [],
  columns: [
    { key: "role", label: "الدور", type: "text", align: "start", total: false },
    { key: "email", label: "البريد الإلكتروني", type: "text", align: "start", total: false },
  ],
  groupBy: "role",
  groupLabel: "الدور",
  groups: [{ label: "مدير النظام", count: 1, countText: "سجل واحد", rows: [{ cells: [{ value: "مدير النظام", text: "مدير النظام" }, { value: "a@x.example", text: "a@x.example" }] }], totals: [null, null] }],
  totals: [null, null],
  rowCount: 1,
  matchCount: 1,
  rowCountText: "سجل واحد",
  truncated: false,
  printedAt: "2026-10-04T10:00:00Z",
  printedAtText: "2026-10-04 14:00",
  printedBy: "Mariam",
  texts: { total: "الإجمالي", printed: "طُبع بواسطة Mariam", page: "صفحة", empty: "لا توجد سجلات.", noValue: "—" },
};

async function show() {
  view = await render(
    <I18nProvider initial="en">
      <ShortcutProvider>
        <ReportsPage />
      </ShortcutProvider>
    </I18nProvider>,
  );
  await settle();
  return view;
}

describe("reports screen", () => {
  it("runs a report with its parameters and draws an Arabic document right to left on an English screen; the address keeps the choice", async () => {
    const calls = mockFetch((_method, url) => {
      if (url.startsWith("/api/reports/catalog")) return { status: 200, body: catalog };
      if (url.startsWith("/api/reports/run/identity.usersByRole")) return { status: 200, body: arabicDocument };
      return { status: 404, body: {} };
    });
    await show();
    const choice = [...view!.container.querySelectorAll<HTMLButtonElement>(".report-choice")].find((b) => b.textContent?.includes("Users by role"))!;
    act(() => choice.click());
    await settle();

    const status = view!.container.querySelector<HTMLSelectElement>('[data-field="status"] select')!;
    act(() => {
      status.value = "active";
      status.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const language = view!.container.querySelector<HTMLSelectElement>('[data-field="language"] select')!;
    act(() => {
      language.value = "ar";
      language.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await settle();
    expect(window.location.search).toBe("?report=identity.usersByRole&status=active&language=ar");

    // The grouping select shows the report's own grouping, the one the document is built with.
    const groupBy = view!.container.querySelector<HTMLSelectElement>('[data-field="groupBy"] select')!;
    expect(groupBy.value).toBe("role");
    expect(groupBy.selectedOptions[0]!.textContent).toBe("Role");
    const pdf = [...view!.container.querySelectorAll<HTMLAnchorElement>("a.button")].find((a) => a.textContent === "PDF")!;
    expect(pdf.getAttribute("href")).toBe("/api/reports/run/identity.usersByRole?status=active&groupBy=role&format=pdf&language=ar&numerals=latn");

    await submit(view!.container);
    await settle();
    expect(calls.at(-1)!.url).toBe("/api/reports/run/identity.usersByRole?status=active&groupBy=role&format=json&language=ar&numerals=latn");
    const doc = view!.container.querySelector('[data-testid="report-document"] article')!;
    expect(doc.getAttribute("dir")).toBe("rtl");
    expect(doc.getAttribute("lang")).toBe("ar");
    expect(doc.querySelector(".report-group")!.textContent).toContain("مدير النظام");
    expect(doc.querySelector(".report-table td")!.textContent).toBe("مدير النظام");
  });

  it("shows the server's parameter errors on their fields and opens a linked report at once", async () => {
    window.history.replaceState(null, "", "/reports/catalog?report=identity.usersByRole&signedInSince=2026-13-01&run=1");
    mockFetch((_method, url) => {
      if (url.startsWith("/api/reports/catalog")) return { status: 200, body: catalog };
      if (url.startsWith("/api/reports/run/")) return { status: 400, body: { code: "validation", title: "Some fields need attention.", errors: { signedInSince: [{ code: "date", message: "Enter a date as YYYY-MM-DD." }] } } };
      return { status: 404, body: {} };
    });
    await show();
    await settle();
    expect(view!.container.querySelector('[data-field="signedInSince"] .field-error')!.textContent).toBe("Enter a date as YYYY-MM-DD.");
    expect(view!.container.querySelector('[data-testid="report-document"]')).toBeNull();
  });
});

describe("report totals", () => {
  it("prints each group's totals under its rows and the grand total at the foot, in their columns, named in the document's language", async () => {
    const cell = (value: number) => ({ value, text: String(value) });
    const totalled: ReportDocument = {
      ...arabicDocument,
      key: "identity.roleSummary",
      title: "الأدوار والصلاحيات",
      parameters: [],
      columns: [
        { key: "name", label: "الاسم", type: "text", align: "start", total: false },
        { key: "users", label: "المستخدمون", type: "number", align: "end", total: true },
        { key: "permissions", label: "الصلاحيات", type: "number", align: "end", total: true },
      ],
      groupBy: "kind",
      groupLabel: "النوع",
      groups: [
        {
          label: "نظام",
          count: 2,
          countText: "سجلان",
          rows: [
            { cells: [{ value: "مدير النظام", text: "مدير النظام" }, cell(3), cell(40)] },
            { cells: [{ value: "مشاهد", text: "مشاهد" }, cell(5), cell(12)] },
          ],
          totals: [null, cell(8), cell(52)],
        },
        { label: "مخصص", count: 1, countText: "سجل واحد", rows: [{ cells: [{ value: "محاسب", text: "محاسب" }, cell(2), cell(9)] }], totals: [null, cell(2), cell(9)] },
      ],
      totals: [null, cell(10), cell(61)],
      rowCount: 3,
      matchCount: 3,
      rowCountText: "3 سجلات",
    };
    view = await render(<ReportView document={totalled} />);
    const doc = view.container.querySelector('[data-testid="report-document"] article')!;
    const texts = (selector: string) => [...doc.querySelectorAll(selector)].map((row) => [...row.querySelectorAll("td")].map((td) => td.textContent));
    expect(texts("tr.report-subtotal")).toEqual([
      ["الإجمالي · نظام", "8", "52"],
      ["الإجمالي · مخصص", "2", "9"],
    ]);
    expect(texts("tfoot tr.report-total")).toEqual([["الإجمالي", "10", "61"]]);
    // Numbers sit at the end of their column, like the headings above them.
    expect([...doc.querySelectorAll("tfoot td")].map((td) => td.className)).toEqual(["", "num", "num"]);
    expect([...doc.querySelectorAll("thead th")].map((th) => th.className)).toEqual(["", "num", "num"]);
  });

  it("draws no totals row for a report with no totalled column", async () => {
    view = await render(<ReportView document={arabicDocument} />);
    expect(view.container.querySelector(".report-subtotal, .report-total")).toBeNull();
  });
});

describe("report addresses and lookups", () => {
  it("leaves out empty parameters, says 'no grouping' explicitly and prints Latin digits in English", () => {
    const report = catalog.items[0]!;
    expect(reportUrl(report, { status: "", groupBy: "-" }, "csv", "en", "arab", "role")).toBe("/api/reports/run/identity.usersByRole?groupBy=&format=csv&language=en&numerals=latn");
    expect(reportUrl(report, { groupBy: "email" }, "xlsx", "ar", "arab", "role")).toBe("/api/reports/run/identity.usersByRole?groupBy=email&format=xlsx&language=ar&numerals=arab");
  });

  it("names a looked-up record by its code and its name in the screen's language", () => {
    const row = { id: "c1", code: "DT", legalNameEn: "Demo Trading", legalNameAr: "ديمو للتجارة" };
    expect(lookupLabel(row, ["code", "legalNameEn", "legalNameAr"], "ar")).toBe("DT · ديمو للتجارة");
    expect(lookupLabel(row, ["code", "legalNameEn", "legalNameAr"], "en")).toBe("DT · Demo Trading");
  });
});
