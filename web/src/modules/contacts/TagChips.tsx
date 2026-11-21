import { useContext } from "react";
import { useI18n } from "../../kernel/i18n";
import { FilterByWordContext } from "../../kernel/lists/filterByField";

/**
 * A contact's tags in the list: each tag is a button that lists the contacts holding it (one click, nothing to type), the way a person
 * reads a tag cell. The buttons stay out of the Tab order (the grid's own keys and the column's Filter menu do the same for the keyboard).
 */
export function TagChips({ value }: { value: unknown }) {
  const { t } = useI18n();
  const filterByWord = useContext(FilterByWordContext);
  const tags = String(value ?? "")
    .split(";")
    .map((tag) => tag.trim())
    .filter(Boolean);
  return (
    <span className="ct-tags" dir="auto">
      {tags.map((tag) => (
        <button
          key={tag}
          type="button"
          tabIndex={-1}
          className="ct-tag"
          title={t("contacts.tags.filterBy", { tag })}
          aria-label={t("contacts.tags.filterBy", { tag })}
          onClick={(e) => {
            e.stopPropagation();
            filterByWord?.("tags", tag);
          }}
          onDoubleClick={(e) => e.stopPropagation()}
        >
          {tag}
        </button>
      ))}
    </span>
  );
}
