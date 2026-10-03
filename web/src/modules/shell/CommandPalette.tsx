import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { Dialog } from "../../kernel/dialog";
import { allowed, extensions, type PaletteItem, type PaletteSource } from "../../kernel/extensions";
import { Icon, type IconName } from "../../kernel/icons";
import { useI18n } from "../../kernel/i18n";

type Translate = ReturnType<typeof useI18n>["t"];
import { navigate } from "../../kernel/router";
import { Keys } from "../../kernel/shortcuts";
import { useSession } from "../../kernel/session";
import { rank } from "./paletteSearch";

/** One line of the palette: a screen, an action or a record. */
export type PaletteEntry = {
  id: string;
  title: string;
  subtitle?: string;
  /** Extra texts matched against the query (the other language's title, a path, keywords). */
  keywords: string[];
  icon: IconName;
  chord?: string;
  path?: string;
  run?: () => void | Promise<void>;
  ltr?: boolean;
};

type Section = { key: string; label: string; entries: PaletteEntry[]; status?: "searching" | "failed" };

type SourceState = { status: "searching" | "done" | "failed"; items: PaletteItem[]; total?: number };

/** Records shown per source; the "show all matches" entry leads to the rest. */
const shownPerSource = 8;

const debounceMs = 120;

/**
 * Ctrl+K (⌘K): type to reach any screen, action or record. Screens and actions match at once in
 * either language; record sources contributed by modules answer as the user types. Up/Down move,
 * Enter opens, Esc closes and gives focus back. Only what the user's roles allow is offered.
 */
