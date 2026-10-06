import { useEffect, useMemo, useState } from "react";
import { api } from "../../kernel/api";
import { BooleanField, SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView, type ReferenceSource } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { AddressFields } from "./CompanyForm";
import { listSearch, loadAll, newRecord, useRecordPanel } from "./records";
import { companiesChanged, type Branch, type CompanyRow } from "./types";
import { useLocalName, useScreenKeys, type Emirate } from "./ui";

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
        renderRecord={(id, close, nav) => (
          <BranchForm
            key={panel.formKey}
            nav={nav}
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
};

const optional = (value: string) => (value.trim() === "" ? null : value.trim());

function draftOf(b: Branch | null, defaultCompanyId: string): Draft {
  return {
    companyId: b?.companyId ?? defaultCompanyId,
    code: b?.code ?? "",
    nameEn: b?.nameEn ?? "",
    nameAr: b?.nameAr ?? "",
    addressLine1: b?.addressLine1 ?? "",
    addressLine2: b?.addressLine2 ?? "",
    city: b?.city ?? "",
    emirate: b?.emirate ?? "",
    poBox: b?.poBox ?? "",
    country: b?.country ?? "AE",
    addressAr: b?.addressAr ?? "",
    phone: b?.phone ?? "",
    email: b?.email ?? "",
    isActive: b?.isActive ?? true,
  };
}

function BranchForm({
  id,
  companies,
  defaultCompanyId,
  onSaved,
  onClose,
  nav,
}: {
  id: string | null;
  companies: CompanyRow[];
  defaultCompanyId: string;
  onSaved: (id: string) => void;
  onClose: () => void;
  nav?: RecordNavigation;
}) {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const form = useRecordForm<Branch, Draft>({
    load: id === null ? undefined : (signal) => api<Branch>("GET", `/api/tenancy/branches/${id}`, undefined, { signal }),
    initial: (b) => draftOf(b, defaultCompanyId),
    canEdit: id === null ? can("tenancy.branches.create") : can("tenancy.branches.update"),
    save: (draft, branch) => {
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
        version: branch?.version ?? null,
      };
      return branch === null ? api<Branch>("POST", "/api/tenancy/branches", body) : api<Branch>("PUT", `/api/tenancy/branches/${branch.id}`, body);
    },
    onSaved: (saved) => {
      onSaved(saved.id);
      window.dispatchEvent(new Event(companiesChanged));
    },
  }, id);
  const branch = form.record;
  const bind = form.bind;
  return (
    <RecordForm
      form={form}
      label={t("tenancy.branch.form")}
      title={branch ? `${branch.companyCode} · ${branch.code}` : t("tenancy.branch.new")}
      subtitle={branch ? name(branch.nameEn, branch.nameAr) : undefined}
      onClose={onClose}
      nav={nav}
    >
      <FormSection title={t("tenancy.company.general")}>
        <SelectField
          field={bind("companyId")}
          label={t("tenancy.branch.company")}
          options={companies.map((c) => ({ value: c.id, label: `${c.code} · ${name(c.legalNameEn, c.legalNameAr)}` }))}
          empty={companies.length === 0 ? (branch?.companyCode ?? "") : undefined}
          disabled={id !== null}
        />
        <TextField field={bind("nameEn")} label={t("tenancy.branch.nameEn")} dir="ltr" maxLength={200} autoFocus={id === null} />
        <TextField field={bind("nameAr")} label={t("tenancy.branch.nameAr")} dir="rtl" maxLength={200} />
        <TextField field={bind("code")} label={t("tenancy.branch.code")} dir="ltr" maxLength={20} upper hint={t("tenancy.company.codeHint")} />
        <BooleanField field={bind("isActive")} label={t("tenancy.common.active")} />
      </FormSection>
      <AddressFields form={form} />
      <FormSection title={t("tenancy.company.contact")}>
        <TextField field={bind("phone")} label={t("tenancy.address.phone")} dir="ltr" type="tel" maxLength={30} />
        <TextField field={bind("email")} label={t("tenancy.address.email")} dir="ltr" type="email" maxLength={254} />
      </FormSection>
    </RecordForm>
  );
}
