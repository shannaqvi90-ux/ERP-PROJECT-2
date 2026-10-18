import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from "react";
import { ApiError } from "../api";
import { useI18n } from "../i18n";
import { addLeaveGuard, hookUnload } from "./leave";

/** Field errors by draft field, in the screen's language (from the server's problem response). */
export type FormErrors = Record<string, string[]>;

export type RecordFormSpec<R, D> = {
  /** Loads the record; leave it out for a new record. Called again by reload(). */
  load?: (signal: AbortSignal) => Promise<R>;
  /** The draft a record starts as (null: a new record). */
  initial: (record: R | null) => D;
  /** Sends the draft (create or update) and returns the saved record. */
  save: (draft: D, record: R | null) => Promise<R>;
  /** The user may change this record (create for a new one, update for an existing one). */
  canEdit: boolean;
  /** After a successful save; `created` is true for a new record. */
  onSaved?: (record: R, created: boolean) => void;
  /** The draft field a server field error belongs to, when the names differ (default: same name). */
  fieldOf?: (serverField: string) => string | undefined;
  /** Checks run before sending; return field errors to stop (messages in the screen's language). */
  validate?: (draft: D) => FormErrors;
};

export type FieldBinding<V> = {
  name: string;
  value: V;
  onChange: (value: V) => void;
  errors: string[];
  readOnly: boolean;
  /** The user typed the last character of a value whose length is fixed (a whole UAE phone
   * number): the form saves it when it is the record's only change (see completeEntry). */
  onComplete?: (value: V) => void;
};

export type RecordFormState<R, D> = {
  status: "loading" | "ready" | "failed";
  record: R | null;
  isNew: boolean;
  draft: D;
  /** The draft differs from what was loaded or last saved. */
  dirty: boolean;
  busy: boolean;
  /** The last save succeeded and nothing changed since. */
  saved: boolean;
  errors: FormErrors;
  /** A message for the whole form (the server's problem title, errors of fields not on screen). */
  message: string | null;
  /** The record changed elsewhere since it was read (409): reloading shows the current version. */
  conflict: boolean;
  readOnly: boolean;
  set: <K extends keyof D>(key: K) => (value: D[K]) => void;
  update: (change: (draft: D) => D) => void;
  bind: <K extends keyof D & string>(key: K) => FieldBinding<D[K]>;
  /** Validates, sends and maps the server's field errors back to the fields; null when it failed. */
  save: () => Promise<R | null>;
  /** Back to what was loaded or last saved. */
  discard: () => void;
  /**
   * A field's entry is complete (its last character typed): on a saved record whose only change
   * is that field, the form saves at once, with no key to press; the save can be undone. With
   * other changes pending, or on a new record, nothing happens: the user saves when done.
   */
  completeEntry: <K extends keyof D & string>(key: K, value: D[K]) => void;
  /** The field whose completed entry the form saved on its own (null after any other change or save). */
  autoSaved: string | null;
  /** Puts back what the record held before the form saved a completed entry on its own, and saves that. */
  undo: () => Promise<R | null>;
  reload: () => void;
  /** Take a newer copy of the record saved by another action (a logo upload), keeping the draft. */
  adopt: (record: R) => void;
  /**
   * Take a newer copy of the record that changed elsewhere in fields the form shows (the shell's
   * language switch changes the user's own Language): the form shows the new values and holds the
   * new version, so the next save is not refused; a field the user has changed keeps what they
   * typed, and stays a change.
   */
  rebase: (record: R) => void;
  /** Set by the record form showing this state: asks in its dialog before leaving unsaved changes
   * (save and leave, discard and leave, keep editing), then calls `proceed`. */
  leaveAsker: RefObject<((proceed: () => void) => void) | null>;
};

const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

