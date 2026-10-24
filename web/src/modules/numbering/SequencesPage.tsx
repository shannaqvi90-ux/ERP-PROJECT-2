import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { BooleanField, Field, replaceOnEntry, SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm, type FieldBinding, type FormErrors } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView, type ReferenceSource } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { checkPattern, formatNumber, maxAffixLength, maxPadding, nextStart, periodOf, resets, tokens, type Reset } from "./pattern";
import { sequencesChanged, type ListPage, type Sequence, type SequenceRow } from "./types";

type Company = { id: string; code: string; legalNameEn: string; legalNameAr: string };

/**
 * Document numbering: how each company numbers each kind of document, the working company's first,
 * with the number its next document dated today would get. Opening a sequence puts the cursor in its
 * prefix (the whole prefix chosen, so typing replaces it): Tab reaches the digits, the suffix, when
 * the numbers start again, whether they may skip and whether each branch counts on its own, and
 * Enter saves. The next number is previewed as the pattern is typed, with when it next starts again,
 * and below the form the sequence's counters and the last numbers it gave.
 */
export function SequencesPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  // Every company holds a sequence for each kind of document: none is created here.
  const panel = useRecordPanel(false);
  const shownRows = useRef(new Map<string, SequenceRow>());
  const [companies, setCompanies] = useState<Company[]>([]);
  useEffect(() => {
    if (!can("tenancy.companies.read")) return;
    const controller = new AbortController();
    api<ListPage<Company>>("GET", "/api/tenancy/companies?take=200", undefined, { signal: controller.signal }).then(
      (page) => setCompanies(page.items),
      () => undefined,
    );
    return () => controller.abort();
  }, [can]);
  const references = useMemo<Record<string, ReferenceSource>>(() => {
    const byId = new Map(companies.map((c) => [c.id, `${c.code} · ${language === "ar" ? c.legalNameAr || c.legalNameEn : c.legalNameEn || c.legalNameAr}`]));
    return { companyId: { label: (id) => byId.get(id) ?? shownRows.current.get(id)?.companyCode, options: companies.map((c) => ({ value: c.id, label: byId.get(c.id)! })) } };
  }, [companies, language]);

  return (
    <div className="nb-list">
      <ListView
        listKey="numbering.sequences"
        titleKey="numbering.sequences.title"
        countKey="numbering.sequences.count"
        searchPlaceholderKey="numbering.sequences.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        references={references}
        renderRecord={(id, close, nav) => (
          <SequenceForm key={panel.formKey} id={id} row={shownRows.current.get(id)} nav={nav} onSaved={panel.saved} onClose={close} />
        )}
        renderCell={{
          nameEn: (r) => {
            shownRows.current.set(String(r.id), r as unknown as SequenceRow);
            return language === "ar" ? <span dir="rtl">{String(r.nameAr || r.nameEn || "")}</span> : <span>{String(r.nameEn ?? "")}</span>;
          },
          nameAr: (r) => <span dir="rtl">{String(r.nameAr ?? "")}</span>,
          companyId: (r) => <span dir="ltr">{String(r.companyCode ?? "")}</span>,
          nextNumber: (r) => (
            <span dir="ltr" className="nb-number">
              {String(r.nextNumber ?? "")}
            </span>
          ),
          prefix: (r) => <code dir="ltr">{String(r.prefix ?? "")}</code>,
          suffix: (r) => <code dir="ltr">{String(r.suffix ?? "")}</code>,
          reset: (r) => t(`numbering.reset.${String(r.reset)}`),
          gapless: (r) => (r.gapless ? t("numbering.gapless.true") : <span className="muted">{t("numbering.gapless.false")}</span>),
          perBranch: (r) => (r.perBranch ? t("numbering.perBranch.true") : <span className="muted">{t("numbering.perBranch.false")}</span>),
        }}
      />
    </div>
  );
}

type Draft = { prefix: string; suffix: string; padding: string; reset: Reset; gapless: boolean; perBranch: boolean; nextValue: string };

