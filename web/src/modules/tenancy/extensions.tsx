import "./tenancy.css";
import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { recordPath } from "../../kernel/router";
import type { ListPage } from "./records";
import { workplaceChanged, type BranchRow, type CompanyRow, type Workplace } from "./types";
import { WorkplaceSwitcher } from "./WorkplaceSwitcher";

/**
 * What the tenancy module adds to the shell: the working company and branch switcher in the top
 * bar, and in the command palette "work in …" for every company and branch the user may work in,
 * and the company and branch records themselves, found by code or by English or Arabic name and
 * opened in their list (narrowed to the record, its form open).
 */
export const extensions: ModuleExtensions = {
  topbar: [{ key: "tenancy.workplace", order: 10, permission: "tenancy.workplace.read", component: WorkplaceSwitcher }],
  palette: [
    {
      key: "tenancy.workplace",
      labelKey: "tenancy.workplace.switch",
      permission: "tenancy.workplace.switch",
      minLength: 0,
      search: async (query, { language, signal }) => {
        const workplace = await api<Workplace>("GET", "/api/tenancy/workplace", undefined, { signal });
        const needle = query.trim().toLowerCase();
        const name = (en: string, ar: string) => (language === "ar" ? ar || en : en || ar);
        return workplace.companies
          .flatMap((c) => c.branches.map((b) => ({ c, b })))
          .filter(({ c, b }) => !(c.id === workplace.companyId && b.id === workplace.branchId))
          .filter(({ c, b }) => !needle || `${c.code} ${b.code} ${c.legalNameEn} ${c.legalNameAr} ${b.nameEn} ${b.nameAr}`.toLowerCase().includes(needle))
          .slice(0, 8)
          .map(({ c, b }) => ({
            id: `tenancy.workplace:${c.id}:${b.id}`,
            title: `${c.code} · ${b.code}`,
            subtitle: `${name(c.legalNameEn, c.legalNameAr)} · ${name(b.nameEn, b.nameAr)}`,
            ltr: true,
            run: async () => {
              const result = await api<Workplace>("PUT", "/api/tenancy/workplace", { companyId: c.id, branchId: b.id });
              window.dispatchEvent(new CustomEvent(workplaceChanged, { detail: { companyId: result.companyId, branchId: result.branchId } }));
            },
          }));
      },
    },
    {
      key: "tenancy.companies",
      labelKey: "tenancy.menu.companies",
      permission: "tenancy.companies.read",
      minLength: 2,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<CompanyRow>>("GET", `/api/tenancy/companies?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((c) => ({
            id: c.id,
            title: language === "ar" ? c.legalNameAr || c.legalNameEn : c.legalNameEn || c.legalNameAr,
            subtitle: c.code,
            path: recordPath("/tenancy/companies", c.id, `${new URLSearchParams({ q: c.code })}`),
          })),
        };
      },
      showAll: (query) => `/tenancy/companies?${new URLSearchParams({ q: query })}`,
    },
    {
      key: "tenancy.branches",
      labelKey: "tenancy.menu.branches",
      permission: "tenancy.branches.read",
      minLength: 2,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<BranchRow>>("GET", `/api/tenancy/branches?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((b) => ({
            id: b.id,
            title: language === "ar" ? b.nameAr || b.nameEn : b.nameEn || b.nameAr,
            subtitle: `${b.companyCode} · ${b.code}`,
            path: recordPath("/tenancy/branches", b.id, `${new URLSearchParams({ q: b.code })}`),
          })),
        };
      },
      showAll: (query) => `/tenancy/branches?${new URLSearchParams({ q: query })}`,
    },
  ],
};
