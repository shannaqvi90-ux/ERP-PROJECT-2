import { useCallback, useMemo, useState } from "react";
import { api } from "../../kernel/api";

/** The record panel's id while a new record is being created (?open=new). */
export const newRecord = "new";

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

/**
 * The open record of a list screen, in the list's details panel: an existing record (?open=id)
 * or a new one (?open=new). Saving a new record keeps its form on screen (so "Saved." stays
 * visible) while the address moves to the saved record's id.
 */
export function useRecordPanel(canCreate: boolean) {
  const [openId, setOpenId] = useState<string | null>(() => {
    const open = new URLSearchParams(window.location.search).get("open");
    return open === newRecord && !canCreate ? null : open;
  });
  const [formKey, setFormKey] = useState(() => openId ?? "");
  const [reload, setReload] = useState(0);

  const onOpenIdChange = useCallback((id: string | null) => {
    setOpenId(id);
    setFormKey(id ?? "");
  }, []);
  const startNew = useMemo(
    () =>
      canCreate
        ? () => {
            setOpenId(newRecord);
            setFormKey(`${newRecord}-${Date.now()}`);
          }
        : undefined,
    [canCreate],
  );
  const saved = useCallback((id: string) => {
    setOpenId(id);
    setReload((n) => n + 1);
  }, []);

  return { openId, formKey, reload, onOpenIdChange, startNew, saved };
}

/** The list's search box, for the "/" shortcut (the list framework renders it). */
export const listSearch = {
  get current() {
    return document.querySelector<HTMLInputElement>(".list-search input");
  },
};
