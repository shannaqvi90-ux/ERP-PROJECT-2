import "./numbering.css";
import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { recordPath } from "../../kernel/router";
import type { ListPage, SequenceRow } from "./types";

/**
 * What the numbering module adds to the shell: G then N opens Document numbering; from anywhere the
 * list printed; and in the command palette the sequences by document type (English or Arabic),
 * company, prefix or next number ("purchase order", "PO-2026"), each opened in its list with its
 * form, the cursor in its prefix.
 */
export const extensions: ModuleExtensions = {
  actions: [
    { key: "numbering.sequences.print", labelKey: "numbering.action.print", keywordsKey: "numbering.action.printKeywords", permission: "numbering.sequences.read", path: "/numbering/sequences", command: "lists.print" },
  ],
  goTo: [{ key: "numbering.sequences", menu: "numbering.sequences", code: "KeyN" }],
  palette: [
    {
      key: "numbering.sequences",
      labelKey: "numbering.palette.sequences",
      permission: "numbering.sequences.read",
      minLength: 3,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<SequenceRow>>("GET", `/api/numbering/sequences?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((s) => ({
            id: s.id,
            title: `${language === "ar" ? s.nameAr || s.nameEn : s.nameEn || s.nameAr} · ${s.companyCode}`,
            subtitle: s.nextNumber,
            path: recordPath("/numbering/sequences", s.id, `${new URLSearchParams({ q: query })}`),
          })),
        };
      },
      showAll: (query) => `/numbering/sequences?${new URLSearchParams({ q: query })}`,
    },
  ],
};
