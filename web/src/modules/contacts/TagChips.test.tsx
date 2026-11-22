import { afterEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import { FilterByWordContext } from "../../kernel/lists/filterByField";
import { render, settle, type Rendered } from "../../test/render";
import { TagChips } from "./TagChips";

let view: Rendered | undefined;
afterEach(() => {
  view?.unmount();
  view = undefined;
});

describe("TagChips", () => {
  it("draws each tag as a button that asks the list for the contacts holding it, and none for an empty cell", async () => {
    const asked: [string, string][] = [];
    view = await render(
      <I18nProvider>
        <FilterByWordContext.Provider value={(column, word) => asked.push([column, word])}>
          <TagChips value="VIP; Government" />
          <TagChips value={null} />
        </FilterByWordContext.Provider>
      </I18nProvider>,
    );
    await settle();
    const buttons = [...document.querySelectorAll<HTMLButtonElement>("button.ct-tag")];
    expect(buttons.map((b) => b.textContent)).toEqual(["VIP", "Government"]);
    expect(buttons[1]!.getAttribute("aria-label")).toBe("Show the contacts tagged Government");
    expect(buttons[1]!.tabIndex).toBe(-1);
    buttons[1]!.click();
    expect(asked).toEqual([["tags", "Government"]]);
  });
});
