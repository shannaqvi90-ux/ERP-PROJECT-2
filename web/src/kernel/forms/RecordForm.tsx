import { createContext, useContext, useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from "react";
import { Dialog } from "../dialog";
import { useI18n } from "../i18n";
import { navigate } from "../router";
import { chordForAria, chordKeys, useShortcut } from "../shortcuts";
import type { RecordFormState } from "./useRecordForm";
import "./forms.css";

/** Moving through the records of the list the form was opened from. */
export type RecordNavigation = {
  previous?: () => void;
  next?: () => void;
  /** 1-based position of the record in the list, and the list's length, when known. */
  position?: number;
  total?: number;
};

/** A record printed as a document by a registered report (its id goes in the report's parameter). */
export type RecordDocument = { report: string; parameter: string; id: string };

/** Whether the open record form is read-only: its sections disable their fields (tabs and
 * actions outside the sections stay usable). */
const FormContext = createContext<{ readOnly: boolean }>({ readOnly: false });

export const useFormReadOnly = () => useContext(FormContext).readOnly;

/** The keys every record form answers to (listed in the shortcut sheet while a form is open). */
export const formChords = {
  save: "Mod+KeyS",
  saveEnter: "Mod+Enter",
  discard: "Alt+KeyZ",
  next: "Alt+PageDown",
  previous: "Alt+PageUp",
  print: "Alt+KeyR",
} as const;

const keyHint = (chord: string) => chordKeys(chord).join("+");

/** One-line inputs in which Enter saves the record (as Ctrl+S does). Not a list of choices (a
 * lookup chooses with Enter), a multi-line text (Enter starts a new line), a checkbox, a button or
 * a select. */
export function savesOnEnter(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLInputElement &&
    ["text", "email", "tel", "url", "number", "search", "password", "date", "datetime-local", "time", "month", "week"].includes(target.type) &&
    !target.readOnly &&
    !target.disabled &&
    target.getAttribute("role") !== "combobox"
  );
}

/**
 * The record form every module uses: a heading with the record's name, the toolbar (previous and
 * next record, print in English or Arabic, discard, save, close), the form's message and the
 * saved notice, then the module's sections and tabs. The same keys everywhere: Enter in a one-line
 * field, Ctrl+S or Ctrl+Enter saves, Alt+Z discards the changes, Alt+PageDown and Alt+PageUp move to the next and
 * previous record, Alt+R prints, Escape closes (asking first when there are unsaved changes).
 * A user who may not change the record sees it read-only, with the reason. After a failed save the
 * cursor goes to the first field the server refused.
 */
