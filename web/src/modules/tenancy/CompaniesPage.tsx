import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { CompanyForm } from "./CompanyForm";
import { listSearch, newRecord, useRecordPanel } from "./records";
import { useScreenKeys } from "./ui";

/**
 * Companies of the workspace (those the user may work in): the shared list (search as you type,
 * filters, sort, grouping, views) with the open company's form in its panel (?open=id, or
 * ?open=new). Keyboard: "/" search, arrows move, Enter opens, Alt+N new, Ctrl+S save, Escape
 * closes the form.
 */
export function CompaniesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(can("tenancy.companies.create"));
  useScreenKeys({ onNew: panel.startNew, search: listSearch });

  return (
    <div className="tn-list">
      <ListView
        listKey="tenancy.companies"
        titleKey="tenancy.companies.title"
        countKey="tenancy.companies.count"
        searchPlaceholderKey="tenancy.companies.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close) => (
          <CompanyForm key={panel.formKey} id={id === newRecord ? null : id} onSaved={panel.saved} onClose={close} />
        )}
        actions={
          panel.startNew && (
            <button type="button" className="button primary" onClick={panel.startNew} title={t("tenancy.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("tenancy.common.new")}
            </button>
          )
        }
        renderCell={{
          code: (c) => <span dir="ltr">{String(c.code ?? "")}</span>,
          legalNameEn: (c) => <span dir="ltr">{String(c.legalNameEn ?? "")}</span>,
          legalNameAr: (c) => <span dir="rtl">{String(c.legalNameAr ?? "")}</span>,
          baseCurrency: (c) => <span dir="ltr">{String(c.baseCurrency ?? "")}</span>,
          isActive: (c) => (c.isActive ? t("tenancy.common.active") : <span className="muted">{t("tenancy.common.inactive")}</span>),
        }}
      />
    </div>
  );
}
