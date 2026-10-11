import { api } from "../../kernel/api";
import type { CompanyRow } from "./types";

/** The record panel's id while a new record is being created (?open=new, the kernel's). */
export { newRecord } from "../../kernel/forms/recordPanel";

/** One page of a registered list (the list query contract). */
export type ListPage<T> = { items: T[]; total: number; next?: string | null };

/** Every row of a list (a short one, such as the companies a user may work in), page by page. */
export async function loadAll<T>(endpoint: string, filter?: string): Promise<T[]> {
  const rows: T[] = [];
  let after: string | null | undefined;
  for (let pages = 0; pages < 50; pages++) {
    const query = new URLSearchParams({ take: "200" });
    if (filter) query.set("filter", filter);
    if (after) query.set("after", after);
    const page = await api<ListPage<T>>("GET", `${endpoint}?${query}`);
    rows.push(...page.items);
    after = page.next;
    if (!after) break;
  }
  return rows;
}

/** The open record of a list screen (the shared kernel hook: ?open=id or ?open=new on every screen). */
export { useRecordPanel } from "../../kernel/forms/recordPanel";

/** The list's search box, for the "/" shortcut (the list framework renders it). */
export const listSearch = {
  get current() {
    return document.querySelector<HTMLInputElement>(".list-search input");
  },
};

/**
 * Whether the user works in every company of the workspace, as the server says on a companies
 * list row (everyCompany, the same on every row). Only they may create a company or change a
 * company code (the server answers 403 tenancy.companyNeedsEveryCompany otherwise). Fails closed:
 * no row means the user works in no company (a newly invited administrator before anyone grants
 * them one, or someone whose last company was taken away; every workspace has a company), and a
 * row that does not say so is not taken as yes (critic p02 round 9: reading an empty list as
 * "every company" offered New to every newly invited administrator, and the server refused it).
 */
export async function worksInEveryCompany(signal?: AbortSignal): Promise<boolean> {
  const page = await api<ListPage<CompanyRow>>("GET", "/api/tenancy/companies?take=1", undefined, { signal });
  const first = page.items[0];
  return first !== undefined && first.everyCompany === true;
}
