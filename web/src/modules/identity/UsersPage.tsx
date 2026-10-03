import { ListView } from "../../kernel/lists/ListView";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";

/** The users list on the shared list framework (search, filters, sort, views, keyboard). */
export function UsersPage() {
  const { t } = useI18n();
  const { can } = useSession();
  return (
    <ListView
      listKey="identity.users"
      titleKey="identity.users.title"
      countKey="identity.users.count"
      searchPlaceholderKey="identity.users.search"
      can={can}
      renderCell={{
        email: (u) => <span dir="ltr">{String(u.email ?? "")}</span>,
        isActive: (u) => (u.isActive ? t("identity.users.active") : t("identity.users.inactive")),
        lastSignInAt: (u) => (u.lastSignInAt ? undefined : <span className="muted">{t("identity.users.never")}</span>),
      }}
    />
  );
}
