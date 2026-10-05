import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { api, ApiError } from "../api";
import { useI18n } from "../i18n";
import { cellText, columnLabel, conditionLabel, formatValue, type Formatters } from "./format";
import {
  defaultColumns,
  initialState,
  queryKey,
  queryOf,
  rowsToText,
  searchFromAddress,
  sortText,
  stateFromAddress,
  stateFromView,
  stateToAddress,
  toggleSort,
  visibleRange,
  type Condition,
  type ListColumn,
  type ListDefinition,
  type ListGroup,
  type ListState,
  type Row,
  type SavedView,
} from "./model";
import { ColumnChooser, FilterEditor, Popover, SaveViewDialog, ViewsMenu, type ViewChoice } from "./parts";
import { useListRows } from "./useListRows";
import "./lists.css";

export type BulkAction = {
  key: string;
  labelKey: string;
  /** Shown only to users holding it (the API enforces it anyway). */
  permission?: string;
  run: (rows: Row[]) => Promise<void> | void;
};

/** What a screen knows about the records a reference column points to: their labels (cells,
 * groups, filter chips) and, for a short set, the choices the column's filter offers. */
export type ReferenceSource = {
  label: (id: string) => string | undefined;
  options?: { value: string; label: string }[];
};

export type ListViewProps = {
  /** Registered list key, for example "identity.users". */
  listKey: string;
  titleKey: string;
  /** Plural message with {count} for the number of matching rows. */
  countKey: string;
  searchPlaceholderKey: string;
  /** Cell renderers for columns that need more than the default format. */
  renderCell?: Partial<Record<string, (row: Row) => ReactNode>>;
  /** Open a record (Enter, double click). Without it a details panel shows every column. */
  onOpen?: (row: Row) => void;
  bulkActions?: BulkAction[];
  /** Screen actions shown in the toolbar (for example "New user"). */
  actions?: ReactNode;
  can?: (permission: string) => boolean;
  /** A single click on a row opens it too (screens with a side panel). */
  openOnClick?: boolean;
  /**
   * The screen's own content for the details panel of the open record (?open=id), for example
   * an editor; it gets the record's id. Without it the panel shows every column of the row.
   */
  renderRecord?: (id: string, close: () => void) => ReactNode;
  /** The open record's id, when the screen controls it (for example to open a record it just
   * created); onOpenIdChange hears every change the list makes. */
  openId?: string | null;
  onOpenIdChange?: (id: string | null) => void;
  /** Change it to fetch the rows again (after the screen saved a record). */
  reloadKey?: number | string;
  /** Labels (and filter choices) of reference columns, by column key. */
  references?: Partial<Record<string, ReferenceSource>>;
};

/** Address parameters the list owns; any other parameter belongs to the screen and is kept. */
const listParams = new Set(["view", "q", "search", "filter", "sort", "group", "cols", "open"]);

const rowHeight = 28;
const searchDelay = 150;

/**
 * The list screen every module reuses: quick search as you type, sort and filter from the column
 * headers, grouping with counts and totals, column chooser, built-in, shared and personal views,
 * a virtualised grid over any number of rows with keyboard navigation and selection, bulk
 * actions, and opening a record from the keyboard. The state lives in the address, so a link or a
 * reload opens the same list.
 */
