import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { AddressFields } from "./CompanyForm";
import type { Branch, BranchRow, CompanyRow, Page } from "./types";
import {
  CheckField,
  gridKeys,
  problemOf,
  readSelection,
  SelectField,
  TextField,
  useLocalName,
  useScreenKeys,
  writeSelection,
  type Emirate,
  type FieldErrors,
} from "./ui";

const pageSize = 100;

/** Branches of every company the user may work in, filterable by company, with the form beside. */
export function BranchesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [companies, setCompanies] = useState<CompanyRow[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [search, setSearch] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const [page, setPage] = useState<Page<BranchRow> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(() => readSelection().id);
  const [creating, setCreating] = useState(() => readSelection().isNew && can("tenancy.branches.create"));
  const [reload, setReload] = useState(0);
  const searchRef = useRef<HTMLInputElement>(null);
  const gridRef = useRef<HTMLTableSectionElement>(null);

  useEffect(() => {
    if (!can("tenancy.companies.read")) return;
    api<Page<CompanyRow>>("GET", "/api/tenancy/companies?take=200")
      .then((p) => setCompanies(p.items))
      .catch(() => setCompanies([]));
  }, [can]);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ take: String(pageSize) });
      if (search.trim()) query.set("search", search.trim());
      if (companyId) query.set("companyId", companyId);
      if (!showInactive) query.set("isActive", "true");
      api<Page<BranchRow>>("GET", `/api/tenancy/branches?${query}`)
        .then((p) => {
          if (!controller.signal.aborted) {
            setPage(p);
            setError(null);
          }
        })
        .catch((e: Error) => setError(e.message));
    }, 150);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [search, companyId, showInactive, reload]);

  const open = useCallback((id: string | null, isNew = false) => {
    setSelected(id);
    setCreating(isNew);
    writeSelection(id, isNew);
  }, []);
  const startNew = can("tenancy.branches.create") ? () => open(null, true) : undefined;
  useScreenKeys({ onNew: startNew, search: searchRef });
  const rows = page?.items ?? [];

  return (
    <section className="split">
      <div className="split-list">
        <div className="screen-header">
          <h1>{t("tenancy.branches.title")}</h1>
          {startNew && (
            <button type="button" className="button primary" onClick={startNew} title={t("tenancy.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("tenancy.common.new")}
            </button>
          )}
        </div>
        <div className="toolbar">
          <input
            ref={searchRef}
            type="search"
            className="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("tenancy.branches.search")}
            aria-label={t("tenancy.branches.search")}
            aria-keyshortcuts="/"
          />
          {companies.length > 0 && (
            <select value={companyId} onChange={(e) => setCompanyId(e.target.value)} aria-label={t("tenancy.branch.company")}>
              <option value="">{t("tenancy.branches.allCompanies")}</option>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} · {name(c.legalNameEn, c.legalNameAr)}
                </option>
              ))}
            </select>
          )}
          <label className="check">
            <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
            <span>{t("tenancy.common.showInactive")}</span>
          </label>
          {page && <span className="muted">{t("tenancy.branches.count", { count: page.total })}</span>}
        </div>
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        <table className="grid selectable" aria-label={t("tenancy.branches.title")}>
          <thead>
            <tr>
              <th scope="col">{t("tenancy.branch.company")}</th>
              <th scope="col">{t("tenancy.branch.code")}</th>
              <th scope="col">{t("tenancy.branch.name")}</th>
              <th scope="col">{t("tenancy.address.city")}</th>
              <th scope="col">{t("tenancy.address.emirate")}</th>
              <th scope="col">{t("tenancy.common.status")}</th>
            </tr>
          </thead>
          <tbody ref={gridRef} tabIndex={0} onKeyDown={gridKeys(rows, selected, setSelected, (id) => open(id))}>
            {rows.map((b) => (
              <tr key={b.id} aria-selected={b.id === selected} className={b.isActive ? undefined : "inactive"} onClick={() => open(b.id)} data-id={b.id}>
                <td dir="ltr">{b.companyCode}</td>
                <td dir="ltr">{b.code}</td>
                <td>{name(b.nameEn, b.nameAr)}</td>
                <td>{b.city}</td>
                <td>{b.emirate ? t(`tenancy.emirate.${b.emirate}`) : ""}</td>
                <td>{b.isActive ? t("tenancy.common.active") : t("tenancy.common.inactive")}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {page && page.items.length === 0 && <p className="muted">{t("tenancy.branches.none")}</p>}
      </div>
      {(selected || creating) && (
        <div className="split-form">
          <BranchForm
            key={creating ? "new" : selected}
            id={creating ? null : selected}
            companies={companies}
            defaultCompanyId={companyId || companies[0]?.id || ""}
            onSaved={(id) => {
              open(id);
              setReload((n) => n + 1);
            }}
            onClose={() => {
              open(null);
              gridRef.current?.focus();
            }}
          />
        </div>
      )}
    </section>
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
          <TextField name="code" label={t("tenancy.branch.code")} value={draft.code} onChange={set("code")} {...common} dir="ltr" maxLength={20} required autoFocus={id === null} upper hint={t("tenancy.company.codeHint")} />
          <TextField name="nameEn" label={t("tenancy.branch.nameEn")} value={draft.nameEn} onChange={set("nameEn")} {...common} dir="ltr" maxLength={200} required />
          <TextField name="nameAr" label={t("tenancy.branch.nameAr")} value={draft.nameAr} onChange={set("nameAr")} {...common} dir="rtl" maxLength={200} required />
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