const draftOf = (s: Pick<Sequence, "prefix" | "suffix" | "padding" | "reset" | "gapless" | "perBranch"> | null): Draft => ({
  prefix: s?.prefix ?? "",
  suffix: s?.suffix ?? "",
  padding: String(s?.padding ?? 5),
  reset: s?.reset ?? "yearly",
  gapless: s?.gapless ?? false,
  perBranch: s?.perBranch ?? false,
  nextValue: "",
});

const wholeNumber = (text: string): number | null => (/^\d{1,18}$/.test(text.trim()) ? Number.parseInt(text.trim(), 10) : null);

/** What a row of the list already says, as a form record until the record itself arrives. */
function fromRow(row: SequenceRow): Sequence {
  return { ...row, companyNameEn: "", companyNameAr: "", previewBranchId: null, today: "", branches: [], counters: [], recent: [], createdAt: "" };
}

function SequenceForm({ id, row, nav, onSaved, onClose }: { id: string; row?: SequenceRow; nav?: RecordNavigation; onSaved: (id: string) => void; onClose: () => void }) {
  const { t, language, format } = useI18n();
  const { can } = useSession();
  const [canUpdate, setCanUpdate] = useState(row?.canUpdate ?? true);
  const read = (signal?: AbortSignal) =>
    api<Sequence>("GET", `/api/numbering/sequences/${id}`, undefined, { signal }).then((s) => {
      setCanUpdate(s.canUpdate);
      return s;
    });
  const message = (code: string, args?: (string | number)[]) =>
    t(`numbering.validation.${code}`, Object.fromEntries((args ?? []).map((a, i) => [String(i), typeof a === "number" ? format.number(a) : a])));
  const form = useRecordForm<Sequence, Draft>(
    {
      // Opened from its row: ready at once with the row's pattern; the record (its counters, the last
      // numbers, the branches) replaces it as soon as it arrives.
      load: (signal) => (row && row.id === id ? Promise.resolve(fromRow(row)) : read(signal)),
      initial: draftOf,
      canEdit: can("numbering.sequences.update") && canUpdate,
      validate: (draft) => {
        const errors: FormErrors = {};
        const add = (field: string, text: string) => (errors[field] = [...(errors[field] ?? []), text]);
        for (const p of checkPattern(draft.prefix.trim(), draft.suffix.trim(), wholeNumber(draft.padding), draft.reset, draft.perBranch)) add(p.field, message(p.code, p.args));
        if (draft.nextValue.trim() !== "" && (wholeNumber(draft.nextValue) ?? 0) < 1) add("nextValue", message("numberingNextNumberRange", ["999,999,999,999,999,999"]));
        return errors;
      },
      save: (draft, sequence) =>
        api<Sequence>("PUT", `/api/numbering/sequences/${sequence!.id}`, {
          prefix: draft.prefix.trim(),
          suffix: draft.suffix.trim(),
          padding: wholeNumber(draft.padding),
          reset: draft.reset,
          gapless: draft.gapless,
          perBranch: draft.perBranch,
          nextValue: draft.nextValue.trim() === "" ? null : wholeNumber(draft.nextValue),
          version: sequence!.version,
        }),
      onSaved: (saved) => {
        setCanUpdate(saved.canUpdate);
        onSaved(saved.id);
        window.dispatchEvent(new Event(sequencesChanged));
      },
    },
    id,
  );
  const adopt = useRef(form.adopt);
  adopt.current = form.adopt;
  useEffect(() => {
    if (!row || row.id !== id) return;
    const controller = new AbortController();
    read(controller.signal).then((s) => adopt.current(s), () => undefined);
    return () => controller.abort();
    // Once per record opened from its row.
  }, [id]);
  const sequence = form.record;
  const bind = form.bind;
  const name = sequence ? (language === "ar" ? sequence.nameAr || sequence.nameEn : sequence.nameEn || sequence.nameAr) : "";
  const company = sequence ? (language === "ar" ? sequence.companyNameAr || sequence.companyNameEn : sequence.companyNameEn) : "";
  return (
    <RecordForm
      form={form}
      label={t("numbering.sequence.form")}
      title={name}
      subtitle={sequence ? [sequence.companyCode, company].filter(Boolean).join(" · ") : undefined}
      onClose={onClose}
      nav={nav}
      readOnlyReason={sequence && can("numbering.sequences.update") && !canUpdate ? t("numbering.sequence.needsEveryBranch") : undefined}
      after={sequence && sequence.today !== "" && <SequenceHistory sequence={sequence} />}
    >
      <FormSection title={t("numbering.sequence.pattern")}>
        <PrefixField field={bind("prefix")} label={t("numbering.sequence.prefix")} focus={!form.readOnly} />
        <TextField field={bind("padding")} label={t("numbering.sequence.padding")} dir="ltr" inputMode="numeric" maxLength={2}
          hint={t("numbering.sequence.paddingHint", { max: maxPadding })} />
        <TextField field={bind("suffix")} label={t("numbering.sequence.suffix")} dir="ltr" maxLength={maxAffixLength} />
        <TokenHint />
      </FormSection>
      <FormSection title={t("numbering.sequence.counting")}>
        <SelectField field={bind("reset") as FieldBinding<Reset | "">} label={t("numbering.sequence.reset")}
          options={resets().map((r) => ({ value: r, label: t(`numbering.reset.${r}`) }))} />
        <BooleanField field={bind("gapless")} label={t("numbering.sequence.gaplessLabel")} />
        <BooleanField field={bind("perBranch")} label={t("numbering.sequence.perBranchLabel")} />
        <TextField field={bind("nextValue")} label={t("numbering.sequence.setNextNumber")} dir="ltr" inputMode="numeric" maxLength={18}
          hint={form.draft.perBranch ? t("numbering.sequence.nextNumberPerBranch") : t("numbering.sequence.nextNumberHint")}
          disabled={form.draft.perBranch} />
      </FormSection>
      {sequence && <Preview sequence={sequence} draft={form.draft} />}
    </RecordForm>
  );
}

