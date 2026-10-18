import { useEffect, useRef } from "react";

/**
 * A record changed by something other than the screen that shows it: the shell's one-step
 * language or digits switch (Alt+L, the top bar, the palette) changes the signed-in user's own
 * row, which the users list and an open user record may be showing (critic p04 round 10: the
 * user's own Language cell still said English after the switch to Arabic, until a reload). The
 * part that made the change announces it; a screen showing that kind of record refreshes it.
 *
 * Only in this page: nothing is stored or sent, and the detail is the record's kind and id, which
 * the screen listening already knows.
 */
export const recordChangedEvent = "erp:record-changed";

export type RecordChange = {
  /** The kind of record, named by the module that owns it ("identity.user"). */
  record: string;
  id: string;
};

export function announceRecordChanged(change: RecordChange): void {
  window.dispatchEvent(new CustomEvent<RecordChange>(recordChangedEvent, { detail: { record: change.record, id: change.id } }));
}

/** Calls `onChanged` with the record's id whenever a record of this kind is announced as changed. */
export function useRecordChanged(record: string, onChanged: (id: string) => void): void {
  const handler = useRef(onChanged);
  handler.current = onChanged;
  useEffect(() => {
    const listener = (event: Event) => {
      const detail = (event as CustomEvent<RecordChange>).detail;
      if (detail && detail.record === record && typeof detail.id === "string") handler.current(detail.id);
    };
    window.addEventListener(recordChangedEvent, listener);
    return () => window.removeEventListener(recordChangedEvent, listener);
  }, [record]);
}
