import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "../api";
import type { ListGroup, ListPage, Row } from "./model";

export const chunkSize = 100;

type Chunks = {
  key: string;
  rows: Map<number, Row[]>;
  next: Map<number, string | null>;
  pending: Set<number>;
  controller: AbortController;
};

export type ListRows = {
  /** Rows matching the query (null until the first page arrives). */
  total: number | null;
  groups: ListGroup[] | null;
  error: ApiError | Error | null;
  /** True while the first page of the current query is loading. */
  loading: boolean;
  /** Increases whenever rows arrive (re-render trigger). */
  version: number;
  /** The query key the rows and total belong to. */
  loadedKey: string | null;
  rowAt: (index: number) => Row | undefined;
  /** Load every chunk covering rows [start, end). */
  ensure: (start: number, end: number) => void;
  /** Every row loaded so far, in order (for selection and copy). */
  loadedRows: () => Row[];
  reload: () => void;
};

/**
 * Pages of a registered list for a virtualised grid: rows are fetched in chunks of 100 as the
 * grid scrolls, by keyset (the previous chunk's <c>next</c>) when it is known and by offset when
 * the user jumps, and kept until the query changes. A changed query aborts what is in flight.
 */
export function useListRows(endpoint: string | null, query: URLSearchParams | null, groupBy: string | null): ListRows {
  const key = endpoint && query ? `${endpoint}?${query.toString()}|${groupBy ?? ""}` : null;
  // The query being loaded, and the one on screen: the previous rows stay visible until the
  // first page of a new query arrives, so typing in the search box never flashes an empty grid.
  const chunks = useRef<Chunks | null>(null);
  const shown = useRef<Chunks | null>(null);
  const [total, setTotal] = useState<number | null>(null);
  const [groups, setGroups] = useState<ListGroup[] | null>(null);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [loading, setLoading] = useState(false);
  const [version, setVersion] = useState(0);
  const [loadedKey, setLoadedKey] = useState<string | null>(null);
  const [reloads, setReloads] = useState(0);

  const fetchChunk = useCallback(
    (state: Chunks, index: number) => {
      if (!endpoint || !query || state.rows.has(index) || state.pending.has(index)) return;
      state.pending.add(index);
      const params = new URLSearchParams(query);
      params.set("take", String(chunkSize));
      const after = index > 0 ? state.next.get(index - 1) : undefined;
      if (after) params.set("after", after);
      else if (index > 0) params.set("skip", String(index * chunkSize));
      if (index === 0 && groupBy) params.set("groupBy", groupBy);
      api<ListPage>("GET", `${endpoint}?${params}`)
        .then((page) => {
          if (state.controller.signal.aborted || chunks.current !== state) return;
          state.rows.set(index, page.items);
          state.next.set(index, page.next);
          state.pending.delete(index);
          if (index === 0) {
            shown.current = state;
            setTotal(page.total);
            setGroups(page.groups ?? null);
            setLoading(false);
            setLoadedKey(state.key);
            setError(null);
          }
          setVersion((v) => v + 1);
        })
        .catch((e: Error) => {
          if (state.controller.signal.aborted || chunks.current !== state) return;
          state.pending.delete(index);
          setError(e);
          setLoading(false);
        });
    },
    [endpoint, query, groupBy],
  );

  useEffect(() => {
    if (!key) return;
    const state: Chunks = { key, rows: new Map(), next: new Map(), pending: new Set(), controller: new AbortController() };
    chunks.current = state;
    setLoading(true);
    fetchChunk(state, 0);
    return () => state.controller.abort();
  }, [key, fetchChunk, reloads]);

  const ensure = useCallback(
    (start: number, end: number) => {
      const state = chunks.current;
      if (!state || shown.current !== state) return;
      const first = Math.floor(start / chunkSize);
      const last = Math.floor(Math.max(start, end - 1) / chunkSize);
      for (let index = first; index <= last; index++) fetchChunk(state, index);
    },
    [fetchChunk],
  );

  const rowAt = useCallback((index: number) => shown.current?.rows.get(Math.floor(index / chunkSize))?.[index % chunkSize], []);

  const loadedRows = useCallback(() => {
    const state = shown.current;
    if (!state) return [];
    return [...state.rows.entries()].sort((a, b) => a[0] - b[0]).flatMap(([, rows]) => rows);
  }, []);

  const reload = useCallback(() => setReloads((r) => r + 1), []);

  return { total, groups, error, loading, version, loadedKey, rowAt, ensure, loadedRows, reload };
}
