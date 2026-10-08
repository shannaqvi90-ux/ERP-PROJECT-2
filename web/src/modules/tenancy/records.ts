import { api } from "../../kernel/api";

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