export function CommandPalette({
  screens,
  actions,
  recent,
  onOpenScreen,
  onClose,
}: {
  screens: PaletteEntry[];
  actions: PaletteEntry[];
  recent: string[];
  onOpenScreen: (path: string) => void;
  onClose: () => void;
}) {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [remote, setRemote] = useState<Record<string, SourceState>>({});
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const listId = useId();
  const sources = useMemo(() => allowed(extensions.palette, can), [can]);
  const trimmed = query.trim();

  // Ask the record sources, debounced; a newer query cancels the older one.
  useEffect(() => {
    const asked = sources.filter((s) => trimmed.length >= s.minLength);
    setRemote(Object.fromEntries(asked.map((s) => [s.key, { status: "searching", items: [] } as SourceState])));
    if (asked.length === 0) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      for (const source of asked) {
        source
          .search(trimmed, { language, signal: controller.signal })
          .then((answer) => {
            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;
            if (!controller.signal.aborted)
              setRemote((r) => ({ ...r, [source.key]: { status: "done", items: items.slice(0, shownPerSource), total: total ?? (items.length > shownPerSource ? items.length : undefined) } }));
          })
          .catch(() => {
            if (!controller.signal.aborted) setRemote((r) => ({ ...r, [source.key]: { status: "failed", items: [] } }));
          });
      }
    }, debounceMs);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [trimmed, sources, language]);

  const sections = useMemo<Section[]>(() => {
    const text = (e: PaletteEntry) => [e.title, ...e.keywords];
    const list: Section[] = [];
    if (!trimmed) {
      const recentEntries = recent.map((path) => screens.find((s) => s.path === path)).filter((s): s is PaletteEntry => !!s);
      if (recentEntries.length) list.push({ key: "recent", label: t("shell.palette.group.recent"), entries: recentEntries });
      list.push({ key: "screens", label: t("shell.palette.group.screens"), entries: screens.filter((s) => !recentEntries.includes(s)) });
      list.push({ key: "actions", label: t("shell.palette.group.actions"), entries: actions });
    } else {
      list.push({ key: "screens", label: t("shell.palette.group.screens"), entries: rank(trimmed, screens, text) });
      list.push({ key: "actions", label: t("shell.palette.group.actions"), entries: rank(trimmed, actions, text) });
    }
    for (const source of sources) {
      const state = remote[source.key];
      if (!state) continue;
      list.push({
        key: `source:${source.key}`,
        label: t(source.labelKey),
        status: state.status === "done" ? undefined : state.status,
        entries: [...state.items.map((item) => recordEntry(source, item)), ...showAllEntry(source, state, trimmed, t)],
      });
    }
    return list.filter((s) => s.entries.length > 0 || s.status);
  }, [trimmed, screens, actions, recent, sources, remote, t]);

  const flat = useMemo(() => sections.flatMap((s) => s.entries), [sections]);
  const searching = sections.some((s) => s.status === "searching");

  useEffect(() => setActive(0), [trimmed]);
  useEffect(() => {
    if (active >= flat.length && flat.length > 0) setActive(flat.length - 1);
  }, [active, flat.length]);
  useEffect(() => {
    listRef.current?.querySelector('[aria-selected="true"]')?.scrollIntoView?.({ block: "nearest" });
  }, [active]);

  function run(entry: PaletteEntry | undefined) {
    if (!entry) return;
    onClose();
    if (entry.path) {
      onOpenScreen(entry.path);
      navigate(entry.path);
    } else if (entry.run) {
      void entry.run();
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const last = flat.length - 1;
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setActive((a) => (a >= last ? 0 : a + 1));
        break;
      case "ArrowUp":
        event.preventDefault();
        setActive((a) => (a <= 0 ? Math.max(0, last) : a - 1));
        break;
      case "PageDown":
        event.preventDefault();
        setActive((a) => Math.min(last, a + 8));
        break;
      case "PageUp":
        event.preventDefault();
        setActive((a) => Math.max(0, a - 8));
        break;
      case "Enter":
        event.preventDefault();
        run(flat[active]);
        break;
    }
  }

  const optionId = (index: number) => `${listId}-o${index}`;
  let index = -1;

  return (
    <Dialog title={t("shell.palette.title")} onClose={onClose} className="palette" initialFocus={inputRef} hideTitle>
      <div className="palette-input">
        <Icon name="search" />
        <input
          ref={inputRef}
          type="text"
          role="combobox"
          aria-expanded="true"
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={flat.length ? optionId(active) : undefined}
          aria-label={t("shell.palette.title")}
          placeholder={t("shell.palette.placeholder")}
          autoComplete="off"
          spellCheck={false}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={onKeyDown}
        />
      </div>
      <div ref={listRef} id={listId} role="listbox" aria-label={t("shell.palette.results")} className="palette-list">
        {sections.map((section) => (
          <div key={section.key} role="group" aria-label={section.label} className="palette-section">
            <div className="palette-section-label" aria-hidden="true">
              {section.label}
              {section.status === "searching" && <span className="muted"> · {t("shell.palette.searching")}</span>}
            </div>
            {section.status === "failed" && (
              <div className="palette-note" role="presentation">
                {t("shell.palette.sourceFailed", { source: section.label })}
              </div>
            )}
            {section.entries.map((entry) => {
              index += 1;
              const i = index;
              return (
                <div
                  key={entry.id}
                  id={optionId(i)}
                  role="option"
                  aria-selected={i === active}
                  className="palette-option"
                  onMouseMove={() => i !== active && setActive(i)}
                  onClick={() => run(entry)}
                >
                  <Icon name={entry.icon} />
                  <span className="palette-option-title" dir={entry.ltr ? "ltr" : undefined}>
                    {entry.title}
                  </span>
                  {entry.subtitle && (
                    <span className="palette-option-subtitle" dir={entry.ltr ? "ltr" : "auto"}>
                      {entry.subtitle}
                    </span>
                  )}
                  {entry.chord && <Keys chord={entry.chord} className="palette-option-keys" />}
                </div>
              );
            })}
          </div>
        ))}
        {flat.length === 0 && !searching && (
          <div className="palette-empty" role="presentation">
            {t("shell.palette.noResults", { query: trimmed })}
          </div>
        )}
      </div>
      <div className="palette-footer" aria-hidden="true">
        <span>
          <Keys chord="ArrowUp" /> <Keys chord="ArrowDown" /> {t("shell.palette.hint.move")}
        </span>
        <span>
          <Keys chord="Enter" /> {t("shell.palette.hint.open")}
        </span>
        <span>
          <Keys chord="Escape" /> {t("shell.palette.hint.close")}
        </span>
        <span className="palette-count">{t("shell.palette.count", { count: flat.length })}</span>
      </div>
      <div className="visually-hidden" aria-live="polite">
        {trimmed ? t("shell.palette.count", { count: flat.length }) : ""}
      </div>
    </Dialog>
  );
}

/** "Show all matches" at the end of a source's records, when it matched more than it shows (or
 * does not say how many) and names a screen that lists them all. */
function showAllEntry(source: PaletteSource, state: SourceState, query: string, t: Translate): PaletteEntry[] {
  if (!source.showAll || state.status !== "done" || state.items.length === 0) return [];
  if (state.total !== undefined && state.total <= state.items.length) return [];
  return [
    {
      id: `${source.key}:all`,
      title: state.total === undefined ? t("shell.palette.showAll", { query }) : t("shell.palette.showAllCount", { count: state.total, query }),
      keywords: [],
      icon: "search",
      path: source.showAll(query),
    },
  ];
}

function recordEntry(source: PaletteSource, item: PaletteItem): PaletteEntry {
  return {
    id: `${source.key}:${item.id}`,
    title: item.title,
    subtitle: item.subtitle,
    keywords: [],
    icon: "record",
    path: item.path,
    run: item.run,
    ltr: item.ltr,
  };
}