/**
 * The prefix: as the panel opens the cursor is here with the whole prefix chosen, so typing replaces
 * it (the comparison's shortest path: open, type the prefix, Tab, the digits, Enter).
 */
function PrefixField({ field, label, focus }: { field: FieldBinding<string>; label: string; focus: boolean }) {
  const input = useRef<HTMLInputElement>(null);
  const focused = useRef(false);
  useEffect(() => {
    if (!focus || focused.current || !input.current) return;
    focused.current = true;
    input.current.focus();
    input.current.select();
  }, [focus]);
  return (
    <Field name={field.name} label={label} errors={field.errors}>
      {(a11y) => (
        <input
          {...a11y}
          ref={input}
          name={field.name}
          type="text"
          value={field.value ?? ""}
          dir="ltr"
          maxLength={maxAffixLength}
          disabled={field.readOnly}
          autoComplete="off"
          spellCheck={false}
          {...replaceOnEntry}
          onChange={(e) => field.onChange(e.target.value)}
        />
      )}
    </Field>
  );
}

/** Which tokens a prefix or suffix may hold, and what each prints. */
function TokenHint() {
  const { t } = useI18n();
  return (
    <p className="field wide muted nb-tokens">
      {tokens().map((token) => (
        <span key={token} className="nb-token">
          <code dir="ltr">{token}</code> {t(`numbering.token.${token.slice(1, -1).toLowerCase()}`)}
        </span>
      ))}
    </p>
  );
}

