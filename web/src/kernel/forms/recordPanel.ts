import { useCallback, useMemo, useState } from "react";

/** The list panel's id while a new record is being created: every screen opens a new record at ?open=new. */
export const newRecord = "new";

/**
 * The open record of a list screen, in the list's details panel: an existing record (?open=id)
 * or a new one (?open=new), the same address on every screen. Saving a new record keeps its form on
 * screen (so "Saved" stays visible) while the address moves to the saved record's id; `reload`
 * tells the list to fetch its rows again.
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
  /** A record was saved (or created): keep its form, point the address at it, refresh the list. */
  const saved = useCallback((id: string) => {
    setOpenId(id);
    setReload((n) => n + 1);
  }, []);
  /** A record was deleted: close its panel and refresh the list. */
  const removed = useCallback(() => {
    setOpenId(null);
    setFormKey("");
    setReload((n) => n + 1);
  }, []);
  const refresh = useCallback(() => setReload((n) => n + 1), []);

  return { openId, formKey, reload, onOpenIdChange, startNew, saved, removed, refresh, isNew: openId === newRecord };
}