export function RecordForm<R, D>({
  form,
  title,
  subtitle,
  label,
  onClose,
  nav,
  document,
  readOnlyReason,
  actions,
  saveLabel,
  narrow,
  children,
  after,
}: {
  form: RecordFormState<R, D>;
  title: string;
  subtitle?: ReactNode;
  /** The form's accessible name (what kind of record), when the title alone does not say. */
  label?: string;
  onClose?: () => void;
  nav?: RecordNavigation;
  document?: RecordDocument;
  readOnlyReason?: string;
  actions?: ReactNode;
  saveLabel?: string;
  narrow?: boolean;
  children: ReactNode;
  /** Content below the form (related records that save on their own). */
  after?: ReactNode;
}) {
  const { t } = useI18n();
  const formRef = useRef<HTMLFormElement>(null);
  const headingId = useId();
  const [asking, setAsking] = useState(false);
  const [printing, setPrinting] = useState(false);
  const editable = !form.readOnly;

  const save = async (event?: FormEvent) => {
    event?.preventDefault();
    await form.save();
  };

  // After a refused save the cursor goes to the first field the server named.
  useEffect(() => {
    const first = Object.keys(form.errors)[0];
    if (!first) return;
    formRef.current
      ?.querySelector<HTMLElement>(`[data-field="${first}"] input, [data-field="${first}"] select, [data-field="${first}"] textarea`)
      ?.focus();
  }, [form.errors]);

  // What to do once the changes are saved or given up: close the form (Escape, the close button),
  // or whatever way out asked (another screen, another record of the list).
  const leaving = useRef<(() => void) | null>(null);
  const leave = () => {
    const proceed = leaving.current ?? onClose;
    leaving.current = null;
    proceed?.();
  };
  useEffect(() => {
    const asker = form.leaveAsker;
    asker.current = (proceed) => {
      leaving.current = proceed;
      setAsking(true);
    };
    return () => {
      asker.current = null;
    };
  }, [form.leaveAsker]);

  const close = () => {
    if (!onClose) return;
    leaving.current = null;
    if (form.dirty) setAsking(true);
    else onClose();
  };

  const group = "forms.shortcut.group";
  useShortcut({ id: "forms.save", chord: formChords.save, labelKey: "forms.shortcut.save", groupKey: group, enabled: editable, run: () => void save() });
  useShortcut({ id: "forms.saveEnter", chord: formChords.saveEnter, labelKey: "forms.shortcut.save", groupKey: group, enabled: editable, run: () => void save() });
  useShortcut({ id: "forms.discard", chord: formChords.discard, labelKey: "forms.shortcut.discard", groupKey: group, enabled: editable, run: () => form.discard() });
  useShortcut({ id: "forms.next", chord: formChords.next, labelKey: "forms.shortcut.next", groupKey: group, enabled: Boolean(nav?.next), run: () => nav?.next?.() });
  useShortcut({ id: "forms.previous", chord: formChords.previous, labelKey: "forms.shortcut.previous", groupKey: group, enabled: Boolean(nav?.previous), run: () => nav?.previous?.() });
  useShortcut({ id: "forms.print", chord: formChords.print, labelKey: "forms.shortcut.print", groupKey: group, enabled: Boolean(document) && !form.isNew, run: () => setPrinting((p) => !p) });

  const onKeyDown = (event: KeyboardEvent<HTMLFormElement>) => {
    // Enter in a one-line field saves, the way a person finishing an entry expects (a field that
    // uses Enter itself, such as a lookup's open list, has taken it already).
    if (
      event.key === "Enter" &&
      !event.defaultPrevented &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey &&
      !event.shiftKey &&
      !event.nativeEvent.isComposing &&
      savesOnEnter(event.target)
    ) {
      event.preventDefault();
      if (editable && !form.busy) void save();
      return;
    }
    if (event.key === "Escape" && !event.defaultPrevented && onClose) {
      event.preventDefault();
      if (printing) setPrinting(false);
      else close();
    }
  };

  if (form.status === "loading") {
    return (
      <div className="record-form" aria-busy="true">
        <p className="muted">{t("forms.loading")}</p>
      </div>
    );
  }
  if (form.status === "failed") {
    return (
      <div className="record-form">
        <div className="alert" role="alert">
          {form.message}
        </div>
        <div className="record-actions">
          <button type="button" className="button" onClick={form.reload}>
            {t("forms.reload")}
          </button>
          {onClose && (
            <button type="button" className="button" onClick={onClose}>
              {t("forms.close")}
            </button>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="record">
      <form
        ref={formRef}
        className={narrow ? "record-form narrow" : "record-form"}
        onSubmit={(e) => void save(e)}
        onKeyDown={onKeyDown}
        noValidate
        aria-labelledby={label ? undefined : headingId}
        aria-label={label}
        data-dirty={form.dirty || undefined}
      >
        <div className="record-header">
          <div className="record-title">
            <h2 id={headingId} tabIndex={-1}>
              {title}
              {form.dirty && (
                <span className="record-dirty" title={t("forms.unsaved")}>
                  {" "}
                  ●<span className="visually-hidden">{t("forms.unsaved")}</span>
                </span>
              )}
            </h2>
            {subtitle && <div className="record-subtitle muted">{subtitle}</div>}
          </div>
          <div className="record-actions" role="toolbar" aria-label={t("forms.toolbar")}>
            {nav && (nav.previous || nav.next) && (
              <span className="record-nav">
                <button
                  type="button"
                  className="button icon"
                  disabled={!nav.previous}
                  onClick={() => nav.previous?.()}
                  title={`${t("forms.previous")} (${keyHint(formChords.previous)})`}
                  aria-label={t("forms.previous")}
                  aria-keyshortcuts={chordForAria(formChords.previous)}
                >
                  <span aria-hidden="true">‹</span>
                </button>
                {nav.position !== undefined && nav.total !== undefined && (
                  <span className="muted record-position">{t("forms.position", { position: nav.position, total: nav.total })}</span>
                )}
                <button
                  type="button"
                  className="button icon"
                  disabled={!nav.next}
                  onClick={() => nav.next?.()}
                  title={`${t("forms.next")} (${keyHint(formChords.next)})`}
                  aria-label={t("forms.next")}
                  aria-keyshortcuts={chordForAria(formChords.next)}
                >
                  <span aria-hidden="true">›</span>
                </button>
              </span>
            )}
            {document && !form.isNew && <PrintMenu document={document} open={printing} setOpen={setPrinting} />}
            {editable && form.dirty && (
              <button type="button" className="button" onClick={form.discard} title={keyHint(formChords.discard)} aria-keyshortcuts={chordForAria(formChords.discard)}>
                {t("forms.discard")}
              </button>
            )}
            {actions}
            {editable && (
              <button
                type="submit"
                className="button primary"
                disabled={form.busy}
                title={`${t("forms.saveKeys.enter")} · ${keyHint(formChords.save)} · ${keyHint(formChords.saveEnter)}`}
                aria-keyshortcuts={`Enter ${chordForAria(formChords.save)} ${chordForAria(formChords.saveEnter)}`}
              >
                {form.busy ? t("forms.saving") : (saveLabel ?? t("forms.save"))}
              </button>
            )}
            {onClose && (
              <button type="button" className="button" onClick={close} title={t("forms.closeHint")} aria-keyshortcuts="Escape">
                {t("forms.close")}
              </button>
            )}
          </div>
        </div>
        {form.readOnly && (
          <div className="record-readonly" data-testid="record-read-only">
            <span className="badge">{t("forms.readOnly")}</span> {readOnlyReason ?? t("forms.readOnlyHint")}
          </div>
        )}
        {form.message && (
          <div className="alert" role="alert">
            {form.message}
            {form.conflict && (
              <button type="button" className="button link" onClick={form.reload}>
                {t("forms.reloadLatest")}
              </button>
            )}
          </div>
        )}
        {form.saved && !form.dirty && (
          <div className="notice" role="status">
            {t("forms.saved")}
          </div>
        )}
        <FormContext.Provider value={{ readOnly: form.readOnly }}>
          <div className="record-body">{children}</div>
        </FormContext.Provider>
      </form>
      {after}
      {asking && (
        <Dialog
          title={t("forms.leave.title")}
          onClose={() => {
            leaving.current = null;
            setAsking(false);
          }}
          className="confirm-dialog"
        >
          <p>{t("forms.leave.question")}</p>
          <div className="dialog-actions">
            <button
              type="button"
              className="button primary"
              onClick={async () => {
                setAsking(false);
                if (await form.save()) leave();
                else leaving.current = null;
              }}
            >
              {t("forms.leave.save")}
            </button>
            <button
              type="button"
              className="button"
              onClick={() => {
                setAsking(false);
                form.discard();
                leave();
              }}
            >
              {t("forms.leave.discard")}
            </button>
            <button
              type="button"
              className="button"
              onClick={() => {
                leaving.current = null;
                setAsking(false);
              }}
            >
              {t("forms.leave.keep")}
            </button>
          </div>
        </Dialog>
      )}
    </div>
  );
}

/** A group of fields under a heading; disabled while the form is read-only. */
export function FormSection({ title, children, columns = true }: { title?: string; children: ReactNode; columns?: boolean }) {
  const readOnly = useFormReadOnly();
  return (
    <fieldset className="form-section" disabled={readOnly}>
      {title && <legend>{title}</legend>}
      {columns ? <div className="form-grid">{children}</div> : children}
    </fieldset>
  );
}

/**
 * Tabs inside a form: arrow keys move between them (in the reading direction), Home and End go to
 * the first and last. Only the open tab is drawn (a tab that reads more, such as a history, reads it
 * when opened); the draft keeps every field, so saving sends them all.
 */
export function FormTabs({ tabs, label }: { tabs: { key: string; label: string; content: ReactNode; hidden?: boolean }[]; label: string }) {
  const { dir } = useI18n();
  const shown = tabs.filter((tab) => !tab.hidden);
  const [current, setCurrent] = useState(shown[0]?.key ?? "");
  const id = useId();
  const listRef = useRef<HTMLDivElement>(null);
  const select = (index: number) => {
    const tab = shown[(index + shown.length) % shown.length];
    if (!tab) return;
    setCurrent(tab.key);
    listRef.current?.querySelector<HTMLButtonElement>(`[data-tab="${tab.key}"]`)?.focus();
  };
  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = shown.findIndex((tab) => tab.key === current);
    const forward = dir === "rtl" ? "ArrowLeft" : "ArrowRight";
    const back = dir === "rtl" ? "ArrowRight" : "ArrowLeft";
    if (event.key === forward) select(index + 1);
    else if (event.key === back) select(index - 1);
    else if (event.key === "Home") select(0);
    else if (event.key === "End") select(shown.length - 1);
    else return;
    event.preventDefault();
  };
  return (
    <div className="form-tabs">
      <div role="tablist" aria-label={label} ref={listRef} onKeyDown={onKey}>
        {shown.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            id={`${id}-${tab.key}`}
            data-tab={tab.key}
            aria-selected={tab.key === current}
            aria-controls={`${id}-${tab.key}-panel`}
            tabIndex={tab.key === current ? 0 : -1}
            className={tab.key === current ? "form-tab active" : "form-tab"}
            onClick={() => setCurrent(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </div>
      {shown
        .filter((tab) => tab.key === current)
        .map((tab) => (
          <div key={tab.key} role="tabpanel" id={`${id}-${tab.key}-panel`} aria-labelledby={`${id}-${tab.key}`}>
            {tab.content}
          </div>
        ))}
    </div>
  );
}

/** The address of a record document in the given language and format. */
export function documentUrl(document: RecordDocument, language: string, numerals: string, format: "pdf" | "json", disposition: "attachment" | "inline" = "attachment"): string {
  const query = new URLSearchParams({ [document.parameter]: document.id, format, language, numerals });
  if (format === "pdf") query.set("disposition", disposition);
  return `/api/reports/run/${document.report}?${query}`;
}

/** Print the record as a document: a PDF in English or in Arabic (laid out right to left), or the
 * document on screen. Alt+R opens it; arrow keys and Enter choose. */
function PrintMenu({ document, open, setOpen }: { document: RecordDocument; open: boolean; setOpen: (open: boolean) => void }) {
  const { t, numerals } = useI18n();
  const menuRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (open) menuRef.current?.querySelector<HTMLElement>("[role=menuitem]")?.focus();
  }, [open]);
  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = [...(menuRef.current?.querySelectorAll<HTMLElement>("[role=menuitem]") ?? [])];
    const index = items.indexOf(window.document.activeElement as HTMLElement);
    if (event.key === "ArrowDown") items[(index + 1) % items.length]?.focus();
    else if (event.key === "ArrowUp") items[(index - 1 + items.length) % items.length]?.focus();
    else if (event.key === "Escape") setOpen(false);
    else return;
    event.preventDefault();
    event.stopPropagation();
  };
  const showOnScreen = () => {
    setOpen(false);
    navigate(`/reports/catalog?${new URLSearchParams({ report: document.report, [document.parameter]: document.id, run: "1" })}`);
  };
  return (
    <span className="menu-anchor">
      <button
        type="button"
        className="button"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        title={keyHint(formChords.print)}
        aria-keyshortcuts={chordForAria(formChords.print)}
      >
        {t("forms.print.open")}
      </button>
      {open && (
        <div className="menu" role="menu" ref={menuRef} onKeyDown={onKey} aria-label={t("forms.print.open")}>
          <a role="menuitem" className="menu-item" href={documentUrl(document, "en", "latn", "pdf")} download onClick={() => setOpen(false)}>
            {t("forms.print.pdfEnglish")}
          </a>
          <a role="menuitem" className="menu-item" href={documentUrl(document, "ar", numerals, "pdf")} download onClick={() => setOpen(false)}>
            {t("forms.print.pdfArabic")}
          </a>
          <button type="button" role="menuitem" className="menu-item" onClick={showOnScreen}>
            {t("forms.print.screen")}
          </button>
        </div>
      )}
    </span>
  );
}

/**
 * The save and close keys for a form that is not a record form (a small dialog-like form such as
 * copying a role or changing one's password): Ctrl+S or Ctrl+Enter saves, the S matched by key
 * position (KeyboardEvent.code) so it also saves on an Arabic keyboard layout; AltGr (Ctrl+Alt)
 * never does; Escape closes. Record forms get the same keys from the shortcut registry.
 */
export function formKeys(event: KeyboardEvent<HTMLElement> | globalThis.KeyboardEvent, save: () => void, close: () => void) {
  if ((event.ctrlKey || event.metaKey) && !event.altKey && (event.key === "Enter" || event.code === "KeyS" || event.key.toLowerCase() === "s")) {
    event.preventDefault();
    save();
  } else if (event.key === "Escape") {
    event.preventDefault();
    close();
  }
}