/** The next numbers as the pattern now reads, in today's period, and when it next starts again. */
function Preview({ sequence, draft }: { sequence: Sequence; draft: Draft }) {
  const { t, format } = useI18n();
  const [branchId, setBranchId] = useState<string | null>(sequence.previewBranchId);
  useEffect(() => setBranchId(sequence.previewBranchId ?? sequence.branches[0]?.id ?? null), [sequence.previewBranchId, sequence.branches]);
  const today = sequence.today;
  if (today === "") return null;
  const padding = wholeNumber(draft.padding) ?? sequence.padding;
  const problems = checkPattern(draft.prefix.trim(), draft.suffix.trim(), wholeNumber(draft.padding), draft.reset, draft.perBranch);
  const branch = draft.perBranch ? (sequence.branches.find((b) => b.id === branchId) ?? null) : null;
  const counterOf = (reset: string) => sequence.counters.find((c) => c.period === periodOf(reset, today) && c.branchId === (draft.perBranch ? branch?.id ?? null : null));
  // As the server does: a sequence that starts again at another time goes on from today's count.
  const counter = counterOf(draft.reset) ?? (draft.reset !== sequence.reset && !draft.perBranch && !sequence.perBranch ? counterOf(sequence.reset) : undefined);
  const typed = draft.perBranch ? null : wholeNumber(draft.nextValue);
  const value = typed ?? counter?.nextValue ?? 1;
  const number = (n: number, date = today) => formatNumber(draft.prefix.trim(), draft.suffix.trim(), padding, date, branch?.code ?? null, n);
  const start = nextStart(draft.reset, today);
  return (
    <section className="nb-preview" aria-label={t("numbering.preview.title")} aria-live="polite">
      <h3>{t("numbering.preview.title")}</h3>
      {problems.length > 0 ? (
        <p className="muted">{t("numbering.preview.invalid")}</p>
      ) : (
        <>
          <p className="nb-next">
            <span className="nb-number nb-big" dir="ltr" data-testid="next-number">
              {number(value)}
            </span>
            <span className="muted">
              {t("numbering.preview.then", { second: number(value + 1), third: number(value + 2) })}
            </span>
          </p>
          <p className="muted">
            {start
              ? t("numbering.preview.startsAgain", { date: format.date(start), first: number(1, start) })
              : t("numbering.preview.neverStartsAgain")}
            {" "}
            {draft.gapless ? t("numbering.preview.gapless") : t("numbering.preview.mayskip")}
          </p>
        </>
      )}
      {draft.perBranch && sequence.branches.length > 0 && (
        <label className="nb-branch">
          <span>{t("numbering.preview.branch")}</span>
          <select value={branchId ?? ""} onChange={(e) => setBranchId(e.target.value || null)}>
            {sequence.branches.map((b) => (
              <option key={b.id} value={b.id}>
                {b.code}
              </option>
            ))}
          </select>
        </label>
      )}
    </section>
  );
}

/** The sequence's counters by period and branch, and the last numbers it gave. */
function SequenceHistory({ sequence }: { sequence: Sequence }) {
  const { t, format, formatDateTime } = useI18n();
  const period = (p: string) => (p === "" ? t("numbering.counter.allTime") : p);
  return (
    <div className="nb-history">
      <section aria-label={t("numbering.counter.title")}>
        <h3>{t("numbering.counter.title")}</h3>
        {sequence.counters.length === 0 ? (
          <p className="muted">{t("numbering.counter.none")}</p>
        ) : (
          <table className="nb-table">
            <thead>
              <tr>
                <th scope="col">{t("numbering.counter.period")}</th>
                <th scope="col">{t("numbering.counter.branch")}</th>
                <th scope="col">{t("numbering.counter.next")}</th>
                <th scope="col">{t("numbering.counter.lastIssued")}</th>
                <th scope="col">{t("numbering.counter.lastIssuedAt")}</th>
              </tr>
            </thead>
            <tbody>
              {sequence.counters.map((c) => (
                <tr key={`${c.period}:${c.branchId ?? ""}`}>
                  <td dir="ltr">{period(c.period)}</td>
                  <td dir="ltr">{c.branchCode ?? t("numbering.counter.wholeCompany")}</td>
                  <td className="num">{format.number(c.nextValue)}</td>
                  <td className="num">{c.lastIssued === null ? "" : format.number(c.lastIssued)}</td>
                  <td>{c.lastIssuedAt ? formatDateTime(c.lastIssuedAt) : ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <section aria-label={t("numbering.recent.title")}>
        <h3>{t("numbering.recent.title")}</h3>
        {sequence.recent.length === 0 ? (
          <p className="muted">{t("numbering.recent.none")}</p>
        ) : (
          <table className="nb-table">
            <thead>
              <tr>
                <th scope="col">{t("numbering.recent.number")}</th>
                <th scope="col">{t("numbering.recent.documentDate")}</th>
                <th scope="col">{t("numbering.recent.issuedAt")}</th>
              </tr>
            </thead>
            <tbody>
              {sequence.recent.map((i) => (
                <tr key={i.number}>
                  <td dir="ltr" className="nb-number">{i.number}</td>
                  <td>{format.date(i.documentDate)}</td>
                  <td>{formatDateTime(i.issuedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
