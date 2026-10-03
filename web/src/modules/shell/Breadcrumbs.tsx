import { useI18n } from "../../kernel/i18n";
import { Icon } from "../../kernel/icons";
import { Link } from "../../kernel/router";
import type { MenuItem } from "../../kernel/session";
import { groupLabelKey } from "./navigation";
import { hasString } from "../../kernel/i18n";

/**
 * Where the user is: Home › group › screen. The separators point in the reading direction, so
 * they mirror on Arabic screens. Not shown on the home screen itself.
 */
export function Breadcrumbs({ entry, titleKey }: { entry: MenuItem | undefined; titleKey: string }) {
  const { t } = useI18n();
  const group = entry?.group && hasString(groupLabelKey(entry.group)) ? groupLabelKey(entry.group) : null;
  return (
    <nav className="breadcrumbs" aria-label={t("shell.breadcrumb")}>
      <ol>
        <li>
          <Link to="/">{t("shell.nav.home")}</Link>
          <Icon name="chevron" size={12} className="crumb-sep" />
        </li>
        {group && (
          <li>
            <span>{t(group)}</span>
            <Icon name="chevron" size={12} className="crumb-sep" />
          </li>
        )}
        <li aria-current="page">{t(titleKey)}</li>
      </ol>
    </nav>
  );
}
