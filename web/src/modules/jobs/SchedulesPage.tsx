import { useEffect, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { BooleanField, SelectField, TextAreaField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { DateTime, useLocalized } from "./JobsPage";
import { jobsChanged, type JobType, type Schedule } from "./types";

/**
 * Job schedules: the shared list of what starts jobs by itself. Opening one shows its form (name,
 * kind, timing in five cron fields read in the workspace's time zone, parameters, on or off) and
 * when it last ran and runs next; Alt+N adds one.
 */
export function SchedulesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const [types, setTypes] = useState<JobType[]>([]);
  const panel = useRecordPanel(can("jobs.schedules.create"));
  const latest = useRef({ startNew: panel.startNew });
  latest.current = { startNew: panel.startNew };
  useShortcut({
    id: "jobs.schedules.new",
    chord: "Alt+KeyN",
    labelKey: "jobs.schedule.new",
    keywordsKey: "jobs.action.newScheduleKeywords",
    groupKey: "jobs.shortcut.group",
    enabled: Boolean(panel.startNew),
    run: () => latest.current.startNew?.(),
  });
  useEffect(() => {
    const controller = new AbortController();
    api<JobType[]>("GET", "/api/jobs/types", undefined, { signal: controller.signal }).then(setTypes, () => undefined);
    return () => controller.abort();
  }, []);
  return (
    <div className="jb-list">
      <ListView
        listKey="jobs.schedules"
        titleKey="jobs.schedules.title"
        countKey="jobs.schedules.count"
        searchPlaceholderKey="jobs.schedules.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) => (
          <ScheduleForm key={panel.formKey} id={id === newRecord ? null : id} nav={nav} types={types} onSaved={panel.saved} onClose={close} />
        )}
        actions={
          panel.startNew && (
            <button type="button" className="button primary" onClick={panel.startNew} title={t("jobs.schedule.newHint")} aria-keyshortcuts="Alt+N">
              {t("jobs.schedule.new")}
            </button>
          )
        }
        renderCell={{
          jobType: (r) => <span dir="ltr">{String(r.jobType ?? "")}</span>,
          cronTiming: (r) => <span dir="ltr" className="num">{String(r.cronTiming ?? "")}</span>,
          nextRunAt: (r) => <DateTime value={r.nextRunAt as string | null} />,
          lastRunAt: (r) => <DateTime value={r.lastRunAt as string | null} />,
        }}
      />
    </div>
  );
}

type Draft = { name: string; type: string; cron: string; payload: string; isEnabled: boolean };

function ScheduleForm({ id, nav, types, onSaved, onClose }: { id: string | null; nav?: RecordNavigation; types: JobType[]; onSaved: (id: string) => void; onClose: () => void }) {
  const { t } = useI18n();
  const { can } = useSession();
  const localized = useLocalized();
  const form = useRecordForm<Schedule, Draft>(
    {
      load: id === null ? undefined : (signal) => api<Schedule>("GET", `/api/jobs/schedules/${id}`, undefined, { signal }),
      initial: (s) => ({
        name: s?.name ?? "",
        type: s?.type ?? "",
        cron: s?.cron ?? "",
        payload: s && Object.keys(s.payload).length > 0 ? JSON.stringify(s.payload) : "",
        isEnabled: s?.isEnabled ?? true,
      }),
      canEdit: id === null ? can("jobs.schedules.create") : can("jobs.schedules.update"),
      validate: (draft): Record<string, string[]> => {
        if (draft.payload.trim() === "") return {};
        try {
          JSON.parse(draft.payload);
          return {};
        } catch {
          return { payload: [t("jobs.start.payloadInvalid")] };
        }
      },
      save: (draft, schedule) => {
        const payload = draft.payload.trim() === "" ? {} : (JSON.parse(draft.payload) as Record<string, unknown>);
        return schedule === null
          ? api<Schedule>("POST", "/api/jobs/schedules", { name: draft.name.trim() || null, type: draft.type || null, cron: draft.cron.trim() || null, payload, isEnabled: draft.isEnabled })
          : api<Schedule>("PUT", `/api/jobs/schedules/${schedule.id}`, { name: draft.name.trim() || null, cron: draft.cron.trim() || null, payload, isEnabled: draft.isEnabled, version: schedule.version });
      },
      onSaved: (saved) => {
        onSaved(saved.id);
        window.dispatchEvent(new Event(jobsChanged));
      },
    },
    id,
  );
  const schedule = form.record;
  const bind = form.bind;
  const choices = types.filter((k) => k.schedulable || k.key === schedule?.type).map((k) => ({ value: k.key, label: localized(k.nameEn, k.nameAr) }));
  return (
    <RecordForm
      form={form}
      label={t("jobs.schedule.form")}
      title={schedule ? schedule.name : t("jobs.schedule.new")}
      subtitle={schedule ? <span dir="ltr">{schedule.cron}</span> : undefined}
      onClose={onClose}
      nav={nav}
      readOnlyReason={schedule && can("jobs.schedules.update") && !schedule.canUpdate ? t("jobs.schedule.needsKindPermission") : undefined}
    >
      <FormSection title={t("jobs.schedule.general")}>
        <TextField field={bind("name")} label={t("jobs.schedule.name")} maxLength={120} autoFocus={id === null} />
        <SelectField field={bind("type")} label={t("jobs.schedule.type")} options={choices} empty={t("jobs.schedule.chooseType")} disabled={id !== null} />
        <TextField field={bind("cron")} label={t("jobs.schedule.cron")} dir="ltr" maxLength={100} hint={t("jobs.schedule.cronHint", { zone: schedule?.timeZone ?? "" })} />
        <TextAreaField field={bind("payload")} label={t("jobs.start.payload")} dir="ltr" rows={3} hint={t("jobs.start.payloadHint")} />
        <BooleanField field={bind("isEnabled")} label={t("jobs.schedule.enabled")} />
      </FormSection>
      {schedule && (
        <dl className="jb-facts">
          <dt>{t("jobs.schedule.nextRunAt")}</dt>
          <dd>{schedule.nextRunAt ? <DateTime value={schedule.nextRunAt} /> : t("jobs.schedule.never")}</dd>
          <dt>{t("jobs.schedule.lastRunAt")}</dt>
          <dd>{schedule.lastRunAt ? <DateTime value={schedule.lastRunAt} /> : t("jobs.job.notYet")}</dd>
        </dl>
      )}
    </RecordForm>
  );
}