export function ListView(props: ListViewProps) {
  const { listKey, titleKey, countKey, searchPlaceholderKey } = props;
  const i18n = useI18n();
  const { t } = i18n;
  const references = props.references;
  const formatters: Formatters = useMemo(
    () => ({
      t,
      formatDateTime: i18n.formatDateTime,
      formatDate: i18n.format.date,
      formatNumber: i18n.formatNumber,
      formatDecimal: i18n.format.decimal,
      reference: references ? (column: string, value: string) => references[column]?.label(value) : undefined,
    }),
    [t, i18n.formatDateTime, i18n.formatNumber, i18n.format, references],
  );
  const id = "list" + useId().replace(/[^a-zA-Z0-9_-]/g, "");

  const [definition, setDefinition] = useState<ListDefinition | null>(null);
  const [views, setViews] = useState<SavedView[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [state, setState] = useState<ListState | null>(null);
  const [baseline, setBaseline] = useState<string>("");
  // The search box works before the definition arrives, so typing right away is never lost.
  const [searchText, setSearchText] = useState(() => searchFromAddress(window.location.search));
  const [appliedSearch, setAppliedSearch] = useState(searchText);
  const [ownOpenId, setOwnOpenId] = useState<string | null>(null);
  const openId = props.openId !== undefined ? props.openId : ownOpenId;
  const onOpenIdChange = props.onOpenIdChange;
  const setOpenId = useCallback(
    (next: string | null) => {
      setOwnOpenId(next);
      onOpenIdChange?.(next);
    },
    [onOpenIdChange],
  );
  const [openRow, setOpenRow] = useState<Row | null>(null);
  const [active, setActive] = useState(0);
  const [selected, setSelected] = useState<Map<string, Row>>(new Map());
  const [anchor, setAnchor] = useState<number | null>(null);
  const [menu, setMenu] = useState<null | { kind: "column"; column: string } | { kind: "filter"; column: string } | { kind: "columns" } | { kind: "views" } | { kind: "save" }>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewport, setViewport] = useState(600);
  const searchRef = useRef<HTMLInputElement>(null);
  const gridRef = useRef<HTMLDivElement>(null);
  const tableRef = useRef<HTMLTableElement>(null);
  const openWhenSingle = useRef(false);

  // Definition and saved views, then the starting state: the address, else the user's default
  // view, else a shared default, else the list's standard state.
  useEffect(() => {
    let cancelled = false;
    Promise.all([
      api<ListDefinition>("GET", `/api/lists/${listKey}/definition`),
      api<{ items: SavedView[] }>("GET", `/api/lists/${listKey}/views`).catch(() => ({ items: [] as SavedView[] })),
    ])
      .then(([loaded, saved]) => {
        if (cancelled) return;
        setDefinition(loaded);
        setViews(saved.items);
        const address = stateFromAddress(window.location.search, loaded);
        let start: ListState;
        if (address.present) {
          start = address.state;
        } else {
          const preferred = saved.items.find((v) => v.isMine && v.isDefault) ?? saved.items.find((v) => v.isShared && v.isDefault);
          start = preferred ? stateFromView(loaded, preferred, `view:${preferred.id}`) : initialState(loaded);
        }
        const typed = searchRef.current?.value ?? "";
        if (typed && !address.present) start = { ...start, search: typed };
        setState(start);
        setSearchText(start.search);
        setAppliedSearch(start.search);
        setBaseline(fingerprint(start));
        setOpenId(address.open);
      })
      .catch((e: Error) => !cancelled && setLoadError(e.message));
    return () => {
      cancelled = true;
    };
  }, [listKey]);

  // Search as you type, a moment after the last key.
  useEffect(() => {
    if (searchText === appliedSearch) return;
    const timer = window.setTimeout(() => setAppliedSearch(searchText), searchDelay);
    return () => window.clearTimeout(timer);
  }, [searchText, appliedSearch]);

  const current: ListState | null = useMemo(() => (state ? { ...state, search: appliedSearch } : null), [state, appliedSearch]);
  const currentKey = current ? queryKey(current) : null;
  // A stable query object: rebuilt only when the rows it selects change.
  const query = useMemo(() => (current ? queryOf(current) : null), [currentKey]);
  const rows = useListRows(definition?.endpoint ?? null, query, current?.groupBy ?? null);
  const expectedKey = definition && query ? `${definition.endpoint}?${query.toString()}|${current?.groupBy ?? ""}` : null;
  const total = rows.total ?? 0;
  const grouped = Boolean(current?.groupBy);

  // Keep the address in step with the state, keeping parameters the screen owns.
  useEffect(() => {
    if (!current || !definition) return;
    const params = new URLSearchParams(stateToAddress(current, definition, openId));
    new URLSearchParams(window.location.search).forEach((value, key) => {
      if (!listParams.has(key)) params.append(key, value);
    });
    const text = params.toString();
    const address = window.location.pathname + (text ? `?${text}` : "");
    if (address !== window.location.pathname + window.location.search) window.history.replaceState(null, "", address);
  }, [current, definition, openId]);

  // Start with the cursor in the search box (after the shell has placed focus on the screen, or
  // while focus is still on the menu link that opened it), unless the address opens a record,
  // whose panel takes focus.
  useEffect(() => {
    if (new URLSearchParams(window.location.search).has("open")) return;
    const timer = window.setTimeout(() => {
      const focused = document.activeElement;
      if (!focused || focused === document.body || focused.id === "main" || focused.tagName === "MAIN" || !!focused.closest("nav"))
        searchRef.current?.focus();
    }, 0);
    return () => window.clearTimeout(timer);
  }, []);

  // Grouping shows groups, not rows: nothing stays selected.
  useEffect(() => {
    setSelected(new Map());
  }, [current?.groupBy]);

  // A new query starts at the top.
  useEffect(() => {
    setActive(0);
    setScrollTop(0);
    setAnchor(null);
    if (gridRef.current) gridRef.current.scrollTop = 0;
  }, [rows.loadedKey]);

  // Enter pressed in the search box before the results arrived: act on them once they are in.
  useEffect(() => {
    if (!openWhenSingle.current || !current || rows.loadedKey === null || rows.loadedKey !== expectedKey) return;
    if (current.search !== searchText) return;
    openWhenSingle.current = false;
    afterSearchEnter();
  });

  // Notices (saved, copied) fade after a few seconds.
  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(null), 4000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const range = useMemo(() => visibleRange(scrollTop, viewport, rowHeight, grouped ? 0 : total), [scrollTop, viewport, total, grouped]);
  const { ensure } = rows;
  useEffect(() => {
    if (!grouped) ensure(range.start, range.end);
  }, [range.start, range.end, ensure, grouped, rows.loadedKey]);

  useLayoutEffect(() => {
    const grid = gridRef.current;
    if (!grid) return;
    const measure = () => setViewport(grid.clientHeight > 0 ? grid.clientHeight - rowHeight : 600);
    measure();
    window.addEventListener("resize", measure);
    return () => window.removeEventListener("resize", measure);
  }, [definition]);

  // The record shown in the details panel follows the address (?open=id).
  useEffect(() => {
    if (!openId || props.renderRecord) {
      setOpenRow(null);
      return;
    }
    const loaded = rows.loadedRows().find((r) => r.id === openId);
    if (loaded) {
      setOpenRow(loaded);
    } else if (definition && /^[0-9a-f-]{36}$/i.test(openId)) {
      // A record opened from a link: the list's own endpoint serves one row by id.
      api<Row>("GET", `${definition.endpoint}/${openId}`)
        .then((row) => setOpenRow(row))
        .catch(() => setOpenRow(null));
    }
  }, [openId, rows.version, definition, rows.loadedRows, props.renderRecord]);

  // The screen asks for fresh rows (a record was created, saved or deleted).
  const { reload } = rows;
  const reloadKey = props.reloadKey;
  const lastReloadKey = useRef(reloadKey);
  useEffect(() => {
    if (lastReloadKey.current === reloadKey) return;
    lastReloadKey.current = reloadKey;
    reload();
  }, [reloadKey, reload]);

  const update = useCallback((change: (s: ListState) => ListState) => setState((s) => (s ? change(s) : s)), []);

  if (loadError) {
    return (
      <section className="list-screen">
        <h1>{t(titleKey)}</h1>
        <div className="alert" role="alert">
          {t("lists.error", { message: loadError })}
        </div>
      </section>
    );
  }

  const column = (key: string): ListColumn | undefined => definition?.columns.find((c) => c.key === key);
  const visible = (current?.columns ?? []).map(column).filter((c): c is ListColumn => Boolean(c));
  const modified = current ? fingerprint(current) !== baseline : false;
  const selectedView = current?.view?.startsWith("view:") ? views.find((v) => `view:${v.id}` === current.view) : undefined;
  const canChange = selectedView ? selectedView.isMine || (selectedView.isShared && Boolean(definition?.canShare)) : false;

  const recordOpen = props.renderRecord ? Boolean(openId) : Boolean(openRow);

  function open(row: Row) {
    if (props.onOpen) {
      props.onOpen(row);
      return;
    }
    setOpenRow(row);
    setOpenId(row.id);
  }

  function closeRecord() {
    setOpenId(null);
    setOpenRow(null);
    tableRef.current?.focus();
  }

  function afterSearchEnter() {
    if (!grouped && rows.total === 1) {
      const only = rows.rowAt(0);
      if (only) {
        open(only);
        return;
      }
    }
    focusGrid(0);
  }

  function focusGrid(index: number) {
    setActive(Math.max(0, Math.min(index, Math.max(0, (grouped ? (rows.groups?.length ?? 0) : total) - 1))));
    tableRef.current?.focus();
  }

  function scrollToRow(index: number) {
    const grid = gridRef.current;
    if (!grid) return;
    const top = index * rowHeight;
    const bottom = top + rowHeight;
    if (top < grid.scrollTop) grid.scrollTop = top;
    else if (bottom > grid.scrollTop + viewport) grid.scrollTop = bottom - viewport;
    setScrollTop(grid.scrollTop);
  }

  function moveTo(index: number, extend: boolean) {
    const count = grouped ? (rows.groups?.length ?? 0) : total;
    if (count === 0) return;
    const next = Math.max(0, Math.min(count - 1, index));
    setActive(next);
    scrollToRow(next);
    if (extend && !grouped) {
      const from = anchor ?? active;
      setAnchor(from);
      const lo = Math.min(from, next);
      const hi = Math.max(from, next);
      const map = new Map(selected);
      for (let i = lo; i <= hi; i++) {
        const row = rows.rowAt(i);
        if (row) map.set(row.id, row);
      }
      setSelected(map);
    } else if (!extend) {
      setAnchor(null);
    }
  }

  function toggleSelected(row: Row) {
    const map = new Map(selected);
    if (map.has(row.id)) map.delete(row.id);
    else map.set(row.id, row);
    setSelected(map);
  }

  function drill(group: ListGroup) {
    if (!current?.groupBy) return;
    const key = current.groupBy;
    const condition: Condition = group.key === null ? { column: key, op: "isNull", values: [] } : { column: key, op: "eq", values: [group.key] };
    update((s) => ({ ...s, groupBy: null, conditions: [...s.conditions.filter((c) => c.column !== key), condition] }));
  }

  function onGridKey(event: KeyboardEvent<HTMLTableElement>) {
    const page = Math.max(1, Math.floor(viewport / rowHeight) - 1);
    const count = grouped ? (rows.groups?.length ?? 0) : total;
    switch (event.key) {
      case "ArrowDown":
        moveTo(active + 1, event.shiftKey);
        break;
      case "ArrowUp":
        if (active === 0 && !event.shiftKey) {
          searchRef.current?.focus();
          break;
        }
        moveTo(active - 1, event.shiftKey);
        break;
      case "PageDown":
        moveTo(active + page, event.shiftKey);
        break;
      case "PageUp":
        moveTo(active - page, event.shiftKey);
        break;
      case "Home":
        moveTo(0, event.shiftKey);
        break;
      case "End":
        moveTo(count - 1, event.shiftKey);
        break;
      case "Enter": {
        if (grouped) {
          const group = rows.groups?.[active];
          if (group) drill(group);
        } else {
          const row = rows.rowAt(active);
          if (row) open(row);
        }
        break;
      }
      case " ": {
        const row = grouped ? undefined : rows.rowAt(active);
        if (row) toggleSelected(row);
        break;
      }
      case "Escape":
        if (recordOpen) closeRecord();
        else if (selected.size > 0) setSelected(new Map());
        else searchRef.current?.focus();
        break;
      case "a":
      case "A":
        if (!(event.ctrlKey || event.metaKey) || grouped) return;
        setSelected(new Map(rows.loadedRows().map((r) => [r.id, r])));
        break;
      case "/":
        searchRef.current?.focus();
        searchRef.current?.select();
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  function onSearchKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter") {
      event.preventDefault();
      if (searchText !== appliedSearch || rows.loading) {
        openWhenSingle.current = true;
        setAppliedSearch(searchText);
      } else {
        afterSearchEnter();
      }
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      focusGrid(0);
    } else if (event.key === "Escape" && recordOpen) {
      event.preventDefault();
      closeRecord();
    } else if (event.key === "Escape" && searchText) {
      event.preventDefault();
      setSearchText("");
      setAppliedSearch("");
    }
  }

  async function copySelected() {
    if (!definition) return;
    const list = [...selected.values()];
    const keys = visible.map((c) => c.key);
    const text = rowsToText(list, keys, visible.map((c) => t(c.labelKey)), (row, key) => cellText(definition, row, key, formatters));
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Clipboard refused (insecure context): nothing else to do; the rows stay selected.
    }
    setNotice(t("lists.bulk.copied", { count: list.length }));
  }

  async function saveView(name: string, shared: boolean, isDefault: boolean) {
    if (!current || !definition) return;
    try {
      const saved = await api<SavedView>("POST", `/api/lists/${listKey}/${shared ? "shared-views" : "views"}`, viewBody(current, name, isDefault));
      const refreshed = await api<{ items: SavedView[] }>("GET", `/api/lists/${listKey}/views`);
      setViews(refreshed.items);
      const next = { ...current, view: `view:${saved.id}` };
      setState(next);
      setBaseline(fingerprint(next));
      setMenu(null);
      setSaveError(null);
      setNotice(t("lists.views.saved"));
    } catch (e) {
      setSaveError(e instanceof ApiError ? (Object.values(e.fieldErrors)[0]?.[0]?.message ?? e.message) : (e as Error).message);
    }
  }

  async function updateView() {
    if (!current || !selectedView) return;
    const path = `/api/lists/${listKey}/${selectedView.isShared ? "shared-views" : "views"}/${selectedView.id}`;
    try {
      const saved = await api<SavedView>("PUT", path, { ...viewBody(current, selectedView.name, selectedView.isDefault), version: selectedView.version });
      setViews((list) => list.map((v) => (v.id === saved.id ? saved : v)));
      setBaseline(fingerprint(current));
      setNotice(t("lists.views.saved"));
    } catch (e) {
      setNotice((e as Error).message);
    }
  }

  async function deleteView() {
    if (!selectedView || !definition) return;
    if (!window.confirm(t("lists.views.deleteConfirm", { name: selectedView.name }))) return;
    try {
      await api<void>("DELETE", `/api/lists/${listKey}/${selectedView.isShared ? "shared-views" : "views"}/${selectedView.id}`);
      setViews((list) => list.filter((v) => v.id !== selectedView.id));
      pickView(null);
      setNotice(t("lists.views.deleted"));
    } catch (e) {
      setNotice((e as Error).message);
    }
  }

  function pickView(choice: string | null) {
    if (!definition) return;
    let next: ListState;
    if (choice === null) {
      next = initialState(definition);
    } else if (choice.startsWith("preset:")) {
      const preset = definition.presets.find((p) => `preset:${p.key}` === choice);
      next = preset ? stateFromView(definition, preset, choice) : initialState(definition);
    } else {
      const view = views.find((v) => `view:${v.id}` === choice);
      next = view ? stateFromView(definition, view, choice) : initialState(definition);
    }
    setState(next);
    setSearchText(next.search);
    setAppliedSearch(next.search);
    setBaseline(fingerprint(next));
    setSelected(new Map());
  }

  const viewChoices: ViewChoice[] = definition
    ? [
        ...definition.presets.map((p) => ({ id: `preset:${p.key}`, label: t(p.labelKey), group: "builtIn" as const, isDefault: false })),
        ...views.filter((v) => v.isShared).map((v) => ({ id: `view:${v.id}`, label: v.name, group: "shared" as const, isDefault: v.isDefault })),
        ...views.filter((v) => v.isMine).map((v) => ({ id: `view:${v.id}`, label: v.name, group: "mine" as const, isDefault: v.isDefault })),
      ]
    : [];
  const viewName = current?.view ? (viewChoices.find((c) => c.id === current.view)?.label ?? t("lists.views.standard")) : t("lists.views.standard");

  const columnsTemplate = `2.25rem ${visible.map((c) => width(c)).join(" ")}`;
  const allowedBulk = (props.bulkActions ?? []).filter((a) => !a.permission || (props.can ? props.can(a.permission) : true));
  const groups = rows.groups ?? [];
  const totalsColumns = (definition?.columns ?? []).filter((c) => c.aggregate);
  const activeRowId = grouped ? `${id}-group-${active}` : `${id}-row-${active}`;
  const listName = t(titleKey);

  const rowsToRender: ReactNode[] = [];
  if (!grouped && definition) {
    for (let index = range.start; index < range.end; index++) {
      const row = rows.rowAt(index);
      const isSelected = row ? selected.has(row.id) : false;
      rowsToRender.push(
        <tr
          key={row?.id ?? `pending-${index}`}
          id={`${id}-row-${index}`}
          role="row"
          aria-rowindex={index + 2}
          aria-selected={isSelected}
          className={`list-row${index === active ? " is-active" : ""}${isSelected ? " is-selected" : ""}`}
          style={{ transform: `translateY(${index * rowHeight}px)`, gridTemplateColumns: columnsTemplate }}
          onMouseDown={() => setActive(index)}
          onClick={(e) => {
            if (props.openOnClick && row && !(e.target instanceof HTMLInputElement)) open(row);
          }}
          onDoubleClick={() => row && open(row)}
        >
          <td role="gridcell" className="list-cell list-select">
            {row && (
              <input
                type="checkbox"
                tabIndex={-1}
                aria-label={t("lists.selection.row")}
                checked={isSelected}
                onChange={() => toggleSelected(row)}
              />
            )}
          </td>
          {visible.map((c) => (
            <td key={c.key} role="gridcell" className={`list-cell type-${c.type}`} dir={c.type === "reference" && !references?.[c.key] ? "ltr" : undefined}>
              {row ? (props.renderCell?.[c.key]?.(row) ?? formatValue(c, row[c.key], formatters)) : index === range.start ? t("lists.loading") : ""}
            </td>
          ))}
        </tr>,
      );
    }
  }

  return (
    <section className="list-screen" aria-busy={rows.loading}>
      <div className="list-toolbar screen-header">
        <h1>{t(titleKey)}</h1>
        <div className="list-search">
          <input
            ref={searchRef}
            type="search"
            className="search"
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            onKeyDown={onSearchKey}
            placeholder={t(searchPlaceholderKey)}
            aria-label={t(searchPlaceholderKey)}
            aria-describedby={`${id}-search-hint`}
            aria-controls={`${id}-grid`}
            autoComplete="off"
            spellCheck={false}
          />
          <span id={`${id}-search-hint`} className="visually-hidden">
            {t("lists.search.hint")}
          </span>
        </div>
        <span className="muted list-count" aria-live="polite">
          {rows.total !== null && t(countKey, { count: rows.total })}
        </span>
        <div className="list-toolbar-end">
          <div className="list-anchor">
            <button type="button" className="button" aria-haspopup="menu" aria-expanded={menu?.kind === "views"} onClick={() => setMenu(menu?.kind === "views" ? null : { kind: "views" })}>
              {t("lists.views.current", { name: viewName })}
              {modified && <span className="list-modified"> · {t("lists.views.modified")}</span>}
            </button>
            {menu?.kind === "views" && (
              <ViewsMenu
                choices={viewChoices}
                current={current?.view ?? null}
                modified={modified}
                canUpdate={canChange}
                canDelete={canChange}
                onPick={pickView}
                onSaveAs={() => {
                  setSaveError(null);
                  setMenu({ kind: "save" });
                }}
                onUpdate={() => void updateView()}
                onDelete={() => void deleteView()}
                onClose={() => setMenu((m) => (m?.kind === "views" ? null : m))}
              />
            )}
            {menu?.kind === "save" && (
              <SaveViewDialog canShare={Boolean(definition?.canShare)} error={saveError} onSave={(n, s, d) => void saveView(n, s, d)} onClose={() => setMenu(null)} />
            )}
          </div>
          <div className="list-anchor">
            <button type="button" className="button" aria-haspopup="dialog" aria-expanded={menu?.kind === "columns"} onClick={() => setMenu(menu?.kind === "columns" ? null : { kind: "columns" })}>
              {t("lists.columns.open")}
            </button>
            {menu?.kind === "columns" && definition && current && (
              <ColumnChooser
                definition={definition}
                columns={current.columns}
                onChange={(columns) => update((s) => ({ ...s, columns }))}
                onReset={() => update((s) => ({ ...s, columns: defaultColumns(definition) }))}
                onClose={() => setMenu(null)}
              />
            )}
          </div>
          {props.actions}
        </div>
      </div>

      {current && definition && (current.conditions.length > 0 || current.baseFilter || current.groupBy) && (
        <ul className="list-chips" aria-label={t("lists.filter.active")}>
          {current.baseFilter && (
            <li className="list-chip">
              <span>{t("lists.filter.view")}</span>
              <button type="button" className="list-chip-remove" aria-label={t("lists.filter.remove", { label: t("lists.filter.view") })} onClick={() => update((s) => ({ ...s, baseFilter: null }))}>
                ×
              </button>
            </li>
          )}
          {current.conditions.map((condition, index) => {
            const label = conditionLabel(definition, condition, formatters);
            return (
              <li key={`${condition.column}-${index}`} className="list-chip">
                <span>{label}</span>
                <button
                  type="button"
                  className="list-chip-remove"
                  aria-label={t("lists.filter.remove", { label })}
                  onClick={() => update((s) => ({ ...s, conditions: s.conditions.filter((_, i) => i !== index) }))}
                >
                  ×
                </button>
              </li>
            );
          })}
          {current.groupBy && (
            <li className="list-chip">
              <span>{t("lists.group.chip", { column: columnLabel(definition, current.groupBy, t) })}</span>
              <button type="button" className="list-chip-remove" aria-label={t("lists.group.clear")} onClick={() => update((s) => ({ ...s, groupBy: null }))}>
                ×
              </button>
            </li>
          )}
          {(current.conditions.length > 0 || current.baseFilter) && (
            <li>
              <button type="button" className="button link" onClick={() => update((s) => ({ ...s, conditions: [], baseFilter: null }))}>
                {t("lists.filter.clearAll")}
              </button>
            </li>
          )}
        </ul>
      )}

      {selected.size > 0 && (
        <div className="list-selectionbar" role="region" aria-label={t("lists.selection.count", { count: selected.size })}>
          <span>{t("lists.selection.count", { count: selected.size })}</span>
          <button type="button" className="button" onClick={() => void copySelected()}>
            {t("lists.bulk.copy")}
          </button>
          {allowedBulk.map((action) => (
            <button key={action.key} type="button" className="button" onClick={() => void Promise.resolve(action.run([...selected.values()])).then(() => rows.reload())}>
              {t(action.labelKey)}
            </button>
          ))}
          <button type="button" className="button link" onClick={() => setSelected(new Map())}>
            {t("lists.selection.clear")}
          </button>
        </div>
      )}

      {notice && (
        <div className="list-notice" role="status">
          {notice}
        </div>
      )}
      {rows.error && (
        <div className="alert" role="alert">
          {rows.error.message}
          <button type="button" className="button link" onClick={rows.reload}>
            {t("lists.retry")}
          </button>
        </div>
      )}

      {/* On paper the list holds the rows on screen: the printout says which, of how many. */}
      {!grouped && rows.total !== null && rows.total > range.end - range.start && range.end > range.start && (
        <p className="print-only list-print-scope">
          {t("lists.print.partial", { from: i18n.formatNumber(range.start + 1), to: i18n.formatNumber(range.end), total: i18n.formatNumber(rows.total) })}
        </p>
      )}
      <div className={`list-body${recordOpen ? " has-record" : ""}`}>
        <div ref={gridRef} className="list-scroll" onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}>
        <table
          ref={tableRef}
          id={`${id}-grid`}
          className="list-grid"
          role="grid"
          tabIndex={0}
          aria-label={t("lists.grid", { list: listName, count: grouped ? groups.length : total })}
          aria-rowcount={(grouped ? groups.length : total) + 1}
          aria-colcount={visible.length + 1}
          aria-multiselectable={!grouped}
          aria-activedescendant={(grouped ? groups.length : total) > 0 ? activeRowId : undefined}
          onKeyDown={onGridKey}
        >
          <thead role="rowgroup" className="list-head">
          {grouped && current?.groupBy ? (
            <tr role="row" aria-rowindex={1} className="list-header list-group-header">
              <th role="columnheader" className="list-cell">
                {columnLabel(definition!, current.groupBy, t)}
              </th>
              <th role="columnheader" className="list-cell">
                {t("lists.group.rows")}
              </th>
              {totalsColumns.map((c) => (
                <th key={c.key} role="columnheader" className="list-cell type-number">
                  {t("lists.group.total", { column: t(c.labelKey) })}
                </th>
              ))}
            </tr>
          ) : (
          <tr role="row" aria-rowindex={1} className="list-header" style={{ gridTemplateColumns: columnsTemplate }}>
            <th role="columnheader" className="list-cell list-select">
              {!grouped && (
                <input
                  type="checkbox"
                  tabIndex={-1}
                  aria-label={t("lists.selection.all")}
                  checked={selected.size > 0 && selected.size >= rows.loadedRows().length}
                  onChange={(e) => setSelected(e.target.checked ? new Map(rows.loadedRows().map((r) => [r.id, r])) : new Map())}
                />
              )}
            </th>
            {visible.map((c, position) => {
              const sortIndex = current?.sort.findIndex((k) => k.column === c.key) ?? -1;
              const sortKey = sortIndex >= 0 ? current!.sort[sortIndex] : undefined;
              const filtered = current?.conditions.some((x) => x.column === c.key);
              const label = t(c.labelKey);
              return (
                <th
                  key={c.key}
                  role="columnheader"
                  className={`list-cell list-headcell type-${c.type}${filtered ? " is-filtered" : ""}`}
                  aria-sort={sortIndex === 0 ? (sortKey!.descending ? "descending" : "ascending") : undefined}
                >
                  {c.sortable ? (
                    <button
                      type="button"
                      className="list-sort"
                      title={t("lists.sort.hint")}
                      onClick={(e) => update((s) => ({ ...s, sort: toggleSort(s.sort, c.key, e.shiftKey) }))}
                    >
                      {label}
                      {sortKey && (
                        <span className="list-sort-mark" aria-label={t(sortKey.descending ? "lists.sort.state.descending" : "lists.sort.state.ascending")}>
                          {sortKey.descending ? "▼" : "▲"}
                          {current && current.sort.length > 1 ? sortIndex + 1 : ""}
                        </span>
                      )}
                    </button>
                  ) : (
                    <span className="list-headlabel">{label}</span>
                  )}
                  {(c.sortable || c.filterable || c.groupable) && (
                    <span className={`list-anchor${position >= visible.length / 2 ? " end" : ""}`}>
                      <button
                        type="button"
                        className="list-colmenu"
                        aria-haspopup="menu"
                        aria-expanded={menu?.kind === "column" && menu.column === c.key}
                        aria-label={t("lists.columnMenu", { column: label })}
                        onClick={() => setMenu(menu?.kind === "column" && menu.column === c.key ? null : { kind: "column", column: c.key })}
                      >
                        {filtered ? "◆" : "▾"}
                      </button>
                      {menu?.kind === "column" && menu.column === c.key && (
                        <Popover label={t("lists.columnMenu", { column: label })} role="menu" onClose={() => setMenu(null)} className="list-colpopover">
                          {c.sortable && (
                            <>
                              <button type="button" role="menuitem" className="list-menuitem" onClick={() => { update((s) => ({ ...s, sort: [{ column: c.key, descending: false }] })); setMenu(null); }}>
                                {t("lists.sort.ascending")}
                              </button>
                              <button type="button" role="menuitem" className="list-menuitem" onClick={() => { update((s) => ({ ...s, sort: [{ column: c.key, descending: true }] })); setMenu(null); }}>
                                {t("lists.sort.descending")}
                              </button>
                            </>
                          )}
                          {c.filterable && (
                            <button type="button" role="menuitem" className="list-menuitem" onClick={() => setMenu({ kind: "filter", column: c.key })}>
                              {t("lists.filter.open")}
                            </button>
                          )}
                          {c.groupable && (
                            <button type="button" role="menuitem" className="list-menuitem" onClick={() => { update((s) => ({ ...s, groupBy: c.key })); setMenu(null); }}>
                              {t("lists.group.by")}
                            </button>
                          )}
                          {visible.length > 1 && (
                            <button type="button" role="menuitem" className="list-menuitem" onClick={() => { update((s) => ({ ...s, columns: s.columns.filter((k) => k !== c.key) })); setMenu(null); }}>
                              {t("lists.column.hide")}
                            </button>
                          )}
                        </Popover>
                      )}
                      {menu?.kind === "filter" && menu.column === c.key && current && (
                        <FilterEditor
                          column={c}
                          options={c.type === "reference" ? references?.[c.key]?.options : undefined}
                          current={current.conditions.filter((x) => x.column === c.key)}
                          onApply={(conditions) => update((s) => ({ ...s, conditions: [...s.conditions.filter((x) => x.column !== c.key), ...conditions] }))}
                          onClose={() => setMenu(null)}
                        />
                      )}
                    </span>
                  )}
                </th>
              );
            })}
          </tr>
          )}
          </thead>

          {grouped ? (
            <tbody role="rowgroup" className="list-groups">
              {groups.map((group, index) => {
                const groupColumn = column(current!.groupBy!);
                const label = group.key === null ? t("lists.group.empty") : groupColumn ? formatValue(groupColumn, group.key, formatters) : String(group.key);
                return (
                  <tr
                    key={`${String(group.key)}-${index}`}
                    id={`${id}-group-${index}`}
                    role="row"
                    aria-rowindex={index + 2}
                    className={`list-group${index === active ? " is-active" : ""}`}
                    onMouseDown={() => setActive(index)}
                    onDoubleClick={() => drill(group)}
                  >
                    <td role="gridcell" className="list-cell list-group-key">
                      <button type="button" tabIndex={-1} className="button link" title={t("lists.group.open")} onClick={() => drill(group)}>
                        {label}
                      </button>
                    </td>
                    <td role="gridcell" className="list-cell list-group-count">
                      {t("lists.group.count", { count: group.count })}
                    </td>
                    {totalsColumns.map((c) => (
                      <td key={c.key} role="gridcell" className="list-cell type-number">
                        {formatValue(c, group.totals?.[c.key] ?? "0", formatters)}
                      </td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          ) : (
            <tbody role="rowgroup" className="list-rows" style={{ height: total * rowHeight }}>
              {rowsToRender}
            </tbody>
          )}
        </table>
          {!rows.loading && rows.total === 0 && (
            <div className="list-empty" role="status">
              {t("lists.empty")}
            </div>
          )}
        </div>

        {props.renderRecord && openId && (
          <aside
            className="list-record"
            role="region"
            aria-label={t("lists.record.title")}
            onKeyDown={(e) => {
              if (e.key === "Escape" && !e.defaultPrevented) {
                e.preventDefault();
                closeRecord();
              }
            }}
          >
            {props.renderRecord(openId, closeRecord)}
          </aside>
        )}
        {!props.renderRecord && openRow && definition && (
          <RecordPanel definition={definition} row={openRow} formatters={formatters} renderCell={props.renderCell} onClose={closeRecord} />
        )}
      </div>
      <div className="list-statusbar muted">
        <span>{t("lists.keys")}</span>
      </div>
    </section>
  );
}

function RecordPanel({
  definition,
  row,
  formatters,
  renderCell,
  onClose,
}: {
  definition: ListDefinition;
  row: Row;
  formatters: Formatters;
  renderCell?: Partial<Record<string, (row: Row) => ReactNode>>;
  onClose: () => void;
}) {
  const { t } = formatters;
  const heading = useRef<HTMLHeadingElement>(null);
  const titleColumn = definition.searchFields[0] ?? definition.columns[0]?.key;
  useEffect(() => {
    heading.current?.focus();
  }, [row.id]);
  return (
    <aside
      className="list-record"
      role="region"
      aria-label={t("lists.record.title")}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div className="list-record-head">
        <h2 ref={heading} tabIndex={-1}>
          {titleColumn ? String(row[titleColumn] ?? "") : t("lists.record.title")}
        </h2>
        <button type="button" className="button" onClick={onClose}>
          {t("lists.record.close")}
        </button>
      </div>
      <dl className="facts">
        {definition.columns.map((c) => (
          <div key={c.key} className="list-fact">
            <dt>{t(c.labelKey)}</dt>
            <dd>{renderCell?.[c.key]?.(row) ?? formatValue(c, row[c.key], formatters)}</dd>
          </div>
        ))}
      </dl>
    </aside>
  );
}

function width(column: ListColumn): string {
  switch (column.type) {
    case "boolean":
      return "minmax(5.5rem, 0.6fr)";
    case "choice":
    case "number":
    case "money":
      return "minmax(6.5rem, 0.7fr)";
    case "date":
    case "dateTime":
      return "minmax(9rem, 0.9fr)";
    default:
      return "minmax(10rem, 1.4fr)";
  }
}

/** What a view stores; the "modified" mark compares against it. */
function fingerprint(state: ListState): string {
  return JSON.stringify([state.search.trim(), sortText(state.sort), queryOf({ ...state, search: "" }).get("filter"), state.groupBy, state.columns]);
}

function viewBody(state: ListState, name: string, isDefault: boolean) {
  return {
    name,
    columns: state.columns,
    sort: sortText(state.sort),
    filter: queryOf({ ...state, search: "" }).get("filter"),
    search: state.search.trim() || null,
    groupBy: state.groupBy,
    isDefault,
  };
}
