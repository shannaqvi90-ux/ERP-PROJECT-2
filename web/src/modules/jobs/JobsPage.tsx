import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "../../kernel/api";
import { Dialog } from "../../kernel/dialog";
import { SelectField, TextAreaField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { isActive, jobsChanged, type Job, type JobAttempt, type JobRow, type JobType, type ListPage } from "./types";

/** How often an open job and the list look again while a job is queued or running. */
const pollMilliseconds = 1500;

/** A job's progress as a bar with its percentage, in the screen's digits. */
export function ProgressBar({ percent, label }: { percent: number; label?: string }) {
  const { format } = useI18n();
  const value = Math.max(0, Math.min(100, percent));
  return (
    <span className="jb-progress" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={value} aria-label={label}>
      <span className="jb-progress-bar" style={{ inlineSize: `${value}%` }} />
      <span className="jb-progress-text">{format.number(value)}%</span>
    </span>
  );
}

/** The status word with a colour that is never the only sign of it. */
export function StatusBadge({ status }: { status: string }) {
  const { t } = useI18n();
  return <span className={`jb-status jb-status-${status}`}>{t(`jobs.status.${status}`)}</span>;
}

/** Text in the screen's language, falling back to the other one. */
export function useLocalized() {
  const { language } = useI18n();
  return (en: string | null | undefined, ar: string | null | undefined) => (language === "ar" ? ar || en || "" : en || ar || "");
}

/**
 * Background jobs: the shared list over every job of the workspace (100,000 in the demo), newest
 * first, searchable by title and kind, filtered or grouped by state, kind or source. Opening a job
 * shows its state and progress (live while it runs), its errors, tries, parameters, result and
 * output file, and runs it again (Alt+R) or cancels it (Alt+K). Alt+N starts a job of a kind
 * people may start.
 */
export function JobsPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(false);
  const [starting, setStarting] = useState(false);
  const [types, setTypes] = useState<JobType[]>([]);
  const startable = types.filter((k) => k.startable);
  const canStart = can("jobs.jobs.run") && startable.length > 0;
  const latest = useRef({ canStart });
  latest.current = { canStart };
  useShortcut({
    id: "jobs.screen.start",
    chord: "Alt+KeyN",
    labelKey: "jobs.start.title",
    keywordsKey: "jobs.action.startKeywords",
    groupKey: "jobs.shortcut.group",
    enabled: canStart,
    run: () => latest.current.canStart && setStarting(true),
  });

  useEffect(() => {
    const controller = new AbortController();
    api<JobType[]>("GET", "/api/jobs/types", undefined, { signal: controller.signal }).then(setTypes, () => undefined);
    return () => controller.abort();
  }, []);

  // While jobs are queued or running the list reads again every few seconds, and once more when the
  // last one ends; a job started or finished elsewhere shows without a reload.
  const active = useRef(0);
  useEffect(() => {
    let stopped = false;
    const look = async () => {
      try {
        const page = await api<ListPage<JobRow>>("GET", `/api/jobs/jobs?take=1&filter=${encodeURIComponent("jobStatus eq 'queued' or jobStatus eq 'running'")}`);
        if (stopped) return;
        const before = active.current;
        active.current = page.total;
        if (page.total > 0 || before > 0) panel.refresh();
      } catch {
        // The next look tries again.
      }
    };
    const timer = window.setInterval(() => {
      if (!document.hidden) void look();
    }, pollMilliseconds * 2);
    const onChanged = () => panel.refresh();
    window.addEventListener(jobsChanged, onChanged);
    return () => {
      stopped = true;
      window.clearInterval(timer);
      window.removeEventListener(jobsChanged, onChanged);
    };
  }, [panel.refresh]);

  const localized = useLocalized();
  return (
    <div className="jb-list">
      <ListView
        listKey="jobs.jobs"
        titleKey="jobs.jobs.title"
        countKey="jobs.jobs.count"
        searchPlaceholderKey="jobs.jobs.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) => <JobPanel key={id} id={id} nav={nav} onChanged={panel.refresh} onOpen={panel.onOpenIdChange} onClose={close} />}
        actions={
          canStart && (
            <button type="button" className="button primary" onClick={() => setStarting(true)} title={t("jobs.start.hint")} aria-keyshortcuts="Alt+N">
              {t("jobs.start.open")}
            </button>
          )
        }
        renderCell={{
          titleEn: (r) => localized(String(r.titleEn ?? ""), String(r.titleAr ?? "")),
          jobStatus: (r) => <StatusBadge status={String(r.jobStatus)} />,
          progressPercent: (r) => <ProgressBar percent={Number(r.progressPercent ?? 0)} label={localized(String(r.progressEn ?? ""), String(r.progressAr ?? ""))} />,
          jobType: (r) => <span dir="ltr">{String(r.jobType ?? "")}</span>,
          startedAt: (r) => <DateTime value={r.startedAt as string | null} />,
          finishedAt: (r) => <DateTime value={r.finishedAt as string | null} />,
          runAt: (r) => <DateTime value={r.runAt as string | null} />,
          createdAt: (r) => <DateTime value={r.createdAt as string | null} />,
          startedBy: (r) => t(`jobs.source.${String(r.startedBy)}`),
        }}
      />
      {starting && <StartDialog types={startable} onClose={() => setStarting(false)} onStarted={(id) => { setStarting(false); panel.onOpenIdChange(id); panel.refresh(); }} />}
    </div>
  );
}

