import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { ListView, type ReferenceSource } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { AddressFields } from "./CompanyForm";
import { listSearch, loadAll, newRecord, useRecordPanel } from "./records";
import { companiesChanged, type Branch, type CompanyRow } from "./types";
import { CheckField, problemOf, SelectField, TextField, useLocalName, useScreenKeys, type Emirate, type FieldErrors } from "./ui";

/**
 * Branches of every company the user may work in: the shared list (search, filters, sort, group
 * by company or emirate, views) with the open branch's form in its panel (?open=id, or
 * ?open=new). Keyboard as on the companies screen.
 */
export function BranchesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [companies, setCompanies] = useState<CompanyRow[]>([]);
  const panel = useRecordPanel(can("tenancy.branches.create"));
  useScreenKeys({ onNew: panel.startNew, search: listSearch });

  useEffect(() => {
    if (!can("tenancy.companies.read")) return;
    let cancelled = false;
    const load = () =>
      loadAll<CompanyRow>("/api/tenancy/companies").then(
        (rows) => !cancelled && setCompanies(rows),
        () => !cancelled && setCompanies([]),
      );
    void load();
    window.addEventListener(companiesChanged, load);
    return () => {
      cancelled = true;
      window.removeEventListener(companiesChanged, load);
    };
  }, [can]);

  // Branch rows carry their company's id: show its code and name, and offer the companies as
  // the column's filter choices.
  const references = useMemo<Record<string, ReferenceSource>>(() => {
    const byId = new Map(companies.map((c) => [c.id, `${c.code} · ${name(c.legalNameEn, c.legalNameAr)}`]));
    return {
      companyId: {
        label: (id) => byId.get(id),
        options: companies.map((c) => ({ value: c.id, label: byId.get(c.id)! })),
      },
    };
  }, [companies, name]);

  return (
    <div className="tn-list">
      <ListView
        listKey="tenancy.branches"
        titleKey="tenancy.branches.title"
        countKey="tenancy.branches.count"
        searchPlaceholderKey="tenancy.branches.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        references={references}
        renderRecord={(id, close) => (
          <BranchForm
            key={panel.formKey}
            id={id === newRecord ? null : id}
            companies={id === newRecord ? companies.filter((c) => c.isActive) : companies}
            defaultCompanyId={companies.find((c) => c.isActive)?.id ?? ""}
            onSaved={panel.saved}
            onClose={close}
          />
        )}
        actions={
          panel.startNew && (
            <button type="button" className="button primary" onClick={panel.startNew} title={t("tenancy.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("tenancy.common.new")}
            </button>
          )
        }
        renderCell={{
          code: (b) => <span dir="ltr">{String(b.code ?? "")}</span>,
          nameEn: (b) => <span dir="ltr">{String(b.nameEn ?? "")}</span>,
          nameAr: (b) => <span dir="rtl">{String(b.nameAr ?? "")}</span>,
          companyId: (b) => references.companyId!.label(String(b.companyId)) ?? <span dir="ltr">{String(b.companyCode ?? "")}</span>,
          isActive: (b) => (b.isActive ? t("tenancy.common.active") : <span className="muted">{t("tenancy.common.inactive")}</span>),
        }}
      />
    </div>
  );
}

type Draft = {
  companyId: string;
  code: string;
  nameEn: string;
  nameAr: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  emirate: Emirate | "";
  poBox: string;
  country: string;
  addressAr: string;
  phone: string;
  email: string;
  isActive: boolean;
  version: number | null;
};

const optional = (value: string) => (value.trim() === "" ? null : value.trim());

function BranchForm({
  id,
  companies,
  defaultCompanyId,
  onSaved,
  onClose,
}: {
  id: string | null;
  companies: CompanyRow[];
  defaultCompanyId: string;
  onSaved: (id: string) => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [branch, setBranch] = useState<Branch | null>(null);
  const [draft, setDraft] = useState<Draft>({
    companyId: defaultCompanyId,
    code: "",
    nameEn: "",
    nameAr: "",
    addressLine1: "",
    addressLine2: "",
    city: "",
    emirate: "",
    poBox: "",
    country: "AE",
    addressAr: "",
    phone: "",
    email: "",
    isActive: true,
    version: null,
  });
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const editable = id === null ? can("tenancy.branches.create") : can("tenancy.branches.update");

  useEffect(() => {
    if (id === null) return;
    api<Branch>("GET", `/api/tenancy/branches/${id}`)
      .then((b) => {
        setBranch(b);
        setDraft({
          companyId: b.companyId,
          code: b.code,
          nameEn: b.nameEn,
          nameAr: b.nameAr,
          addressLine1: b.addressLine1 ?? "",
          addressLine2: b.addressLine2 ?? "",
          city: b.city ?? "",
          emirate: b.emirate ?? "",
          poBox: b.poBox ?? "",
          country: b.country,
          addressAr: b.addressAr ?? "",
          phone: b.phone ?? "",
          email: b.email ?? "",
          isActive: b.isActive,
          version: b.version,
        });
      })
      .catch((e) => setMessage(problemOf(e).message));
  }, [id]);

  const set = <K extends keyof Draft>(key: K) => (value: Draft[K]) => {
    setDraft((d) => ({ ...d, [key]: value }));
    setSaved(false);
  };

  const save = async (event?: FormEvent) => {
    event?.preventDefault();
    if (!editable || busy) return;
    setBusy(true);
    setMessage(null);
    const body = {
      companyId: draft.companyId || null,
      code: draft.code.trim(),
      nameEn: draft.nameEn,
      nameAr: draft.nameAr,
      addressLine1: optional(draft.addressLine1),
      addressLine2: optional(draft.addressLine2),
      city: optional(draft.city),
      emirate: draft.emirate === "" ? null : draft.emirate,
      poBox: optional(draft.poBox),
      country: draft.country.trim().toUpperCase(),
      addressAr: optional(draft.addressAr),
      phone: optional(draft.phone),
      email: optional(draft.email),
      isActive: draft.isActive,
      version: draft.version,
    };
    try {
      const result = id === null ? await api<Branch>("POST", "/api/tenancy/branches", body) : await api<Branch>("PUT", `/api/tenancy/branches/${id}`, body);
      setBranch(result);
      setDraft((d) => ({ ...d, version: result.version }));
      setErrors({});
      setSaved(true);
      onSaved(result.id);
      window.dispatchEvent(new Event(companiesChanged));
    } catch (error) {
      const problem = problemOf(error);
      setErrors(problem.fields);
      setMessage(problem.message);
    } finally {
      setBusy(false);
    }
  };

  useScreenKeys({ onSave: () => void save(), onClose });
  const common = { errors };
  return (
    <form className="record-form" onSubmit={save} noValidate aria-label={t("tenancy.branch.form")}>
      <div className="record-header">
        <h2>{id === null ? t("tenancy.branch.new") : `${branch?.companyCode ?? ""} · ${branch?.code ?? ""}`}</h2>
        <div className="record-actions">
          {editable && (
            <button type="submit" className="button primary" disabled={busy} title={t("tenancy.common.saveHint")} aria-keyshortcuts="Control+S">
              {busy ? t("tenancy.common.saving") : t("tenancy.common.save")}
            </button>
          )}
          <button type="button" className="button" onClick={onClose} title={t("tenancy.common.closeHint")} aria-keyshortcuts="Escape">
            {t("tenancy.common.close")}
          </button>
        </div>
      </div>
      {message && (
        <div className="alert" role="alert">
          {message}
        </div>
      )}
      {saved && (
        <div className="notice" role="status">
          {t("tenancy.common.saved")}
        </div>
      )}
      <fieldset disabled={!editable}>
        <legend>{t("tenancy.company.general")}</legend>
        <div className="form-grid">
          <SelectField
            name="companyId"
            label={t("tenancy.branch.company")}
            value={draft.companyId}
            options={companies.map((c) => ({ value: c.id, label: `${c.code} · ${name(c.legalNameEn, c.legalNameAr)}` }))}
            empty={companies.length === 0 ? (branch?.companyCode ?? "") : undefined}
            onChange={(v) => set("companyId")(v)}
            disabled={id !== null}
            errors={errors}
          />
          <TextField name="nameEn" label={t("tenancy.branch.nameEn")} value={draft.nameEn} onChange={set("nameEn")} {...common} dir="ltr" maxLength={200} autoFocus={id === null} />
          <TextField name="nameAr" label={t("tenancy.branch.nameAr")} value={draft.nameAr} onChange={set("nameAr")} {...common} dir="rtl" maxLength={200} />
          <TextField name="code" label={t("tenancy.branch.code")} value={draft.code} onChange={set("code")} {...common} dir="ltr" maxLength={20} upper hint={t("tenancy.company.codeHint")} />
          <CheckField name="isActive" label={t("tenancy.common.active")} checked={draft.isActive} onChange={set("isActive")} />
        </div>
      </fieldset>
      <AddressFields draft={draft} set={set} errors={errors} disabled={!editable} />
      <fieldset disabled={!editable}>
        <legend>{t("tenancy.company.contact")}</legend>
        <div className="form-grid">
          <TextField name="phone" label={t("tenancy.address.phone")} value={draft.phone} onChange={set("phone")} {...common} dir="ltr" type="tel" maxLength={30} />
          <TextField name="email" label={t("tenancy.address.email")} value={draft.email} onChange={set("email")} {...common} dir="ltr" type="email" maxLength={254} />
        </div>
      </fieldset>
    </form>
  );
}
