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
  reload: () => void;
  /** Take a newer copy of the record saved by another action (a logo upload), keeping the draft. */
  adopt: (record: R) => void;
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

  useEffect(() => {
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
        setStatus("ready");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setMessage(error instanceof Error ? error.message : String(error));
        setStatus("failed");
      });
    return () => controller.abort();
  }, [key, loads]);

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
  }, []);

  const set = useCallback(<K extends keyof D>(field: K) => (value: D[K]) => {
    update((d) => ({ ...d, [field]: value }));
    setErrors((e) => (e[field as string] ? Object.fromEntries(Object.entries(e).filter(([k]) => k !== field)) : e));
  }, [update]);

  const save = useCallback(async (): Promise<R | null> => {
    const current = specRef.current;
    if (!current.canEdit || busy) return null;
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
      return null;
    } finally {
      setBusy(false);
    }
  }, [busy, draft, record]);

  const discard = useCallback(() => {
    setDraft(baseline);
    setErrors({});
    setMessage(null);
    setSaved(false);
  }, [baseline]);

  const reload = useCallback(() => setLoads((n) => n + 1), []);
  const adopt = useCallback((next: R) => setRecord(next), []);

  const bind = useCallback(
    <K extends keyof D & string>(field: K): FieldBinding<D[K]> => ({
      name: field,
      value: draft[field],
      onChange: set(field),
      errors: errors[field] ?? [],
      readOnly,
    }),
    [draft, errors, readOnly, set],
  );

  return useMemo(
    () => ({ status, record, isNew: record === null, draft, dirty, busy, saved, errors, message, conflict, readOnly, set, update, bind, save, discard, reload, adopt, leaveAsker }),
    [status, record, draft, dirty, busy, saved, errors, message, conflict, readOnly, set, update, bind, save, discard, reload, adopt],
  );
}