export function DateTime({ value }: { value: string | null | undefined }) {
  const { format } = useI18n();
  return <>{value ? format.dateTime(value) : ""}</>;
}

type Draft = Record<string, never>;

/** One job: its state, live while it is queued or running, and what can be done with it. */
/** A job's parameters or result as read-only text: a field the keyboard reaches, scrolls and selects from, like the record's other fields. */
function JsonText({ value, label }: { value: unknown; label: string }) {
  const text = JSON.stringify(value, null, 2) ?? "";
  return <textarea className="jb-json" dir="ltr" readOnly rows={Math.min(10, Math.max(2, text.split("\n").length))} value={text} aria-label={label} />;
}

function JobPanel({ id, nav, onChanged, onOpen, onClose }: { id: string; nav?: RecordNavigation; onChanged: () => void; onOpen: (id: string) => void; onClose: () => void }) {
  const { t, format } = useI18n();
  const localized = useLocalized();
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const form = useRecordForm<Job, Draft>(
    {
      load: (signal) => api<Job>("GET", `/api/jobs/jobs/${id}`, undefined, { signal }),
      initial: () => ({}),
      canEdit: false,
      save: (_draft, job) => Promise.resolve(job as Job),
    },
    id,
  );
  const job = form.record;
  const { adopt } = form;

  // Live while the job is queued or running: read it again every moment and tell the list when it ends.
  const status = job?.status;
  useEffect(() => {
    if (!status || !isActive(status)) return;
    const controller = new AbortController();
    const timer = window.setInterval(() => {
      api<Job>("GET", `/api/jobs/jobs/${id}`, undefined, { signal: controller.signal }).then((fresh) => {
        adopt(fresh);
        if (!isActive(fresh.status)) onChanged();
      }, () => undefined);
    }, pollMilliseconds);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [id, status, adopt, onChanged]);

  const act = useCallback(
    async (what: "cancel" | "rerun") => {
      if (busy) return;
      setBusy(true);
      setProblem(null);
      try {
        const next = await api<Job>("POST", `/api/jobs/jobs/${id}/${what}`);
        window.dispatchEvent(new Event(jobsChanged));
        onChanged();
        // A new run is the job to watch now; a cancelled job stays open.
        if (what === "rerun") onOpen(next.id);
        else {
          adopt(next);
          // The job ended on its own before the request reached it: say so rather than show a silent no-op.
          if (what === "cancel" && next.status !== "cancelled" && !isActive(next.status)) setProblem(t("jobs.cancel.alreadyFinished"));
        }
      } catch (e) {
        setProblem(e instanceof ApiError || e instanceof Error ? e.message : String(e));
      } finally {
        setBusy(false);
      }
    },
    [busy, id, adopt, onChanged, onOpen, t],
  );
  const latest = useRef(act);
  latest.current = act;
  useShortcut({
    id: "jobs.panel.rerun",
    chord: "Alt+KeyR",
    labelKey: "jobs.rerun.title",
    keywordsKey: "jobs.action.rerunKeywords",
    groupKey: "jobs.shortcut.group",
    enabled: Boolean(job?.canRerun),
    run: () => void latest.current("rerun"),
  });
  useShortcut({
    id: "jobs.panel.cancel",
    chord: "Alt+KeyK",
    labelKey: "jobs.cancel.title",
    keywordsKey: "jobs.action.cancelKeywords",
    groupKey: "jobs.shortcut.group",
    enabled: Boolean(job?.canCancel),
    run: () => void latest.current("cancel"),
  });

  const title = job ? localized(job.titleEn, job.titleAr) : "";
  const duration = job?.startedAt && job.finishedAt ? Math.max(0, Math.round((Date.parse(job.finishedAt) - Date.parse(job.startedAt)) / 1000)) : null;
  return (
    <RecordForm
      form={form}
      record={{ type: "jobs.jobs", id }}
      label={t("jobs.job.form")}
      title={title}
      readOnlyReason={t("jobs.job.readOnlyReason")}
      subtitle={job ? <StatusBadge status={job.status} /> : undefined}
      onClose={onClose}
      nav={nav}
      actions={
        job && (
          <>
            {job.canRerun && (
              <button type="button" className="button primary" disabled={busy} onClick={() => void act("rerun")} title={t("jobs.rerun.hint")} aria-keyshortcuts="Alt+R">
                {t("jobs.rerun.title")}
              </button>
            )}
            {job.canCancel && (
              <button type="button" className="button danger" disabled={busy || job.cancelRequested} onClick={() => void act("cancel")} title={t("jobs.cancel.hint")} aria-keyshortcuts="Alt+K">
                {job.cancelRequested ? t("jobs.cancel.requested") : t("jobs.cancel.title")}
              </button>
            )}
            {job.hasOutput && job.canDownload && (
              <a className="button" href={`/api/jobs/jobs/${job.id}/output`} download={job.outputName ?? undefined}>
                {t("jobs.output.download")}
              </a>
            )}
          </>
        )
      }
    >
      {problem && (
        <div className="alert" role="alert">
          {problem}
        </div>
      )}
      {job && (
        <>
          <FormSection title={t("jobs.job.state")}>
            <div className="jb-state">
              <ProgressBar percent={job.progress} label={t("jobs.job.progress")} />
              {(job.progressEn || job.progressAr) && <span className="muted">{localized(job.progressEn, job.progressAr)}</span>}
            </div>
            <dl className="jb-facts">
              <dt>{t("jobs.job.type")}</dt>
              <dd>
                {localized(job.typeNameEn, job.typeNameAr)} <span className="muted" dir="ltr">({job.type})</span>
              </dd>
              <dt>{t("jobs.job.source")}</dt>
              <dd>{t(`jobs.source.${job.source}`)}</dd>
              <dt>{t("jobs.job.runAt")}</dt>
              <dd><DateTime value={job.runAt} /></dd>
              <dt>{t("jobs.job.startedAt")}</dt>
              <dd>{job.startedAt ? <DateTime value={job.startedAt} /> : t("jobs.job.notYet")}</dd>
              <dt>{t("jobs.job.finishedAt")}</dt>
              <dd data-testid="job-finished">{job.finishedAt ? <DateTime value={job.finishedAt} /> : t("jobs.job.notYet")}</dd>
              {duration !== null && (
                <>
                  <dt>{t("jobs.job.duration")}</dt>
                  <dd>{t("jobs.job.seconds", { count: format.number(duration) })}</dd>
                </>
              )}
              <dt>{t("jobs.job.attempts")}</dt>
              <dd>{t("jobs.job.attemptsOf", { attempts: format.number(job.attempts), max: format.number(job.maxAttempts) })}</dd>
            </dl>
          </FormSection>
          {(job.errorCode || job.errorEn || job.errorAr) && (
            <div className="alert" role="alert" data-testid="job-error">
              <strong>{localized(job.errorEn, job.errorAr)}</strong>
              {job.errorCode && (
                <span className="muted" dir="ltr">
                  {" "}
                  ({job.errorCode})
                </span>
              )}
            </div>
          )}
          {job.attemptLog.length > 0 && (
            <FormSection title={t("jobs.job.tries")}>
              <table className="jb-tries">
                <thead>
                  <tr>
                    <th scope="col">{t("jobs.try.number")}</th>
                    <th scope="col">{t("jobs.try.startedAt")}</th>
                    <th scope="col">{t("jobs.try.outcome")}</th>
                    <th scope="col">{t("jobs.try.error")}</th>
                  </tr>
                </thead>
                <tbody>
                  {job.attemptLog.map((a) => (
                    <TryRow key={a.number} attempt={a} />
                  ))}
                </tbody>
              </table>
            </FormSection>
          )}
          <FormSection title={t("jobs.job.parameters")}>
            <JsonText value={job.payload} label={t("jobs.job.parameters")} />
          </FormSection>
          {job.result && (
            <FormSection title={t("jobs.job.result")}>
              <JsonText value={job.result} label={t("jobs.job.result")} />
            </FormSection>
          )}
          {job.hasOutput && (
            <p className="muted">
              {t("jobs.output.facts", { name: job.outputName ?? "", bytes: format.number(job.outputBytes) })}
            </p>
          )}
        </>
      )}
    </RecordForm>
  );
}

function TryRow({ attempt }: { attempt: JobAttempt }) {
  const { t, format } = useI18n();
  const localized = useLocalized();
  return (
    <tr>
      <td>{format.number(attempt.number)}</td>
      <td><DateTime value={attempt.startedAt} /></td>
      <td>{t(`jobs.outcome.${attempt.outcome}`)}</td>
      <td>{localized(attempt.errorEn, attempt.errorAr)}</td>
    </tr>
  );
}

type StartDraft = { type: string; payload: string };

/** Start a job of a kind people may start: choose it, give its parameters if it reads any, and go. */
function StartDialog({ types, onClose, onStarted }: { types: JobType[]; onClose: () => void; onStarted: (id: string) => void }) {
  const { t } = useI18n();
  const localized = useLocalized();
  const [draft, setDraft] = useState<StartDraft>({ type: types[0]?.key ?? "", payload: "" });
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const chosen = types.find((k) => k.key === draft.type);
  const field = <K extends keyof StartDraft>(name: K) => ({
    name,
    value: draft[name] as never,
    onChange: ((value: string) => setDraft((d) => ({ ...d, [name]: value }))) as never,
    errors: errors[name] ?? [],
    readOnly: false,
  });
  const run = async () => {
    if (busy) return;
    let payload: unknown = undefined;
    if (draft.payload.trim() !== "") {
      try {
        payload = JSON.parse(draft.payload);
      } catch {
        setErrors({ payload: [t("jobs.start.payloadInvalid")] });
        return;
      }
    }
    setBusy(true);
    setErrors({});
    setMessage(null);
    try {
      const started = await api<Job>("POST", "/api/jobs/jobs", { type: draft.type, payload });
      window.dispatchEvent(new Event(jobsChanged));
      onStarted(started.id);
    } catch (e) {
      if (e instanceof ApiError) {
        const byField: Record<string, string[]> = {};
        for (const [name, list] of Object.entries(e.fieldErrors)) byField[name] = list.map((f) => f.message);
        setErrors(byField);
        if (Object.keys(byField).length === 0) setMessage(e.message);
      } else {
        setMessage(e instanceof Error ? e.message : String(e));
      }
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog title={t("jobs.start.title")} onClose={onClose} className="jb-start">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void run();
        }}
        onKeyDown={(event) => {
          // Ctrl+Enter (Cmd+Enter) starts the job from anywhere in the dialog, the parameters box included (Enter there adds a line).
          if (event.key === "Enter" && (event.ctrlKey || event.metaKey) && draft.type && !busy) {
            event.preventDefault();
            void run();
          }
        }}
      >
        <SelectField
          field={field("type") as never}
          label={t("jobs.job.type")}
          options={types.map((k) => ({ value: k.key, label: localized(k.nameEn, k.nameAr) }))}
          autoFocus
        />
        {chosen && (chosen.descriptionEn || chosen.descriptionAr) && <p className="muted">{localized(chosen.descriptionEn, chosen.descriptionAr)}</p>}
        <TextAreaField field={field("payload") as never} label={t("jobs.start.payload")} dir="ltr" rows={4} hint={t("jobs.start.payloadHint")} />
        {message && (
          <div className="alert" role="alert">
            {message}
          </div>
        )}
        <div className="dialog-actions">
          <button type="submit" className="button primary" disabled={busy || !draft.type}>
            {busy ? t("jobs.start.running") : t("jobs.start.run")}
          </button>
          <button type="button" className="button" onClick={onClose}>
            {t("jobs.common.close")}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