/**
 * The state of one record form, the same for every module: load, a draft with dirty tracking
 * (and the unsaved-changes guard while dirty), save with the server's validation errors mapped to
 * their fields and the rest to the form's message, a conflict (409) offered as a reload, discard,
 * and read-only when the user may not change the record. Screens render it with
 * <c>RecordForm</c> and the field components of kernel/forms/fields.
 */
export function useRecordForm<R, D>(spec: RecordFormSpec<R, D>, key: unknown = null): RecordFormState<R, D> {
  const { t } = useI18n();
  const specRef = useRef(spec);
  specRef.current = spec;
  const [status, setStatus] = useState<"loading" | "ready" | "failed">(spec.load ? "loading" : "ready");
  const [record, setRecord] = useState<R | null>(null);
  const [baseline, setBaseline] = useState<D>(() => spec.initial(null));
  const [draft, setDraft] = useState<D>(baseline);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [errors, setErrors] = useState<FormErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [loads, setLoads] = useState(0);
  // What the record held before the form saved a completed entry on its own, for undo.
  const [undoable, setUndoable] = useState<{ field: string; draft: D } | null>(null);

  // A record this form has just created: the screen then points the form at the new record's id
  // (the key changes in the same render as the save). The save's answer is that record, so the
  // form keeps it instead of reading it again: a re-read would swap the whole form for "Loading"
  // and back, losing the focus and anything typed meanwhile (a new company's first branch line).
  const justCreated = useRef(false);
  const seenLoads = useRef(loads);
  useEffect(() => {
    const keyChangeOnly = seenLoads.current === loads;
    seenLoads.current = loads;
    if (justCreated.current && keyChangeOnly) {
      justCreated.current = false;
      return;
    }
    const load = specRef.current.load;
    if (!load) return;
    const controller = new AbortController();
    setStatus("loading");
    load(controller.signal)
      .then((loaded) => {
        if (controller.signal.aborted) return;
        const start = specRef.current.initial(loaded);
        setRecord(loaded);
        setBaseline(start);
        setDraft(start);
        setErrors({});
        setConflict(false);
        setMessage(null);
        setUndoable(null);
        setStatus("ready");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setMessage(error instanceof Error ? error.message : String(error));
        setStatus("failed");
      });
    return () => controller.abort();
  }, [key, loads]);
  // The created mark holds for the render that follows the save only (declared after the load
  // effect, so it runs after it in that render).
  useEffect(() => {
    justCreated.current = false;
  });

  const readOnly = !spec.canEdit;
  const leaveAsker = useRef<((proceed: () => void) => void) | null>(null);
  const dirty = !readOnly && status === "ready" && !same(draft, baseline);

  // Unsaved changes: leaving the screen, opening another record or reloading asks first.
  useEffect(() => {
    if (!dirty) return;
    hookUnload();
    return addLeaveGuard({
      message: () => t("forms.leave.confirm"),
      ask: (proceed) => {
        const asker = leaveAsker.current;
        if (asker) asker(proceed);
        else if (window.confirm(t("forms.leave.confirm"))) proceed();
      },
    });
  }, [dirty, t]);

  const update = useCallback((change: (draft: D) => D) => {
    setDraft((d) => change(d));
    setSaved(false);
    setUndoable(null);
  }, []);

  const set = useCallback(<K extends keyof D>(field: K) => (value: D[K]) => {
    update((d) => ({ ...d, [field]: value }));
    setErrors((e) => (e[field as string] ? Object.fromEntries(Object.entries(e).filter(([k]) => k !== field)) : e));
  }, [update]);

  const send = useCallback(async (draft: D, auto: { field: string; before: D } | null): Promise<R | null> => {
    const current = specRef.current;
    if (!current.canEdit || busy) return null;
    // Enter or Ctrl+S right after the form saved a completed entry on its own (the habit of
    // finishing an entry with Enter) sends nothing more: no new version, no audit entry of nothing.
    if (record !== null && undoable !== null && same(draft, baseline)) return record;
    const local = current.validate?.(draft) ?? {};
    if (Object.keys(local).length > 0) {
      setErrors(local);
      setMessage(null);
      return null;
    }
    setBusy(true);
    setMessage(null);
    try {
      const result = await current.save(draft, record);
      const next = current.initial(result);
      setRecord(result);
      setBaseline(next);
      setDraft(next);
      setErrors({});
      setConflict(false);
      setSaved(true);
      setUndoable(auto ? { field: auto.field, draft: auto.before } : null);
      justCreated.current = record === null;
      current.onSaved?.(result, record === null);
      return result;
    } catch (error) {
      const mapped: FormErrors = {};
      const elsewhere: string[] = [];
      if (error instanceof ApiError) {
        const fields = new Set(Object.keys(draft as object));
        for (const [serverField, list] of Object.entries(error.fieldErrors)) {
          const field = current.fieldOf?.(serverField) ?? serverField;
          if (fields.has(field)) mapped[field] = [...(mapped[field] ?? []), ...list.map((e) => e.message)];
          else elsewhere.push(...list.map((e) => e.message));
        }
        setConflict(error.status === 409 && error.code === "concurrency");
      }
      setErrors(mapped);
      setMessage([error instanceof Error ? error.message : String(error), ...elsewhere].join(" "));
      setUndoable(null);
      return null;
    } finally {
      setBusy(false);
    }
  }, [busy, baseline, record, undoable]);

  const save = useCallback(() => send(draft, null), [send, draft]);

  const completeEntry = useCallback(<K extends keyof D & string>(field: K, value: D[K]) => {
    if (record === null || readOnly || busy) return;
    const next = { ...draft, [field]: value } as D;
    const onlyThis = (Object.keys(next as object) as (keyof D)[]).every((k) => k === field || same(next[k], baseline[k]));
    if (!onlyThis || same(value, baseline[field])) return;
    setDraft(next);
    void send(next, { field, before: baseline });
  }, [record, readOnly, busy, draft, baseline, send]);

  const undo = useCallback(async (): Promise<R | null> => {
    if (!undoable || !same(draft, baseline)) return null;
    const previous = undoable.draft;
    setUndoable(null);
    setDraft(previous);
    return send(previous, null);
  }, [undoable, draft, baseline, send]);

  const discard = useCallback(() => {
    setDraft(baseline);
    setErrors({});
    setMessage(null);
    setSaved(false);
    setUndoable(null);
  }, [baseline]);

  const reload = useCallback(() => setLoads((n) => n + 1), []);
  const adopt = useCallback((next: R) => setRecord(next), []);
  const rebase = useCallback(
    (next: R) => {
      const fresh = specRef.current.initial(next);
      const merged = { ...(fresh as Record<string, unknown>) };
      const held = draft as Record<string, unknown>;
      const base = baseline as Record<string, unknown>;
      for (const key of Object.keys(merged)) if (!same(held[key], base[key])) merged[key] = held[key];
      setRecord(next);
      setBaseline(fresh);
      setDraft(merged as D);
      setConflict(false);
    },
    [draft, baseline],
  );

  const bind = useCallback(
    <K extends keyof D & string>(field: K): FieldBinding<D[K]> => ({
      name: field,
      value: draft[field],
      onChange: set(field),
      errors: errors[field] ?? [],
      readOnly,
      onComplete: (value: D[K]) => completeEntry(field, value),
    }),
    [draft, errors, readOnly, set, completeEntry],
  );

  return useMemo(
    () => ({ status, record, isNew: record === null, draft, dirty, busy, saved, errors, message, conflict, readOnly, set, update, bind, save, discard, completeEntry, autoSaved: undoable?.field ?? null, undo, reload, adopt, rebase, leaveAsker }),
    [status, record, draft, dirty, busy, saved, errors, message, conflict, readOnly, set, update, bind, save, discard, completeEntry, undoable, undo, reload, adopt, rebase],
  );
}
